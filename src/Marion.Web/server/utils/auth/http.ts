import type { ExternalIdentity, MarionSession } from './security'
import type {
  AtomicTransactionStore,
  AuthRepositories,
  IdentityRepository,
  SessionRevocationStore
} from './storage'

export interface AuthApiSettings {
  apiBase: string
  bffKey: string
}

interface SessionPayload {
  sessionId?: unknown
  userId?: unknown
  issuedAt?: unknown
  lastActiveAt?: unknown
}

const BFF_KEY_HEADER = 'X-Marion-Bff-Key'

function asTimestamp(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
    ? value
    : undefined
}

function sessionFromPayload(
  payload: SessionPayload | undefined
): MarionSession | null {
  const issuedAt = asTimestamp(payload?.issuedAt)
  const lastActiveAt = asTimestamp(payload?.lastActiveAt)
  if (
    typeof payload?.sessionId !== 'string'
    || typeof payload.userId !== 'string'
    || !payload.sessionId
    || !payload.userId
    || issuedAt === undefined
    || lastActiveAt === undefined
  ) {
    return null
  }

  return {
    sessionId: payload.sessionId,
    userId: payload.userId,
    issuedAt,
    lastActiveAt
  }
}

function sessionToPayload(session: MarionSession) {
  return {
    sessionId: session.sessionId,
    userId: session.userId,
    issuedAt: session.issuedAt,
    lastActiveAt: session.lastActiveAt
  }
}

class AuthApiClient {
  private readonly origin: string
  private readonly bffKey: string

  constructor(settings: AuthApiSettings) {
    this.origin = `${settings.apiBase.replace(/\/+$/, '')}/`
    this.bffKey = settings.bffKey
  }

  /**
   * Only `expectedStatuses` are returned to the caller; everything else throws so a transport
   * fault can never be mistaken for "no session" and silently sign a user out.
   */
  async send(
    method: string,
    path: string,
    body?: unknown,
    expectedStatuses: readonly number[] = []
  ): Promise<Response> {
    const url = new URL(`internal/auth/${path}`, this.origin)
    let response: Response
    try {
      response = await fetch(url, {
        method,
        headers: {
          'content-type': 'application/json',
          [BFF_KEY_HEADER]: this.bffKey
        },
        body: body === undefined ? undefined : JSON.stringify(body)
      })
    } catch (cause) {
      throw new Error(
        `The authentication store is unreachable (${method} ${path}).`,
        { cause }
      )
    }

    if (!response.ok && !expectedStatuses.includes(response.status)) {
      throw new Error(
        `The authentication store rejected ${method} ${path} with ${response.status}.`
      )
    }

    return response
  }
}

class HttpAuthRepositories implements AuthRepositories {
  private readonly client: AuthApiClient

  constructor(settings: AuthApiSettings) {
    this.client = new AuthApiClient(settings)
  }

  readonly transactions: AtomicTransactionStore = {
    create: async (transactionId, expiresAt) => {
      await this.client.send('POST', 'transactions', {
        transactionId,
        expiresAt
      })
    },
    consume: async (transactionId, now) => {
      const response = await this.client.send('POST', 'transactions/consume', {
        transactionId,
        now
      })
      const result = (await response.json()) as { consumed?: unknown }
      return result.consumed === true
    }
  }

  readonly sessions: SessionRevocationStore = {
    create: async (session) => {
      await this.client.send('POST', 'sessions', sessionToPayload(session))
    },
    get: async (sessionId) => {
      const response = await this.client.send(
        'GET',
        `sessions/${encodeURIComponent(sessionId)}`,
        undefined,
        [404]
      )
      if (response.status === 404) {
        return null
      }

      return sessionFromPayload((await response.json()) as SessionPayload)
    },
    touch: async (session, now) => {
      const response = await this.client.send(
        'POST',
        'sessions/touch',
        { session: sessionToPayload(session), now },
        [409]
      )
      if (response.status === 409) {
        return null
      }

      return sessionFromPayload((await response.json()) as SessionPayload)
    },
    rotate: async (previousSessionId, session) => {
      await this.client.send('POST', 'sessions/rotate', {
        previousSessionId,
        session: sessionToPayload(session)
      })
    },
    revoke: async (sessionId) => {
      await this.client.send(
        'DELETE',
        `sessions/${encodeURIComponent(sessionId)}`
      )
    }
  }

  readonly identities: IdentityRepository = {
    resolve: async (identity: ExternalIdentity, now: number) => {
      const response = await this.client.send('POST', 'identities/resolve', {
        issuer: identity.issuer,
        subject: identity.subject,
        now
      })
      const result = (await response.json()) as { userId?: unknown }
      if (typeof result.userId !== 'string' || !result.userId) {
        throw new Error(
          'The authentication store returned an invalid user identifier.'
        )
      }

      return result.userId
    }
  }
}

export function createHttpAuthRepositories(
  settings: AuthApiSettings
): AuthRepositories {
  return new HttpAuthRepositories(settings)
}
