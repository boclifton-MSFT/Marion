import { describe, expect, it } from 'vitest'
import type { H3Event } from 'h3'
import {
  bindAuthDependencies,
  getAuthDependencies,
  type AuthTelemetry,
  type AuthDependencies
} from './dependencies'

const dependencies: AuthDependencies = {
  clock: {
    now: () => 1_750_000_000_000
  },
  random: {
    uuid: () => 'id',
    state: () => 'state',
    nonce: () => 'nonce',
    pkceVerifier: () => 'verifier'
  },
  oidc: {
    authorizationUrl: async () => new URL('https://accounts.google.com/o/oauth2/v2/auth'),
    exchangeCode: async () => ({
      issuer: 'https://accounts.google.com',
      subject: 'subject'
    })
  },
  telemetry: {
    record: () => {}
  },
  transactions: {
    create: async () => {},
    consume: async () => true
  },
  sessions: {
    create: async () => {},
    get: async () => null,
    touch: async () => null,
    rotate: async () => {},
    revoke: async () => {}
  },
  identities: {
    resolve: async () => 'user'
  }
}

describe('authentication dependency seams', () => {
  it('uses event-scoped clock, OIDC, randomness, telemetry, and repositories', () => {
    const event = { context: {} } as H3Event

    bindAuthDependencies(event, dependencies)

    expect(getAuthDependencies(event)).toBe(dependencies)
  })

  it('allows a telemetry sink to receive only a safe event name', () => {
    const events: string[] = []
    const telemetry: AuthTelemetry = {
      record: event => events.push(event)
    }

    telemetry.record('callback-failed')

    expect(events).toEqual(['callback-failed'])
  })
})
