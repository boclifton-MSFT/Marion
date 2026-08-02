import { createError, defineEventHandler, getRequestHeader, sendNoContent } from 'h3'
import { getAuthDependencies } from '../../utils/auth/dependencies'
import { authRuntimeConfig, getOidcSettings } from '../../utils/auth/runtime'
import { isTrustedRequestOrigin } from '../../utils/auth/security'
import { revokeCurrentMarionSession } from '../../utils/auth/session'

export default defineEventHandler(async (event) => {
  const dependencies = getAuthDependencies(event)
  const settings = getOidcSettings(authRuntimeConfig(event))
  if (!settings || !isTrustedRequestOrigin(getRequestHeader(event, 'origin'), settings.redirectUri)) {
    throw createError({
      statusCode: 403,
      statusMessage: 'Forbidden'
    })
  }

  try {
    await revokeCurrentMarionSession(event, dependencies)
  } catch {
    dependencies.telemetry.record('logout-revocation-failed')
    throw createError({
      statusCode: 503,
      statusMessage: 'Sign out is temporarily unavailable.'
    })
  }

  sendNoContent(event, 204)
})
