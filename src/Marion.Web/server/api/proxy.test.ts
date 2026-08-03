import { createServer } from 'node:http'
import { createApp, createRouter, defineEventHandler, toNodeListener } from 'h3'
import { afterEach, describe, expect, it, vi } from 'vitest'
import proxyRoute from './[...path]'

// Captured before any stubbing so the test client is not caught by the upstream mock.
const clientFetch = globalThis.fetch.bind(globalThis)

const runtimeConfig = {
  apiBase: 'https://api.invalid',
  session: { password: 's'.repeat(32) },
  authStore: { bffKey: '' }
}

Object.assign(globalThis, {
  useRuntimeConfig: () => runtimeConfig
})

async function startServer(injectedPath?: string) {
  const app = createApp()
  const router = createRouter()
  router.use('/api/**:path', proxyRoute)

  // Stands in for a router that decodes an encoded traversal into the catch-all parameter,
  // which HTTP-level normalisation would otherwise hide from this test.
  router.use('/probe', defineEventHandler((event) => {
    event.context.params = { path: injectedPath ?? '' }
    return proxyRoute(event)
  }))
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

function stubUpstream() {
  const upstream = vi.fn().mockResolvedValue(
    new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } })
  )
  vi.stubGlobal('fetch', upstream)
  return upstream
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('API proxy boundary', () => {
  it('forwards an ordinary API path to the upstream service', async () => {
    const upstream = stubUpstream()
    const server = await startServer()
    try {
      await clientFetch(`${server.baseUrl}/api/system/info`)
    } finally {
      await server.close()
    }

    expect(upstream).toHaveBeenCalledTimes(1)
    expect(upstream.mock.calls[0]![0].toString())
      .toBe('https://api.invalid/api/system/info')
  })

  it.each([
    '../internal/auth/sessions/session-1',
    '../../internal/auth/identities/resolve',
    'system/../../internal/auth/sessions/touch',
    '%2e%2e/internal/auth/sessions/session-1'
  ])('refuses to let %s escape the public API prefix', async (injectedPath) => {
    const upstream = stubUpstream()
    const server = await startServer(injectedPath)
    const response = await clientFetch(`${server.baseUrl}/probe`)
      .finally(() => server.close())

    expect(upstream).not.toHaveBeenCalled()
    expect(response.status).toBe(404)
  })
})
