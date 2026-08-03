import { afterEach, describe, expect, it, vi } from 'vitest'
import { createHttpAuthRepositories } from './http'
import type { MarionSession } from './security'

const settings = {
  apiBase: 'https://api.invalid',
  bffKey: 'bff-key-sentinel'
}

const session: MarionSession = {
  sessionId: 'session-1',
  userId: 'user-1',
  issuedAt: 1_700_000_000_000,
  lastActiveAt: 1_700_000_060_000
}

function stubFetch(...responses: Response[]) {
  const fetchMock = vi.fn()
  for (const response of responses) {
    fetchMock.mockResolvedValueOnce(response)
  }

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' }
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('HTTP auth repositories', () => {
  it('authenticates to the internal auth surface and never leaks the key to the browser', async () => {
    const fetchMock = stubFetch(new Response(null, { status: 204 }))

    await createHttpAuthRepositories(settings).transactions.create(
      'tx-1',
      1_700_000_300_000
    )

    const [url, init] = fetchMock.mock.calls[0]!
    expect(url.toString()).toBe(
      'https://api.invalid/internal/auth/transactions'
    )
    expect(init.method).toBe('POST')
    expect(init.headers['X-Marion-Bff-Key']).toBe('bff-key-sentinel')
    expect(JSON.parse(init.body)).toEqual({
      transactionId: 'tx-1',
      expiresAt: 1_700_000_300_000
    })
  })

  it('reports a replayed transaction as unconsumed', async () => {
    stubFetch(json({ consumed: false }))

    const consumed = await createHttpAuthRepositories(
      settings
    ).transactions.consume('tx-1', 1_700_000_000_000)

    expect(consumed).toBe(false)
  })

  it('treats a missing session as no session', async () => {
    stubFetch(new Response(null, { status: 404 }))

    await expect(
      createHttpAuthRepositories(settings).sessions.get('session-1')
    ).resolves.toBeNull()
  })

  it('treats a lost touch race as no session', async () => {
    stubFetch(new Response(null, { status: 409 }))

    await expect(
      createHttpAuthRepositories(settings).sessions.touch(
        session,
        1_700_000_120_000
      )
    ).resolves.toBeNull()
  })

  it('returns the refreshed session after a successful touch', async () => {
    stubFetch(json({ ...session, lastActiveAt: 1_700_000_120_000 }))

    await expect(
      createHttpAuthRepositories(settings).sessions.touch(
        session,
        1_700_000_120_000
      )
    ).resolves.toEqual({ ...session, lastActiveAt: 1_700_000_120_000 })
  })

  it('fails loudly when the store errors so an outage cannot look like a signed-out user', async () => {
    stubFetch(new Response(null, { status: 500 }))

    await expect(
      createHttpAuthRepositories(settings).sessions.get('session-1')
    ).rejects.toThrow(/500/)
  })

  it('fails loudly when a revocation cannot be delivered', async () => {
    const fetchMock = vi.fn().mockRejectedValue(new Error('connection reset'))
    vi.stubGlobal('fetch', fetchMock)

    await expect(
      createHttpAuthRepositories(settings).sessions.revoke('session-1')
    ).rejects.toThrow(/unreachable/)
  })

  it('rejects a malformed identity response', async () => {
    stubFetch(json({ userId: 42 }))

    await expect(
      createHttpAuthRepositories(settings).identities.resolve(
        { issuer: 'https://accounts.google.com', subject: 'subject-1' },
        1_700_000_000_000
      )
    ).rejects.toThrow(/invalid user identifier/)
  })
})
