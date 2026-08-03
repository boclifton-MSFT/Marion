import { describe, expect, it } from 'vitest'
import {
  authConfigurationIssues,
  getAuthStoreSettings,
  getOidcSettings,
  getSessionPassword
} from './runtime'

const validConfig = {
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
    connectionString: 'Server=auth-store.invalid;Database=marion',
    provisionSchema: true
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

  it('requires a private shared auth-store connection string', () => {
    expect(getAuthStoreSettings(validConfig)).toEqual(validConfig.authStore)
    expect(getAuthStoreSettings({ authStore: { connectionString: '  ' } })).toBeUndefined()
  })

  it('reports preflight configuration names without leaking configuration values', () => {
    const issues = authConfigurationIssues({
      oauth: { oidc: { clientSecret: 'private-client-secret' } },
      session: { password: 'private-session-password' },
      authStore: { connectionString: 'Server=private-auth-store' }
    })

    expect(issues).toEqual([
      'NUXT_OAUTH_OIDC_ISSUER, NUXT_OAUTH_OIDC_CLIENT_ID, NUXT_OAUTH_OIDC_CLIENT_SECRET, or NUXT_OAUTH_OIDC_REDIRECT_URI',
      'NUXT_SESSION_PASSWORD'
    ])
    expect(issues.join('\n')).not.toContain('private-')
  })

  it('requires a public HTTPS callback origin in production', () => {
    expect(authConfigurationIssues(validConfig, { production: true })).toEqual([
      'NUXT_OAUTH_OIDC_REDIRECT_URI must use a public HTTPS origin in production'
    ])
    expect(authConfigurationIssues({
      ...validConfig,
      oauth: {
        oidc: {
          ...validConfig.oauth.oidc,
          redirectUri: 'https://app.example.test/auth/google/callback'
        }
      }
    }, { production: true })).toEqual([])
  })
})
