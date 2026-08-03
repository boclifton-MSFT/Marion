import { createServer } from 'node:http'
import { readFile } from 'node:fs/promises'
import { webcrypto } from 'node:crypto'
import { createApp, createRouter, defineEventHandler, toNodeListener } from 'h3'
import { afterEach, describe, expect, it, vi } from 'vitest'
import googleRoute from './google.get'
import callbackRoute from './google/callback.get'
import logoutRoute from './logout.post'
import { bindAuthDependencies, type AuthDependencies } from '../../utils/auth/dependencies'
import type { MarionSession } from '../../utils/auth/security'

if (!globalThis.crypto) {
  Object.defineProperty(globalThis, 'crypto', { value: webcrypto })
}

const sessionPassword = 's'.repeat(32)
const runtimeConfig = {
  oauth: {
    oidc: {
      issuer: 'https://accounts.google.com',
      clientId: 'marion-client',
      clientSecret: 'client-secret-sentinel',
      redirectUri: 'https://localhost:7257/auth/google/callback'
    }
  },
  session: {
    password: sessionPassword
  },
  authStore: {
    connectionString: 'Server=auth-store.invalid;Database=marion',
    provisionSchema: false
  }
}

Object.assign(globalThis, {
  useRuntimeConfig: () => runtimeConfig
})

interface TestAuthState {
  dependencies: AuthDependencies
  authorizationUrl: ReturnType<typeof vi.fn>
  exchangeCode: ReturnType<typeof vi.fn>
  telemetry: string[]
  sessions: Map<string, MarionSession>
}

function createTestAuthState(): TestAuthState {
  const transactions = new Map<string, number>()
  const sessions = new Map<string, MarionSession>()
  const telemetry: string[] = []
  const authorizationUrl = vi.fn(async (settings: { redirectUri: string }) => new URL(
    `https://accounts.google.com/o/oauth2/v2/auth?state=state-sentinel&redirect_uri=${encodeURIComponent(settings.redirectUri)}`
  ))
  const exchangeCode = vi.fn(async () => ({
    issuer: 'https://accounts.google.com',
    subject: 'provider-subject'
  }))
  let generatedId = 0

  return {
    authorizationUrl,
    exchangeCode,
    telemetry,
    sessions,
    dependencies: {
      clock: {
        now: () => 1_750_000_000_000
      },
      random: {
        uuid: () => `generated-${++generatedId}`,
        state: () => 'state-sentinel',
        nonce: () => 'nonce-sentinel',
        pkceVerifier: () => 'verifier-sentinel'
      },
      oidc: {
        authorizationUrl,
        exchangeCode
      },
      telemetry: {
        record: event => telemetry.push(event)
      },
      transactions: {
        create: async (transactionId, expiresAt) => {
          transactions.set(transactionId, expiresAt)
        },
        consume: async (transactionId, now) => {
          const expiresAt = transactions.get(transactionId)
          transactions.delete(transactionId)
          return expiresAt !== undefined && expiresAt >= now
        }
      },
      sessions: {
        create: async (session) => {
          sessions.set(session.sessionId, session)
        },
        get: async sessionId => sessions.get(sessionId) ?? null,
        touch: async (session, now) => {
          const current = sessions.get(session.sessionId)
          if (!current || current.lastActiveAt !== session.lastActiveAt) {
            return null
          }
          const active = { ...current, lastActiveAt: now }
          sessions.set(active.sessionId, active)
          return active
        },
        rotate: async (previousSessionId, session) => {
          if (previousSessionId) {
            sessions.delete(previousSessionId)
          }
          sessions.set(session.sessionId, session)
        },
        revoke: async (sessionId) => {
          sessions.delete(sessionId)
        }
      },
      identities: {
        resolve: async () => 'marion-user'
      }
    }
  }
}

async function startServer(state: TestAuthState) {
  const app = createApp()
  const router = createRouter()
  const withDependencies = (handler: typeof googleRoute) => defineEventHandler((event) => {
    bindAuthDependencies(event, state.dependencies)
    return handler(event)
  })

  router.get('/auth/google', withDependencies(googleRoute))
  router.get('/auth/google/callback', withDependencies(callbackRoute))
  router.post('/auth/logout', withDependencies(logoutRoute))
  app.use(router)

  const server = createServer(toNodeListener(app))
  await new Promise<void>((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => resolve())
  })
  const address = server.address()
  if (!address || typeof address === 'string') {
    throw new Error('Test server did not expose a TCP address.')
  }

  return {
    baseUrl: `http://127.0.0.1:${address.port}`,
    close: async () => new Promise<void>((resolve, reject) =>
      server.close(error => error ? reject(error) : resolve()))
  }
}

function responseCookies(response: Response): string[] {
  const headers = response.headers as Headers & { getSetCookie?: () => string[] }
  return headers.getSetCookie?.() ?? [response.headers.get('set-cookie') ?? '']
}

function cookieNamed(response: Response, name: string): string {
  const cookie = responseCookies(response).find(value => value.startsWith(`${name}=`))
  if (!cookie) {
    throw new Error(
      `Expected ${name} cookie (status ${response.status}, location ${response.headers.get('location')}, cookies ${responseCookies(response).join('|')}).`
    )
  }
  return cookie
}

function requestCookie(value: string): string {
  return value.split(';', 1)[0] ?? ''
}

describe('Google auth route boundary', () => {
  const servers: Array<{ close: () => Promise<void> }> = []

  afterEach(async () => {
    await Promise.all(servers.splice(0).map(server => server.close()))
  })

  it('starts a server-owned code flow with a sealed host-only transaction cookie', async () => {
    const state = createTestAuthState()
    const server = await startServer(state)
    servers.push(server)

    const response = await fetch(`${server.baseUrl}/auth/google?returnTo=/pricing`, {
      redirect: 'manual'
    })
    const cookie = cookieNamed(response, '__Host-marion_oauth_tx')
    const location = response.headers.get('location') ?? ''

    expect(response.status).toBe(302)
    expect(location).toContain('https://accounts.google.com/o/oauth2/v2/auth')
    expect(cookie).toContain('HttpOnly')
    expect(cookie).toContain('Secure')
    expect(cookie).toContain('SameSite=Lax')
    expect(cookie).toContain('Path=/')
    expect(cookie).not.toContain('Domain=')
    expect(`${location}\n${cookie}`).not.toContain('client-secret-sentinel')
    expect(`${location}\n${cookie}`).not.toContain('verifier-sentinel')
    expect(`${location}\n${cookie}`).not.toContain('nonce-sentinel')
    expect(`${location}\n${cookie}`).not.toContain(sessionPassword)
  })

  it('clears a mismatched callback transaction without exchanging or exposing request secrets', async () => {
    const state = createTestAuthState()
    const server = await startServer(state)
    servers.push(server)
    const start = await fetch(`${server.baseUrl}/auth/google`, { redirect: 'manual' })
    const transactionCookie = requestCookie(cookieNamed(start, '__Host-marion_oauth_tx'))

    const callback = await fetch(
      `${server.baseUrl}/auth/google/callback?state=wrong-state&code=authorization-code-sentinel`,
      {
        headers: { cookie: transactionCookie },
        redirect: 'manual'
      }
    )
    const output = `${callback.headers.get('location')}\n${cookieNamed(callback, '__Host-marion_oauth_tx')}\n${await callback.text()}`

    expect(callback.status).toBe(302)
    expect(callback.headers.get('location')).toBe('/login?error=sign-in-failed')
    expect(state.exchangeCode).not.toHaveBeenCalled()
    expect(output).not.toContain('authorization-code-sentinel')
    expect(output).not.toContain('client-secret-sentinel')
    expect(output).not.toContain('verifier-sentinel')
    expect(output).not.toContain('nonce-sentinel')
    expect(output).not.toContain(sessionPassword)
  })

  it('uses the configured HTTPS callback origin behind an HTTP proxy request', async () => {
    const state = createTestAuthState()
    const server = await startServer(state)
    servers.push(server)

    const response = await fetch(`${server.baseUrl}/auth/google`, {
      headers: {
        'forwarded': 'for=192.0.2.1;proto=http;host=forged.example.test',
        'x-forwarded-host': 'forged.example.test',
        'x-forwarded-proto': 'http'
      },
      redirect: 'manual'
    })
    const location = response.headers.get('location') ?? ''

    expect(response.status).toBe(302)
    expect(new URL(location).searchParams.get('redirect_uri'))
      .toBe('https://localhost:7257/auth/google/callback')
    expect(location).not.toContain('forged.example.test')
    expect(state.authorizationUrl).toHaveBeenCalledTimes(1)
  })

  it('rejects provider failures and replays after a transaction has been consumed', async () => {
    const state = createTestAuthState()
    const server = await startServer(state)
    servers.push(server)

    const rejectedStart = await fetch(`${server.baseUrl}/auth/google`, { redirect: 'manual' })
    const rejectedCookie = requestCookie(cookieNamed(rejectedStart, '__Host-marion_oauth_tx'))
    const rejectedCallback = await fetch(
      `${server.baseUrl}/auth/google/callback?state=state-sentinel&error=access_denied&error_description=provider-error-sentinel`,
      {
        headers: { cookie: rejectedCookie },
        redirect: 'manual'
      }
    )
    const acceptedStart = await fetch(`${server.baseUrl}/auth/google?returnTo=/pricing`, {
      redirect: 'manual'
    })
    const replayCookie = requestCookie(cookieNamed(acceptedStart, '__Host-marion_oauth_tx'))
    const acceptedCallback = await fetch(
      `${server.baseUrl}/auth/google/callback?state=state-sentinel&code=authorization-code-sentinel`,
      {
        headers: { cookie: replayCookie },
        redirect: 'manual'
      }
    )
    const replayCallback = await fetch(
      `${server.baseUrl}/auth/google/callback?state=state-sentinel&code=authorization-code-sentinel`,
      {
        headers: { cookie: replayCookie },
        redirect: 'manual'
      }
    )
    const sessionCookie = cookieNamed(acceptedCallback, '__Host-marion_session')
    const output = `${await rejectedCallback.text()}\n${await replayCallback.text()}\n${sessionCookie}`

    expect(rejectedCallback.headers.get('location')).toBe('/login?error=sign-in-failed')
    expect(acceptedCallback.headers.get('location')).toBe('/pricing')
    expect(replayCallback.headers.get('location')).toBe('/login?error=sign-in-failed')
    expect(state.exchangeCode).toHaveBeenCalledTimes(1)
    expect(sessionCookie).toContain('HttpOnly')
    expect(sessionCookie).toContain('Secure')
    expect(sessionCookie).toContain('SameSite=Lax')
    expect(sessionCookie).not.toContain('Domain=')
    expect(output).not.toContain('provider-error-sentinel')
    expect(output).not.toContain('authorization-code-sentinel')
    expect(output).not.toContain('client-secret-sentinel')
    expect(output).not.toContain('provider-subject')
    expect(output).not.toContain('generated-')
  })

  it('revokes the server session and clears its cookie only for the trusted origin', async () => {
    const state = createTestAuthState()
    const server = await startServer(state)
    servers.push(server)
    const start = await fetch(`${server.baseUrl}/auth/google`, { redirect: 'manual' })
    const transactionCookie = requestCookie(cookieNamed(start, '__Host-marion_oauth_tx'))
    const callback = await fetch(
      `${server.baseUrl}/auth/google/callback?state=state-sentinel&code=authorization-code-sentinel`,
      {
        headers: { cookie: transactionCookie },
        redirect: 'manual'
      }
    )
    const sessionCookie = requestCookie(cookieNamed(callback, '__Host-marion_session'))

    const logout = await fetch(`${server.baseUrl}/auth/logout`, {
      method: 'POST',
      headers: {
        cookie: sessionCookie,
        origin: 'https://localhost:7257'
      },
      redirect: 'manual'
    })
    const clearedCookie = cookieNamed(logout, '__Host-marion_session')

    expect(logout.status).toBe(204)
    expect(state.sessions.size).toBe(0)
    expect(clearedCookie).toContain('Max-Age=0')
    expect(clearedCookie).toContain('HttpOnly')
    expect(clearedCookie).toContain('Secure')
    expect(clearedCookie).not.toContain('generated-')
  })
})

describe('anonymous UI route assumptions', () => {
  it('keeps login and signup public while using top-level browser navigation for Google', async () => {
    const [login, signup, config] = await Promise.all([
      readFile(new URL('../../../app/pages/login.vue', import.meta.url), 'utf8'),
      readFile(new URL('../../../app/pages/signup.vue', import.meta.url), 'utf8'),
      readFile(new URL('../../../nuxt.config.ts', import.meta.url), 'utf8')
    ])

    expect(login).toContain('window.location.assign(\'/auth/google\')')
    expect(signup).toContain('window.location.assign(\'/auth/google\')')
    expect(login).not.toContain('middleware:')
    expect(signup).not.toContain('middleware:')
    expect(config).not.toContain('\'/\': { redirect: \'/login\' }')
    expect(config).not.toContain('global auth middleware')
  })
})
