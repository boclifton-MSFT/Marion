import { describe, expect, it } from 'vitest'
import { getAuthStoreSettings, getOidcSettings, getSessionPassword } from './runtime'

const validConfig = {
  apiBase: 'https://api.invalid',
  oauth: {
    oidc: {
      issuer: 'https://accounts.google.com',
      clientId: 'client-id',
      clientSecret: 'client-secret',
      redirectUri: 'https://localhost:7257/auth/google/callback'
    }
  },
  session: {
    password: 'a'.repeat(32)
  },
  authStore: {
    bffKey: 'bff-key'
  }
}

describe('private OIDC runtime configuration', () => {
  it('accepts only complete private configuration', () => {
    expect(getOidcSettings(validConfig)).toEqual({
      issuer: 'https://accounts.google.com',
      clientId: 'client-id',
      clientSecret: 'client-secret',
      redirectUri: 'https://localhost:7257/auth/google/callback'
    })
    expect(getSessionPassword(validConfig)).toBe('a'.repeat(32))
  })

  it.each([
    [{ ...validConfig, oauth: { oidc: { ...validConfig.oauth.oidc, issuer: 'http://accounts.google.com' } } }],
    [{ ...validConfig, oauth: { oidc: { ...validConfig.oauth.oidc, redirectUri: 'https://localhost:7257/not-a-callback' } } }],
    [{ ...validConfig, oauth: { oidc: { ...validConfig.oauth.oidc, clientSecret: '' } } }]
  ])('rejects an incomplete or unsafe OIDC configuration', (config) => {
    expect(getOidcSettings(config)).toBeUndefined()
  })

  it('requires a session password of at least 32 characters', () => {
    expect(getSessionPassword({ session: { password: 'short' } })).toBeUndefined()
  })

  it('requires a private shared key and an API base for the auth store', () => {
    expect(getAuthStoreSettings(validConfig)).toEqual({
      apiBase: 'https://api.invalid',
      bffKey: 'bff-key'
    })
    expect(getAuthStoreSettings({ ...validConfig, authStore: { bffKey: '  ' } })).toBeUndefined()
    expect(getAuthStoreSettings({ ...validConfig, apiBase: '' })).toBeUndefined()
  })
})
