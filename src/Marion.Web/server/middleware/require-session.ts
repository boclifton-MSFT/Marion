import { defineEventHandler, getRequestURL, sendRedirect } from 'h3'
import { getAuthDependencies } from '../utils/auth/dependencies'
import { isProtectedPath, safeProtectedReturnTo } from '../utils/auth/security'
import { getActiveMarionSession } from '../utils/auth/session'

export default defineEventHandler(async (event) => {
  const requestUrl = getRequestURL(event)
  if (!isProtectedPath(requestUrl.pathname)) {
    return
  }

  const session = await getActiveMarionSession(event, getAuthDependencies(event))
  if (!session) {
    const returnTo = safeProtectedReturnTo(`${requestUrl.pathname}${requestUrl.search}`)
    return sendRedirect(event, `/login?returnTo=${encodeURIComponent(returnTo)}`)
  }
})
