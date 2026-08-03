import type { H3Event } from 'h3'

export interface OidcRuntimeSettings {
  issuer: string
  clientId: string
  clientSecret: string
  redirectUri: string
}

export interface AuthStoreRuntimeSettings {
  connectionString: string
  provisionSchema: boolean
}

interface RuntimeConfigShape {
  oauth?: {
    oidc?: Partial<OidcRuntimeSettings>
  }
  session?: {
    password?: unknown
  }
  authStore?: {
    connectionString?: unknown
    provisionSchema?: unknown
  }
}

export interface AuthConfigValidationOptions {
  production?: boolean
}

function normalizeIssuer(value: string): string | undefined {
  try {
    const issuer = new URL(value)
    if (issuer.protocol !== 'https:' || issuer.search || issuer.hash) {
      return
    }

    const path = issuer.pathname === '/' ? '' : issuer.pathname.replace(/\/+$/, '')
    return `${issuer.origin}${path}`
  } catch {
    return
  }
}

function validRedirectUri(value: string): string | undefined {
  try {
    const redirectUri = new URL(value)
    if (redirectUri.protocol !== 'https:'
      || redirectUri.pathname !== '/auth/google/callback'
      || redirectUri.search
      || redirectUri.hash) {
      return
    }

    return redirectUri.href
  } catch {
    return
  }
}

export function getSessionPassword(config: RuntimeConfigShape): string | undefined {
  const password = config.session?.password
  return typeof password === 'string' && password.length >= 32 ? password : undefined
}

export function getOidcSettings(config: RuntimeConfigShape): OidcRuntimeSettings | undefined {
  const oidc = config.oauth?.oidc
  if (!oidc
    || typeof oidc.issuer !== 'string'
    || typeof oidc.clientId !== 'string'
    || typeof oidc.clientSecret !== 'string'
    || typeof oidc.redirectUri !== 'string'
    || !oidc.clientId
    || !oidc.clientSecret) {
    return
  }

  const issuer = normalizeIssuer(oidc.issuer)
  const redirectUri = validRedirectUri(oidc.redirectUri)
  if (!issuer || !redirectUri) {
    return
  }

  return {
    issuer,
    clientId: oidc.clientId,
    clientSecret: oidc.clientSecret,
    redirectUri
  }
}

export function getAuthStoreSettings(
  config: RuntimeConfigShape
): AuthStoreRuntimeSettings | undefined {
  const connectionString = config.authStore?.connectionString
  if (typeof connectionString !== 'string' || !connectionString.trim()) {
    return
  }

  return {
    connectionString,
    provisionSchema: config.authStore?.provisionSchema === true
  }
}

function isLocalHostname(hostname: string): boolean {
  const normalized = hostname.toLowerCase()
  return normalized === 'localhost'
    || normalized.endsWith('.localhost')
    || normalized === '::1'
    || normalized.startsWith('127.')
}

export function authConfigurationIssues(
  config: RuntimeConfigShape,
  options: AuthConfigValidationOptions = {}
): string[] {
  const issues: string[] = []
  const oidc = getOidcSettings(config)

  if (!oidc) {
    issues.push('NUXT_OAUTH_OIDC_ISSUER, NUXT_OAUTH_OIDC_CLIENT_ID, NUXT_OAUTH_OIDC_CLIENT_SECRET, or NUXT_OAUTH_OIDC_REDIRECT_URI')
  } else if (options.production && isLocalHostname(new URL(oidc.redirectUri).hostname)) {
    issues.push('NUXT_OAUTH_OIDC_REDIRECT_URI must use a public HTTPS origin in production')
  }

  if (!getSessionPassword(config)) {
    issues.push('NUXT_SESSION_PASSWORD')
  }

  if (!getAuthStoreSettings(config)) {
    issues.push('NUXT_AUTH_STORE_CONNECTION_STRING')
  }

  return issues
}

export function authRuntimeConfig(event: H3Event): RuntimeConfigShape {
  return useRuntimeConfig(event) as RuntimeConfigShape
}
