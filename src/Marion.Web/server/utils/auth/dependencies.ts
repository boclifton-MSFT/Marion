import { randomUUID } from 'node:crypto'
import type { H3Event } from 'h3'
import * as oidc from 'openid-client'
import { openIdConnectClient, type OidcClient } from './oidc'
import { authRuntimeConfig, getAuthStoreSettings } from './runtime'
import type { Clock, RandomSource } from './security'
import type {
  AuthRepositories,
  AtomicTransactionStore,
  IdentityRepository,
  SessionRevocationStore
} from './storage'
import { createSqlAuthRepositories } from './sql'

export type AuthTelemetryEvent
  = 'authorization-start-failed'
    | 'callback-failed'
    | 'logout-revocation-failed'

export interface AuthTelemetry {
  record(event: AuthTelemetryEvent): void
}

export interface AuthDependencies {
  clock: Clock
  random: RandomSource
  oidc: OidcClient
  telemetry: AuthTelemetry
  transactions: AtomicTransactionStore
  sessions: SessionRevocationStore
  identities: IdentityRepository
}

interface AuthEventContext {
  marionAuth?: AuthDependencies
}

const systemClock: Clock = {
  now: () => Date.now()
}

const systemRandom: RandomSource = {
  uuid: () => randomUUID(),
  state: () => oidc.randomState(),
  nonce: () => oidc.randomNonce(),
  pkceVerifier: () => oidc.randomPKCECodeVerifier()
}

const noOpTelemetry: AuthTelemetry = {
  record: () => {}
}

function unavailableRepositories(): AuthRepositories {
  const unavailable = async (): Promise<never> => {
    throw new Error('The durable authentication store is unavailable.')
  }

  return {
    transactions: {
      create: unavailable,
      consume: async () => false
    } as AtomicTransactionStore,
    sessions: {
      create: unavailable,
      get: async () => null,
      touch: async () => null,
      rotate: unavailable,
      revoke: unavailable
    } as SessionRevocationStore,
    identities: {
      resolve: unavailable
    } as IdentityRepository
  }
}

function sqlRepositories(event: H3Event): AuthRepositories {
  const settings = getAuthStoreSettings(authRuntimeConfig(event))
  return settings
    ? createSqlAuthRepositories(settings, systemRandom)
    : unavailableRepositories()
}

export function bindAuthDependencies(event: H3Event, dependencies: AuthDependencies): void {
  const context = event.context as AuthEventContext
  context.marionAuth = dependencies
}

export function getAuthDependencies(event: H3Event): AuthDependencies {
  const override = (event.context as AuthEventContext).marionAuth
  if (override) {
    return override
  }

  const repositories = sqlRepositories(event)
  return {
    clock: systemClock,
    random: systemRandom,
    oidc: openIdConnectClient,
    telemetry: noOpTelemetry,
    ...repositories
  }
}
