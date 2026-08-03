import { createSign, generateKeyPairSync, type KeyObject } from 'node:crypto'
import { describe, expect, it } from 'vitest'
import * as oidc from 'openid-client'
import { authorizationUrl, completeAuthorization } from './oidc'
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
  redirectUri: settings.redirectUri,
  returnTo: '/',
  issuedAt: 1_750_000_000_000
}

function base64Json(value: object): string {
  return Buffer.from(JSON.stringify(value)).toString('base64url')
}

function signedIdToken(
  privateKey: KeyObject,
  nowSeconds: number
): string {
  const signed = `${base64Json({ alg: 'RS256', kid: 'test-key', typ: 'JWT' })}.${base64Json({
    iss: settings.issuer,
    aud: settings.clientId,
    sub: 'provider-subject',
    nonce: transaction.nonce,
    iat: nowSeconds - 20,
    exp: nowSeconds + 10 * 60
  })}`
  const signer = createSign('RSA-SHA256')
  signer.update(signed)
  signer.end()
  return `${signed}.${signer.sign(privateKey).toString('base64url')}`
}

function configuration(nowSeconds: number, tamperKey = false): oidc.Configuration {
  const { privateKey, publicKey } = generateKeyPairSync('rsa', { modulusLength: 2048 })
  const jwk = publicKey.export({ format: 'jwk' })
  const token = signedIdToken(privateKey as KeyObject, nowSeconds)
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
      expect(String(init?.body)).toContain(`redirect_uri=${encodeURIComponent(transaction.redirectUri)}`)
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
  it('builds an authorization request with every required OIDC parameter', async () => {
    const config = configuration(Math.floor(Date.now() / 1000))
    const url = await authorizationUrl(config, {
      ...settings,
      redirectUri: 'https://changed.example.test/auth/google/callback'
    }, transaction)

    expect(url.searchParams.get('response_type')).toBe('code')
    expect(url.searchParams.get('state')).toBe(transaction.state)
    expect(url.searchParams.get('nonce')).toBe(transaction.nonce)
    expect(url.searchParams.get('code_challenge')).toBe(
      await oidc.calculatePKCECodeChallenge(transaction.codeVerifier)
    )
    expect(url.searchParams.get('code_challenge_method')).toBe('S256')
    expect(url.searchParams.get('redirect_uri')).toBe(transaction.redirectUri)
    expect(url.searchParams.getAll('scope')).toEqual(['openid email profile'])
  })

  it('accepts a token whose signature validates against the issuer JWKS', async () => {
    const now = Date.now()
    const config = configuration(Math.floor(now / 1000))

    await expect(completeAuthorization(
      config,
      { ...settings, redirectUri: 'https://changed.example.test/auth/google/callback' },
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
    const config = configuration(Math.floor(now / 1000), true)

    await expect(completeAuthorization(
      config,
      settings,
      transaction,
      '?code=authorization-code&state=state-value',
      now
    )).rejects.toThrow()
  })
})
