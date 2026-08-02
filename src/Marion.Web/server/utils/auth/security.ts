import { createHash, timingSafeEqual } from 'node:crypto'
import type { SessionConfig } from 'h3'

export const DEFAULT_RETURN_TO = '/'
export const ID_TOKEN_CLOCK_SKEW_SECONDS = 60
export const ID_TOKEN_MAX_AGE_SECONDS = 10 * 60
export const OAUTH_TRANSACTION_MAX_AGE_SECONDS = 5 * 60
export const SESSION_IDLE_TIMEOUT_SECONDS = 30 * 60
export const SESSION_ABSOLUTE_TIMEOUT_SECONDS = 8 * 60 * 60
export const SESSION_COOKIE_NAME = '__Host-marion_session'
export const TRANSACTION_COOKIE_NAME = '__Host-marion_oauth_tx'

export interface OAuthTransaction {
  transactionId: string
  state: string
  nonce: string
  codeVerifier: string
  returnTo: string
  issuedAt: number
}

export interface Clock {
  now(): number
}

export interface RandomSource {
  uuid(): string
  state(): string
  nonce(): string
  pkceVerifier(): string
}

export interface MarionSession {
  sessionId: string
  userId: string
  issuedAt: number
  lastActiveAt: number
}

export interface ExternalIdentity {
  issuer: string
  subject: string
}

export interface IdTokenValidationOptions {
  issuer: string
  clientId: string
  nonce: string
  now: number
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0
}

function isTimestamp(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

export function safeReturnTo(value: unknown): string {
  if (!isNonEmptyString(value) || !value.startsWith('/') || value.startsWith('//')) {
    return DEFAULT_RETURN_TO
  }

  let decoded: string
  try {
    decoded = decodeURIComponent(value)
  } catch {
    return DEFAULT_RETURN_TO
  }

  if (value.includes('\\') || decoded.includes('\\') || decoded.startsWith('//')) {
    return DEFAULT_RETURN_TO
  }

  try {
    const target = new URL(value, 'https://marion.invalid')
    return target.origin === 'https://marion.invalid'
      ? `${target.pathname}${target.search}${target.hash}`
      : DEFAULT_RETURN_TO
  } catch {
    return DEFAULT_RETURN_TO
  }
}

export function createOAuthTransaction(
  values: Pick<OAuthTransaction, 'state' | 'nonce' | 'codeVerifier'>,
  transactionId: string,
  returnTo: string,
  now: number
): OAuthTransaction {
  return {
    transactionId,
    state: values.state,
    nonce: values.nonce,
    codeVerifier: values.codeVerifier,
    returnTo: safeReturnTo(returnTo),
    issuedAt: now
  }
}

export function isCurrentOAuthTransaction(value: unknown, now: number): value is OAuthTransaction {
  if (!value || typeof value !== 'object') {
    return false
  }

  const transaction = value as Partial<OAuthTransaction>
  return isNonEmptyString(transaction.transactionId)
    && isNonEmptyString(transaction.state)
    && isNonEmptyString(transaction.nonce)
    && isNonEmptyString(transaction.codeVerifier)
    && isNonEmptyString(transaction.returnTo)
    && isTimestamp(transaction.issuedAt)
    && transaction.issuedAt <= now
    && now - transaction.issuedAt <= OAUTH_TRANSACTION_MAX_AGE_SECONDS * 1000
    && safeReturnTo(transaction.returnTo) === transaction.returnTo
}

export function createMarionSession(userId: string, sessionId: string, now: number): MarionSession {
  return {
    sessionId,
    userId,
    issuedAt: now,
    lastActiveAt: now
  }
}

export function sessionIsActive(session: unknown, now: number): session is MarionSession {
  if (!session || typeof session !== 'object') {
    return false
  }

  const candidate = session as Partial<MarionSession>
  if (!isNonEmptyString(candidate.sessionId)
    || !isNonEmptyString(candidate.userId)
    || !isTimestamp(candidate.issuedAt)
    || !isTimestamp(candidate.lastActiveAt)
    || candidate.lastActiveAt < candidate.issuedAt
    || candidate.issuedAt > now) {
    return false
  }

  return now - candidate.lastActiveAt <= SESSION_IDLE_TIMEOUT_SECONDS * 1000
    && now - candidate.issuedAt <= SESSION_ABSOLUTE_TIMEOUT_SECONDS * 1000
}

export function validateIdTokenClaims(
  claims: Record<string, unknown> | undefined,
  options: IdTokenValidationOptions
): ExternalIdentity | undefined {
  if (!claims) {
    return
  }

  const audience = claims.aud
  const audiences = Array.isArray(audience) ? audience : [audience]
  const validAudience = audiences.every(isNonEmptyString) && audiences.includes(options.clientId)
  const authorizedParty = claims.azp
  const validAuthorizedParty = authorizedParty === undefined
    ? audiences.length === 1
    : authorizedParty === options.clientId

  if (!isNonEmptyString(claims.iss)
    || claims.iss !== options.issuer
    || !isNonEmptyString(claims.sub)
    || !validAudience
    || !validAuthorizedParty
    || !isTimestamp(claims.exp)
    || claims.exp < options.now - ID_TOKEN_CLOCK_SKEW_SECONDS
    || !isTimestamp(claims.iat)
    || claims.iat > options.now + ID_TOKEN_CLOCK_SKEW_SECONDS
    || claims.iat < options.now - ID_TOKEN_MAX_AGE_SECONDS
    || !isNonEmptyString(claims.nonce)
    || !constantTimeEquals(claims.nonce, options.nonce)) {
    return
  }

  return {
    issuer: claims.iss,
    subject: claims.sub
  }
}

export function externalIdentityKey(identity: ExternalIdentity): string {
  const hash = createHash('sha256')
    .update(identity.issuer)
    .update('\u0000')
    .update(identity.subject)
    .digest('base64url')

  return `identities:${hash}`
}

export function constantTimeEquals(left: string, right: string): boolean {
  const leftBuffer = Buffer.from(left)
  const rightBuffer = Buffer.from(right)

  return leftBuffer.length === rightBuffer.length && timingSafeEqual(leftBuffer, rightBuffer)
}

export function sessionCookieConfig(password: string, clear = false): SessionConfig {
  return {
    name: SESSION_COOKIE_NAME,
    password,
    maxAge: SESSION_ABSOLUTE_TIMEOUT_SECONDS,
    sessionHeader: false,
    cookie: {
      secure: true,
      httpOnly: true,
      sameSite: 'lax',
      path: '/',
      ...(clear ? { maxAge: 0 } : {})
    }
  }
}

export function transactionCookieConfig(password: string, clear = false): SessionConfig {
  return {
    name: TRANSACTION_COOKIE_NAME,
    password,
    maxAge: OAUTH_TRANSACTION_MAX_AGE_SECONDS,
    sessionHeader: false,
    cookie: {
      secure: true,
      httpOnly: true,
      sameSite: 'lax',
      path: '/',
      ...(clear ? { maxAge: 0 } : {})
    }
  }
}

export function isTrustedRequestOrigin(origin: string | undefined, redirectUri: string): boolean {
  if (!origin) {
    return false
  }

  try {
    return new URL(origin).origin === new URL(redirectUri).origin
  } catch {
    return false
  }
}
