export const DEFAULT_RETURN_TO = '/'

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0
}

export function safeReturnTo(value: unknown): string {
  if (!isNonEmptyString(value) || !value.startsWith('/') || value.startsWith('//')) {
    return DEFAULT_RETURN_TO
  }

  let decoded: string
  try {
    decoded = decodeURIComponent(value)
  } catch {
    return DEFAULT_RETURN_TO
  }

  if (value.includes('\\') || decoded.includes('\\') || decoded.startsWith('//')) {
    return DEFAULT_RETURN_TO
  }

  try {
    const target = new URL(value, 'https://marion.invalid')
    return target.origin === 'https://marion.invalid'
      ? `${target.pathname}${target.search}${target.hash}`
      : DEFAULT_RETURN_TO
  } catch {
    return DEFAULT_RETURN_TO
  }
}

export function isProtectedPath(pathname: string): boolean {
  return pathname === '/app' || pathname.startsWith('/app/')
}

export function safeProtectedReturnTo(value: unknown): string {
  const returnTo = safeReturnTo(value)
  const pathname = new URL(returnTo, 'https://marion.invalid').pathname
  return isProtectedPath(pathname) ? returnTo : DEFAULT_RETURN_TO
}

export function googleSignInPath(returnTo: unknown): string {
  const target = safeProtectedReturnTo(returnTo)
  return target === DEFAULT_RETURN_TO
    ? '/auth/google'
    : `/auth/google?returnTo=${encodeURIComponent(target)}`
}
