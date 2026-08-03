import { describe, expect, it } from 'vitest'
import { calculatePKCECodeChallenge } from 'openid-client'
import {
  DEFAULT_RETURN_TO,
  ID_TOKEN_MAX_AGE_SECONDS,
  OAUTH_TRANSACTION_MAX_AGE_SECONDS,
  SESSION_ABSOLUTE_TIMEOUT_SECONDS,
  SESSION_IDLE_TIMEOUT_SECONDS,
  SESSION_COOKIE_NAME,
  TRANSACTION_COOKIE_NAME,
  createMarionSession,
  createOAuthTransaction,
  isCurrentOAuthTransaction,
  safeReturnTo,
  sessionCookieConfig,
  sessionIsActive,
  transactionCookieConfig,
  validateIdTokenClaims
} from './security'

describe('OIDC transaction security', () => {
  it('uses the S256 PKCE challenge required by the authorization-code flow', async () => {
    const verifier = 'dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk'

    await expect(calculatePKCECodeChallenge(verifier))
      .resolves.toBe('E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM')
  })

  it('preserves one authorization request values for at most five minutes', () => {
    const clock = {
      now: () => 1_750_000_000_000
    }
    const random = {
      uuid: () => 'transaction-id',
      state: () => 'state',
      nonce: () => 'nonce',
      pkceVerifier: () => 'verifier'
    }
    const now = clock.now()
    const transaction = createOAuthTransaction({
      state: random.state(),
      nonce: random.nonce(),
      codeVerifier: random.pkceVerifier()
    }, random.uuid(), '/pricing?source=google', now)

    expect(transaction).toMatchObject({
      state: 'state',
      nonce: 'nonce',
      codeVerifier: 'verifier',
      returnTo: '/pricing?source=google',
      issuedAt: now
    })
    expect(isCurrentOAuthTransaction(transaction, now + OAUTH_TRANSACTION_MAX_AGE_SECONDS * 1000)).toBe(true)
    expect(isCurrentOAuthTransaction(transaction, now + OAUTH_TRANSACTION_MAX_AGE_SECONDS * 1000 + 1)).toBe(false)
  })

  it.each([
    ['', DEFAULT_RETURN_TO],
    ['https://attacker.example', DEFAULT_RETURN_TO],
    ['//attacker.example', DEFAULT_RETURN_TO],
    ['/%2f%2fattacker.example', DEFAULT_RETURN_TO],
    ['/\\attacker.example', DEFAULT_RETURN_TO],
    ['/docs/getting-started?section=oidc', '/docs/getting-started?section=oidc']
  ])('only accepts local return paths: %s', (value, expected) => {
    expect(safeReturnTo(value)).toBe(expected)
  })
})

describe('ID token identity boundary', () => {
  const now = 1_750_000_000
  const validClaims = {
    iss: 'https://accounts.google.com',
    sub: 'provider-subject',
    aud: 'marion-client',
    azp: 'marion-client',
    exp: now + 60,
    iat: now - 60,
    nonce: 'nonce'
  }
  const options = {
    issuer: 'https://accounts.google.com',
    clientId: 'marion-client',
    nonce: 'nonce',
    now
  }

  it('returns only the validated issuer and subject', () => {
    expect(validateIdTokenClaims({
      ...validClaims,
      email: 'mutable@example.invalid',
      name: 'Mutable profile'
    }, options)).toEqual({
      issuer: validClaims.iss,
      subject: validClaims.sub
    })
  })

  it.each([
    ['issuer', { iss: 'https://attacker.example' }],
    ['audience', { aud: 'other-client' }],
    ['authorized party', { aud: ['marion-client', 'another-client'], azp: 'another-client' }],
    ['expired token', { exp: now - 61 }],
    ['old issue time', { iat: now - ID_TOKEN_MAX_AGE_SECONDS - 1 }],
    ['future issue time', { iat: now + 61 }],
    ['nonce', { nonce: 'other-nonce' }]
  ])('rejects an invalid %s claim', (_, override) => {
    expect(validateIdTokenClaims({ ...validClaims, ...override }, options)).toBeUndefined()
  })
})

describe('sealed cookie policy and session expiry', () => {
  it('requires host-only secure cookies and sends no session header', () => {
    const password = 'a'.repeat(32)
    const session = sessionCookieConfig(password)
    const transaction = transactionCookieConfig(password)
    const sessionCookie = session.cookie
    const transactionCookie = transaction.cookie

    expect(session.name).toBe(SESSION_COOKIE_NAME)
    expect(transaction.name).toBe(TRANSACTION_COOKIE_NAME)
    expect(session.sessionHeader).toBe(false)
    expect(transaction.sessionHeader).toBe(false)
    expect(sessionCookie).toMatchObject({ secure: true, httpOnly: true, sameSite: 'lax', path: '/' })
    expect(transactionCookie).toMatchObject({ secure: true, httpOnly: true, sameSite: 'lax', path: '/' })
    expect((sessionCookie as Exclude<typeof sessionCookie, false | undefined>).domain).toBeUndefined()
    expect((transactionCookie as Exclude<typeof transactionCookie, false | undefined>).domain).toBeUndefined()
    expect(sessionCookieConfig(password, true).cookie).toMatchObject({ maxAge: 0 })
    expect(transactionCookieConfig(password, true).cookie).toMatchObject({ maxAge: 0 })
  })

  it('enforces idle and absolute session limits', () => {
    const issuedAt = 1_750_000_000_000
    const session = createMarionSession('user', 'session', issuedAt)

    expect(sessionIsActive(session, issuedAt + SESSION_IDLE_TIMEOUT_SECONDS * 1000)).toBe(true)
    expect(sessionIsActive(session, issuedAt + SESSION_IDLE_TIMEOUT_SECONDS * 1000 + 1)).toBe(false)
    expect(sessionIsActive({
      ...session,
      lastActiveAt: issuedAt + SESSION_ABSOLUTE_TIMEOUT_SECONDS * 1000
    }, issuedAt + SESSION_ABSOLUTE_TIMEOUT_SECONDS * 1000)).toBe(true)
    expect(sessionIsActive({
      ...session,
      lastActiveAt: issuedAt + SESSION_ABSOLUTE_TIMEOUT_SECONDS * 1000
    }, issuedAt + SESSION_ABSOLUTE_TIMEOUT_SECONDS * 1000 + 1)).toBe(false)
  })

  it('stores only Marion identifiers and timestamps in the session payload', () => {
    expect(Object.keys(createMarionSession('user', 'session', 1_750_000_000_000)).sort()).toEqual([
      'issuedAt',
      'lastActiveAt',
      'sessionId',
      'userId'
    ])
  })
})
