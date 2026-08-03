import type { ExternalIdentity, MarionSession } from './security'

export interface AtomicTransactionStore {
  create(transactionId: string, expiresAt: number): Promise<void>
  consume(transactionId: string, now: number): Promise<boolean>
}

export interface SessionRevocationStore {
  create(session: MarionSession): Promise<void>
  get(sessionId: string): Promise<MarionSession | null>
  touch(session: MarionSession, now: number): Promise<MarionSession | null>
  rotate(previousSessionId: string | undefined, session: MarionSession): Promise<void>
  revoke(sessionId: string): Promise<void>
}

export interface IdentityRepository {
  resolve(identity: ExternalIdentity, now: number): Promise<string>
}

export interface AuthRepositories {
  transactions: AtomicTransactionStore
  sessions: SessionRevocationStore
  identities: IdentityRepository
}
