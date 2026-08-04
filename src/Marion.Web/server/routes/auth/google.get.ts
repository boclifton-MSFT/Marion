import { defineEventHandler, getQuery, sendRedirect } from 'h3'
import { getAuthDependencies } from '../../utils/auth/dependencies'
import {
  authConfigurationIssues,
  authRuntimeConfig,
  getOidcSettings
} from '../../utils/auth/runtime'
import {
  createOAuthTransaction,
  OAUTH_TRANSACTION_MAX_AGE_SECONDS,
  safeProtectedReturnTo
} from '../../utils/auth/security'
import { saveTransactionInCookie } from '../../utils/auth/session'

const SIGN_IN_UNAVAILABLE = '/login?error=sign-in-unavailable'

export default defineEventHandler(async (event) => {
  const dependencies = getAuthDependencies(event)
  try {
    const runtimeConfig = authRuntimeConfig(event)
    const settings = getOidcSettings(runtimeConfig)
    if (!settings || authConfigurationIssues(runtimeConfig).length > 0) {
      return sendRedirect(event, SIGN_IN_UNAVAILABLE)
    }

    const returnTo = safeProtectedReturnTo(getQuery(event).returnTo)
    const transaction = createOAuthTransaction({
      state: dependencies.random.state(),
      nonce: dependencies.random.nonce(),
      codeVerifier: dependencies.random.pkceVerifier()
    }, dependencies.random.uuid(), settings.redirectUri, returnTo, dependencies.clock.now())

    if (!await saveTransactionInCookie(event, transaction)) {
      return sendRedirect(event, SIGN_IN_UNAVAILABLE)
    }

    await dependencies.transactions.create(
      transaction.transactionId,
      transaction.issuedAt + OAUTH_TRANSACTION_MAX_AGE_SECONDS * 1000
    )

    return sendRedirect(event, (await dependencies.oidc.authorizationUrl(settings, transaction)).href)
  } catch {
    dependencies.telemetry.record('authorization-start-failed')
    return sendRedirect(event, SIGN_IN_UNAVAILABLE)
  }
})
