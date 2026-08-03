import {
  createError,
  defineEventHandler,
  getMethod,
  getRequestHeaders,
  getRequestURL,
  getRouterParam,
  readRawBody,
  setResponseHeader,
  setResponseStatus
} from 'h3'
import { getAuthDependencies } from '../utils/auth/dependencies'
import { authRuntimeConfig, getAuthStoreSettings } from '../utils/auth/runtime'
import { getActiveMarionSession } from '../utils/auth/session'

const BFF_KEY_HEADER = 'x-marion-bff-key'
const USER_ID_HEADER = 'x-marion-user-id'

const forwardedMethods = new Set(['GET', 'HEAD', 'POST', 'PUT', 'PATCH', 'DELETE', 'OPTIONS'])
// Deliberately excludes the identity headers so a client can never assert its own user.
const forwardedRequestHeaders = [
  'accept',
  'authorization',
  'baggage',
  'content-type',
  'traceparent',
  'tracestate',
  'x-correlation-id',
  'x-ms-client-request-id',
  'x-request-id'
]
const forwardedResponseHeaders = [
  'cache-control',
  'content-type',
  'etag',
  'last-modified',
  'location',
  'retry-after',
  'x-correlation-id',
  'x-request-id'
]

export default defineEventHandler(async (event) => {
  const method = getMethod(event)
  if (!forwardedMethods.has(method)) {
    throw createError({
      statusCode: 405,
      statusMessage: 'Method not allowed'
    })
  }

  const path = getRouterParam(event, 'path')
  if (!path) {
    throw createError({
      statusCode: 404,
      statusMessage: 'API route not found'
    })
  }

  const config = useRuntimeConfig(event)
  const apiBase = String(config.apiBase).replace(/\/+$/, '')
  const upstreamUrl = new URL(`/api/${path.replace(/^\/+/, '')}`, `${apiBase}/`)
  upstreamUrl.search = getRequestURL(event).search

  const requestHeaders = getRequestHeaders(event)
  const headers = new Headers()
  for (const headerName of forwardedRequestHeaders) {
    const value = requestHeaders[headerName]
    if (value) {
      headers.set(headerName, value)
    }
  }

  const authStore = getAuthStoreSettings(authRuntimeConfig(event))
  if (authStore) {
    try {
      const session = await getActiveMarionSession(event, getAuthDependencies(event))
      if (session) {
        headers.set(BFF_KEY_HEADER, authStore.bffKey)
        headers.set(USER_ID_HEADER, session.userId)
      }
    } catch {
      // An unverifiable session is forwarded as anonymous rather than assumed valid.
    }
  }

  const body = method === 'GET' || method === 'HEAD'
    ? undefined
    : await readRawBody(event)
  let upstreamResponse: Response
  try {
    upstreamResponse = await fetch(upstreamUrl, {
      method,
      headers,
      body
    })
  } catch {
    throw createError({
      statusCode: 502,
      statusMessage: 'Upstream API unavailable'
    })
  }

  for (const headerName of forwardedResponseHeaders) {
    const value = upstreamResponse.headers.get(headerName)
    if (value) {
      setResponseHeader(event, headerName, value)
    }
  }

  setResponseStatus(event, upstreamResponse.status)

  if (method === 'HEAD' || upstreamResponse.status === 204) {
    return
  }

  if (upstreamResponse.status >= 400) {
    setResponseHeader(event, 'content-type', 'application/json')
    return {
      error: 'Upstream API request failed'
    }
  }

  return new Uint8Array(await upstreamResponse.arrayBuffer())
})
