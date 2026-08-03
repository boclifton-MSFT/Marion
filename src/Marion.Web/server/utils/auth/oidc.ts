import * as oidc from 'openid-client'
import type { OAuthTransaction, ExternalIdentity } from './security'
import { validateIdTokenClaims } from './security'
import type { OidcRuntimeSettings } from './runtime'

export interface OidcClient {
  authorizationUrl(settings: OidcRuntimeSettings, transaction: OAuthTransaction): Promise<URL>
  exchangeCode(
    settings: OidcRuntimeSettings,
    transaction: OAuthTransaction,
    search: string,
    now: number
  ): Promise<ExternalIdentity | undefined>
}

let configuration: Promise<oidc.Configuration> | undefined

export async function oidcConfiguration(settings: OidcRuntimeSettings): Promise<oidc.Configuration> {
  configuration ||= oidc.discovery(
    new URL(settings.issuer),
    settings.clientId,
    {
      client_secret: settings.clientSecret,
      redirect_uris: [settings.redirectUri]
    }
  ).then((discovered) => {
    oidc.enableNonRepudiationChecks(discovered)
    return discovered
  }).catch((error: unknown) => {
    configuration = undefined
    throw error
  })

  return configuration
}

export async function authorizationUrl(
  configuration: oidc.Configuration,
  settings: OidcRuntimeSettings,
  transaction: OAuthTransaction
): Promise<URL> {
  const codeChallenge = await oidc.calculatePKCECodeChallenge(transaction.codeVerifier)
  return oidc.buildAuthorizationUrl(configuration, {
    redirect_uri: transaction.redirectUri,
    scope: 'openid email profile',
    state: transaction.state,
    nonce: transaction.nonce,
    code_challenge: codeChallenge,
    code_challenge_method: 'S256'
  })
}

export async function completeAuthorization(
  configuration: oidc.Configuration,
  settings: OidcRuntimeSettings,
  transaction: OAuthTransaction,
  search: string,
  now: number
): Promise<ExternalIdentity | undefined> {
  const callbackUrl = new URL(transaction.redirectUri)
  callbackUrl.search = search

  const tokens = await oidc.authorizationCodeGrant(configuration, callbackUrl, {
    expectedState: transaction.state,
    expectedNonce: transaction.nonce,
    pkceCodeVerifier: transaction.codeVerifier,
    idTokenExpected: true
  })

  return validateIdTokenClaims(tokens.claims(), {
    issuer: settings.issuer,
    clientId: settings.clientId,
    nonce: transaction.nonce,
    now: Math.floor(now / 1000)
  })
}

export const openIdConnectClient: OidcClient = {
  async authorizationUrl(settings, transaction) {
    return authorizationUrl(await oidcConfiguration(settings), settings, transaction)
  },
  async exchangeCode(settings, transaction, search, now) {
    return completeAuthorization(
      await oidcConfiguration(settings),
      settings,
      transaction,
      search,
      now
    )
  }
}
