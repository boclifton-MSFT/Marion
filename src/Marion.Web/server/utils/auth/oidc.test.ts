import { createSign, generateKeyPairSync, type KeyObject } from 'node:crypto'
import { describe, expect, it } from 'vitest'
import * as oidc from 'openid-client'
import { completeAuthorization } from './oidc'
import type { OAuthTransaction } from './security'
import type { OidcRuntimeSettings } from './runtime'

const settings: OidcRuntimeSettings = {
  issuer: 'https://issuer.example.test',
  clientId: 'marion-client',
  clientSecret: 'test-client-secret',
  redirectUri: 'https://localhost:7257/auth/google/callback'
}

const transaction: OAuthTransaction = {
  transactionId: 'transaction-id',
  state: 'state-value',
  nonce: 'nonce-value',
  codeVerifier: 'pkce-verifier',
  returnTo: '/',
  issuedAt: 1_750_000_000_000
}

function base64Json(value: object): string {
  return Buffer.from(JSON.stringify(value)).toString('base64url')
}

function signedIdToken(
  privateKey: KeyObject,
  nowSeconds: number,
  claimOverrides: Record<string, unknown> = {}
): string {
  const signed = `${base64Json({ alg: 'RS256', kid: 'test-key', typ: 'JWT' })}.${base64Json({
    iss: settings.issuer,
    aud: settings.clientId,
    sub: 'provider-subject',
    nonce: transaction.nonce,
    iat: nowSeconds - 20,
    exp: nowSeconds + 10 * 60,
    ...claimOverrides
  })}`
  const signer = createSign('RSA-SHA256')
  signer.update(signed)
  signer.end()
  return `${signed}.${signer.sign(privateKey).toString('base64url')}`
}

function configuration(
  nowSeconds: number,
  claimOverrides: Record<string, unknown> = {},
  tamperKey = false
): oidc.Configuration {
  const { privateKey, publicKey } = generateKeyPairSync('rsa', { modulusLength: 2048 })
  const jwk = publicKey.export({ format: 'jwk' })
  const token = signedIdToken(privateKey as KeyObject, nowSeconds, claimOverrides)
  const config = new oidc.Configuration({
    issuer: settings.issuer,
    authorization_endpoint: `${settings.issuer}/authorize`,
    token_endpoint: `${settings.issuer}/token`,
    jwks_uri: `${settings.issuer}/jwks`
  }, settings.clientId, { client_secret: settings.clientSecret })

  config[oidc.customFetch] = async (input, init) => {
    const url = new URL(input.toString())
    if (url.pathname === '/jwks') {
      return Response.json({
        keys: [{ ...jwk, kid: tamperKey ? 'other-key' : 'test-key', alg: 'RS256', use: 'sig' }]
      })
    }
    if (url.pathname === '/token') {
      expect(String(init?.body)).toContain('code=authorization-code')
      return Response.json({
        access_token: 'test-access-token',
        token_type: 'Bearer',
        id_token: token
      })
    }
    throw new Error(`Unexpected OIDC request: ${url}`)
  }
  oidc.enableNonRepudiationChecks(config)
  return config
}

describe('OIDC ID-token verification', () => {
  it('accepts a token whose signature validates against the issuer JWKS', async () => {
    const now = Date.now()
    const config = configuration(Math.floor(now / 1000))

    await expect(completeAuthorization(
      config,
      settings,
      transaction,
      '?code=authorization-code&state=state-value',
      now
    )).resolves.toEqual({
      issuer: settings.issuer,
      subject: 'provider-subject'
    })
  })

  it('rejects an ID token when no issuer JWKS key validates its signature', async () => {
    const now = Date.now()
    const config = configuration(Math.floor(now / 1000), {}, true)

    await expect(completeAuthorization(
      config,
      settings,
      transaction,
      '?code=authorization-code&state=state-value',
      now
    )).rejects.toThrow()
  })

  it.each([
    ['issuer', { iss: 'https://attacker.example.test' }],
    ['audience', { aud: 'another-client' }],
    ['nonce', { nonce: 'another-nonce' }],
    ['expiry', { exp: 1 }],
    ['missing subject', { sub: undefined }],
    ['empty subject', { sub: '' }]
  ])('fails closed for an invalid %s in a signed provider token', async (_, claimOverrides) => {
    const now = Date.now()
    const config = configuration(Math.floor(now / 1000), claimOverrides)

    const identity = await completeAuthorization(
      config,
      settings,
      transaction,
      '?code=authorization-code&state=state-value',
      now
    ).catch(() => undefined)

    expect(identity).toBeUndefined()
  })
})
