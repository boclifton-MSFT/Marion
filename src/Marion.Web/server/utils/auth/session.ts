import { clearSession, getSession, setCookie, useSession } from 'h3'
import type { H3Event } from 'h3'
import type { AuthDependencies } from './dependencies'
import { authRuntimeConfig, getSessionPassword } from './runtime'
import {
  type OAuthTransaction,
  type MarionSession,
  createMarionSession,
  isCurrentOAuthTransaction,
  SESSION_COOKIE_NAME,
  sessionCookieConfig,
  sessionIsActive,
  transactionCookieConfig
} from './security'

interface MarionCookieData {
  secure?: {
    marion?: MarionSession
  }
}

function sessionConfig(event: H3Event, clear = false) {
  const password = getSessionPassword(authRuntimeConfig(event))
  return password ? sessionCookieConfig(password, clear) : undefined
}

async function readCookieSession(event: H3Event): Promise<MarionSession | undefined> {
  const config = sessionConfig(event)
  if (!config) {
    return
  }

  const session = await getSession<MarionCookieData>(event, config)
  return session.data.secure?.marion
}

async function clearCookieSession(event: H3Event): Promise<void> {
  const config = sessionConfig(event, true)
  if (!config) {
    setCookie(event, SESSION_COOKIE_NAME, '', {
      secure: true,
      httpOnly: true,
      sameSite: 'lax',
      path: '/',
      maxAge: 0
    })
    return
  }

  await clearSession(event, config)
}

async function writeCookieSession(
  event: H3Event,
  session: MarionSession,
  config: NonNullable<ReturnType<typeof sessionConfig>>
): Promise<void> {
  const cookieSession = await useSession<MarionCookieData>(event, config)
  await cookieSession.update({ secure: { marion: session } })
}

export async function rotateMarionSession(
  event: H3Event,
  userId: string,
  dependencies: AuthDependencies
): Promise<void> {
  const config = sessionConfig(event)
  if (!config) {
    throw new Error('Authentication is unavailable.')
  }

  const previous = await readCookieSession(event)
  const session = createMarionSession(userId, dependencies.random.uuid(), dependencies.clock.now())
  await dependencies.sessions.rotate(previous?.sessionId, session)
  await writeCookieSession(event, session, config)
}

export async function getActiveMarionSession(
  event: H3Event,
  dependencies: AuthDependencies
): Promise<MarionSession | undefined> {
  const now = dependencies.clock.now()
  const config = sessionConfig(event)
  const cookieSession = await readCookieSession(event)
  if (!config || !sessionIsActive(cookieSession, now)) {
    if (cookieSession) {
      await dependencies.sessions.revoke(cookieSession.sessionId)
    }
    await clearCookieSession(event)
    return
  }

  const storedSession = await dependencies.sessions.get(cookieSession.sessionId)
  if (!storedSession
    || storedSession.userId !== cookieSession.userId
    || storedSession.issuedAt !== cookieSession.issuedAt
    || !sessionIsActive(storedSession, now)) {
    await dependencies.sessions.revoke(cookieSession.sessionId)
    await clearCookieSession(event)
    return
  }

  const activeSession = await dependencies.sessions.touch(storedSession, now)
  if (!activeSession) {
    await clearCookieSession(event)
    return
  }

  await writeCookieSession(event, activeSession, config)
  return activeSession
}

export async function revokeCurrentMarionSession(
  event: H3Event,
  dependencies: AuthDependencies
): Promise<void> {
  const cookieSession = await readCookieSession(event)
  try {
    if (cookieSession) {
      await dependencies.sessions.revoke(cookieSession.sessionId)
    }
  } finally {
    await clearCookieSession(event)
  }
}

export async function saveTransactionInCookie(
  event: H3Event,
  transaction: OAuthTransaction
): Promise<boolean> {
  const password = getSessionPassword(authRuntimeConfig(event))
  if (!password) {
    return false
  }

  const session = await useSession<{ transaction: OAuthTransaction }>(
    event,
    transactionCookieConfig(password)
  )
  await session.update({ transaction })
  return true
}

export async function consumeTransactionFromCookie(
  event: H3Event,
  now: number
): Promise<OAuthTransaction | undefined> {
  const password = getSessionPassword(authRuntimeConfig(event))
  if (!password) {
    return
  }

  const config = transactionCookieConfig(password)
  const session = await getSession<{ transaction?: OAuthTransaction }>(event, config)
  await clearSession(event, transactionCookieConfig(password, true))

  const transaction = session.data.transaction
  return isCurrentOAuthTransaction(transaction, now) ? transaction : undefined
}
