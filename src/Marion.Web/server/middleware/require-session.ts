import { defineEventHandler, getRequestURL, sendRedirect } from 'h3'
import { getAuthDependencies } from '../utils/auth/dependencies'
import { safeReturnTo } from '../utils/auth/security'
import { getActiveMarionSession } from '../utils/auth/session'

const PROTECTED_PREFIX = '/app/'

export default defineEventHandler(async (event) => {
  const requestUrl = getRequestURL(event)
  if (!requestUrl.pathname.startsWith(PROTECTED_PREFIX)) {
    return
  }

  const session = await getActiveMarionSession(event, getAuthDependencies(event))
  if (!session) {
    const returnTo = safeReturnTo(`${requestUrl.pathname}${requestUrl.search}`)
    return sendRedirect(event, `/login?returnTo=${encodeURIComponent(returnTo)}`)
  }
})
