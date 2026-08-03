import { createServer } from 'node:http'
import { webcrypto } from 'node:crypto'
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, createRouter, defineEventHandler, toNodeListener } from 'h3'
import requireSession from './require-session'
import { bindAuthDependencies, type AuthDependencies } from '../utils/auth/dependencies'
import { rotateMarionSession } from '../utils/auth/session'
import type { MarionSession } from '../utils/auth/security'

if (!globalThis.crypto) {
  Object.defineProperty(globalThis, 'crypto', { value: webcrypto })
}

Object.assign(globalThis, {
  useRuntimeConfig: () => ({
    session: { password: 's'.repeat(32) }
  })
})

type TouchMode = 'stale' | 'revoked'

function createDependencies(touchMode?: TouchMode) {
  const sessions = new Map<string, MarionSession>()
  let now = 1_750_000_000_000
  let resolveFirstTouchStarted: (() => void) | undefined
  let resolveFirstTouch: (() => void) | undefined
  const firstTouchStarted = new Promise<void>((resolve) => {
    resolveFirstTouchStarted = resolve
  })
  const firstTouch = new Promise<void>((resolve) => {
    resolveFirstTouch = resolve
  })
  let touches = 0
  const dependencies: AuthDependencies = {
    clock: { now: () => touchMode === 'stale' ? now += 1_000 : now },
    random: {
      uuid: () => 'session-id',
      state: () => 'state',
      nonce: () => 'nonce',
      pkceVerifier: () => 'verifier'
    },
    oidc: {
      authorizationUrl: async () => new URL('https://accounts.google.com'),
      exchangeCode: async () => undefined
    },
    telemetry: { record: () => {} },
    transactions: {
      create: async () => {},
      consume: async () => false
    },
    sessions: {
      create: async (session) => { sessions.set(session.sessionId, session) },
      get: async sessionId => sessions.get(sessionId) ?? null,
      touch: async (session, now) => {
        if (touchMode === 'revoked') {
          sessions.delete(session.sessionId)
          return null
        }

        if (touchMode === 'stale' && touches++ === 0) {
          resolveFirstTouchStarted?.()
          await firstTouch
          if (sessions.get(session.sessionId)?.lastActiveAt !== session.lastActiveAt) {
            return null
          }
        }

        const active = { ...session, lastActiveAt: now }
        sessions.set(active.sessionId, active)
        resolveFirstTouch?.()
        return active
      },
      rotate: async (_previous, session) => { sessions.set(session.sessionId, session) },
      revoke: async (sessionId) => { sessions.delete(sessionId) }
    },
    identities: {
      resolve: async () => 'marion-user'
    }
  }
  return { dependencies, firstTouchStarted }
}

async function startServer(touchMode?: TouchMode) {
  const { dependencies, firstTouchStarted } = createDependencies(touchMode)
  const app = createApp()
  const router = createRouter()
  app.use(defineEventHandler((event) => {
    bindAuthDependencies(event, dependencies)
  }))
  app.use(requireSession)
  router.get('/test/seed-session', defineEventHandler(async (event) => {
    await rotateMarionSession(event, 'marion-user', dependencies)
    return { seeded: true }
  }))
  router.get('/app/future-loan', defineEventHandler(() => ({ protected: true })))
  app.use(router)

  const server = createServer(toNodeListener(app))
  await new Promise<void>((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, '127.0.0.1', resolve)
  })
  const address = server.address()
  if (!address || typeof address === 'string') {
    throw new Error('Test server did not expose a TCP address.')
  }

  return {
    baseUrl: `http://127.0.0.1:${address.port}`,
    firstTouchStarted,
    close: async () => new Promise<void>((resolve, reject) =>
      server.close(error => error ? reject(error) : resolve()))
  }
}

function requestCookie(response: Response): string {
  const headers = response.headers as Headers & { getSetCookie?: () => string[] }
  const cookie = (headers.getSetCookie?.() ?? [response.headers.get('set-cookie') ?? ''])
    .find(value => value.startsWith('__Host-marion_session='))
  if (!cookie) {
    throw new Error('Expected a Marion session cookie.')
  }
  return cookie.split(';', 1)[0] ?? ''
}

describe('protected SSR route middleware', () => {
  const servers: Array<{ close: () => Promise<void> }> = []

  afterEach(async () => {
    await Promise.all(servers.splice(0).map(server => server.close()))
  })

  it('redirects an anonymous future protected route and permits a durable session', async () => {
    const server = await startServer()
    servers.push(server)

    const anonymous = await fetch(`${server.baseUrl}/app/future-loan?tab=details`, {
      redirect: 'manual'
    })
    const seed = await fetch(`${server.baseUrl}/test/seed-session`)
    const authenticated = await fetch(`${server.baseUrl}/app/future-loan`, {
      headers: { cookie: requestCookie(seed) }
    })

    expect(anonymous.status).toBe(302)
    expect(anonymous.headers.get('location')).toBe('/login?returnTo=%2Fapp%2Ffuture-loan%3Ftab%3Ddetails')
    expect(authenticated.status).toBe(200)
    await expect(authenticated.json()).resolves.toEqual({ protected: true })
  })

  it('keeps a valid session signed in when a concurrent request has already touched it', async () => {
    const server = await startServer('stale')
    servers.push(server)

    const seed = await fetch(`${server.baseUrl}/test/seed-session`)
    const cookie = requestCookie(seed)
    const first = fetch(`${server.baseUrl}/app/future-loan`, { headers: { cookie } })
    await server.firstTouchStarted
    const second = fetch(`${server.baseUrl}/app/future-loan`, { headers: { cookie } })
    const [firstResponse, secondResponse] = await Promise.all([first, second])

    expect(firstResponse.status).toBe(200)
    expect(secondResponse.status).toBe(200)
    expect(firstResponse.headers.get('set-cookie')).not.toContain('Max-Age=0')
  })

  it('still clears the cookie when the session is revoked while it is being touched', async () => {
    const server = await startServer('revoked')
    servers.push(server)

    const seed = await fetch(`${server.baseUrl}/test/seed-session`)
    const response = await fetch(`${server.baseUrl}/app/future-loan`, {
      headers: { cookie: requestCookie(seed) },
      redirect: 'manual'
    })

    expect(response.status).toBe(302)
    expect(response.headers.get('set-cookie')).toContain('Max-Age=0')
  })
})
