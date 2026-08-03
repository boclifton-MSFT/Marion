import { defineEventHandler, getQuery, getRequestURL, sendRedirect } from 'h3'
import { getAuthDependencies } from '../../../utils/auth/dependencies'
import {
  authConfigurationIssues,
  authRuntimeConfig,
  getOidcSettings
} from '../../../utils/auth/runtime'
import { constantTimeEquals } from '../../../utils/auth/security'
import { consumeTransactionFromCookie, rotateMarionSession } from '../../../utils/auth/session'

const SIGN_IN_FAILED = '/login?error=sign-in-failed'

function queryValue(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined
}

export default defineEventHandler(async (event) => {
  const dependencies = getAuthDependencies(event)
  try {
    const runtimeConfig = authRuntimeConfig(event)
    const now = dependencies.clock.now()
    const settings = getOidcSettings(runtimeConfig)
    const transaction = await consumeTransactionFromCookie(event, now)
    const state = queryValue(getQuery(event).state)
    if (!settings
      || authConfigurationIssues(runtimeConfig).length > 0
      || !transaction
      || !state
      || !constantTimeEquals(state, transaction.state)) {
      return sendRedirect(event, SIGN_IN_FAILED)
    }

    if (!await dependencies.transactions.consume(transaction.transactionId, now)) {
      return sendRedirect(event, SIGN_IN_FAILED)
    }

    if (queryValue(getQuery(event).error)) {
      return sendRedirect(event, SIGN_IN_FAILED)
    }

    const identity = await dependencies.oidc.exchangeCode(
      settings,
      transaction,
      getRequestURL(event).search,
      now
    )
    if (!identity) {
      return sendRedirect(event, SIGN_IN_FAILED)
    }

    const userId = await dependencies.identities.resolve(identity, now)
    await rotateMarionSession(event, userId, dependencies)
    return sendRedirect(event, transaction.returnTo)
  } catch {
    dependencies.telemetry.record('callback-failed')
    return sendRedirect(event, SIGN_IN_FAILED)
  }
})
