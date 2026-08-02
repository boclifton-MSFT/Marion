import { defineEventHandler, getQuery, sendRedirect } from 'h3'
import { getAuthDependencies } from '../../utils/auth/dependencies'
import {
  authRuntimeConfig,
  getAuthStoreSettings,
  getOidcSettings,
  getSessionPassword
} from '../../utils/auth/runtime'
import { createOAuthTransaction, safeReturnTo } from '../../utils/auth/security'
import { saveTransactionInCookie } from '../../utils/auth/session'

const SIGN_IN_UNAVAILABLE = '/login?error=sign-in-unavailable'

export default defineEventHandler(async (event) => {
  const dependencies = getAuthDependencies(event)
  try {
    const runtimeConfig = authRuntimeConfig(event)
    const settings = getOidcSettings(runtimeConfig)
    if (!settings || !getSessionPassword(runtimeConfig) || !getAuthStoreSettings(runtimeConfig)) {
      return sendRedirect(event, SIGN_IN_UNAVAILABLE)
    }

    const returnTo = safeReturnTo(getQuery(event).returnTo)
    const transaction = createOAuthTransaction({
      state: dependencies.random.state(),
      nonce: dependencies.random.nonce(),
      codeVerifier: dependencies.random.pkceVerifier()
    }, dependencies.random.uuid(), returnTo, dependencies.clock.now())

    if (!await saveTransactionInCookie(event, transaction)) {
      return sendRedirect(event, SIGN_IN_UNAVAILABLE)
    }

    await dependencies.transactions.create(
      transaction.transactionId,
      transaction.issuedAt + 5 * 60 * 1000
    )

    return sendRedirect(event, (await dependencies.oidc.authorizationUrl(settings, transaction)).href)
  } catch {
    dependencies.telemetry.record('authorization-start-failed')
    return sendRedirect(event, SIGN_IN_UNAVAILABLE)
  }
})
