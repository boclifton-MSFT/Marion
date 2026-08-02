import { describe, expect, it, vi } from 'vitest'
import type { RandomSource } from './security'
import {
  createSqlAuthRepositories,
  type AuthSqlConnection
} from './sql'

interface RecordedCommand {
  statement: string
  parameters: Record<string, string | number | Date>
}

class RecordingSqlConnection implements AuthSqlConnection {
  readonly commands: RecordedCommand[] = []
  transactions = 0

  async query<T>(
    statement: string,
    parameters: Record<string, string | number | Date> = {}
  ): Promise<{ recordset: T[], rowsAffected: number[] }> {
    this.commands.push({ statement, parameters })
    if (statement.includes('OUTPUT DELETED.TransactionId')) {
      return { recordset: [], rowsAffected: [1] }
    }
    if (statement.includes('SELECT UserId')) {
      return {
        recordset: [{ UserId: 'resolved-user' }] as T[],
        rowsAffected: [1]
      }
    }
    return { recordset: [], rowsAffected: [1] }
  }

  async transaction<T>(operation: (connection: AuthSqlConnection) => Promise<T>): Promise<T> {
    this.transactions++
    return operation(this)
  }
}

const random: RandomSource = {
  uuid: () => 'generated-user',
  state: () => 'state',
  nonce: () => 'nonce',
  pkceVerifier: () => 'verifier'
}

function repositories(connection: RecordingSqlConnection) {
  const connect = vi.fn(async () => connection)
  return {
    repositories: createSqlAuthRepositories({
      connectionString: 'Server=auth-store.invalid;Database=marion',
      provisionSchema: false
    }, random, connect),
    connect
  }
}

describe('shared SQL authentication repositories', () => {
  it('consumes an authorization transaction with one conditional delete', async () => {
    const connection = new RecordingSqlConnection()
    const { repositories: auth, connect } = repositories(connection)

    await expect(auth.transactions.consume('transaction-one', 1_750_000_000_000)).resolves.toBe(true)

    expect(connect).toHaveBeenCalledWith('Server=auth-store.invalid;Database=marion')
    expect(connection.commands).toHaveLength(1)
    expect(connection.commands[0]?.statement).toContain('DELETE FROM dbo.MarionAuthTransactions')
    expect(connection.commands[0]?.statement).toContain('ExpiresAt >= @now')
    expect(connection.commands[0]?.parameters.transactionId).toBe('transaction-one')
  })

  it('rotates sessions in one database transaction and cannot recreate a revoked session by touch', async () => {
    const connection = new RecordingSqlConnection()
    const { repositories: auth } = repositories(connection)
    const session = {
      sessionId: 'marion-session-two',
      userId: 'marion-user',
      issuedAt: 1_750_000_010_000,
      lastActiveAt: 1_750_000_010_000
    }

    await auth.sessions.rotate('marion-session-one', session)
    await auth.sessions.touch(session, 1_750_000_020_000)

    expect(connection.transactions).toBe(1)
    expect(connection.commands[0]?.statement).toContain('DELETE FROM dbo.MarionAuthSessions')
    expect(connection.commands[1]?.statement).toContain('INSERT INTO dbo.MarionAuthSessions')
    expect(connection.commands[2]?.statement).toContain('LastActiveAt = @now')
    expect(connection.commands[2]?.statement).toContain('LastActiveAt = @lastActiveAt')
  })

  it('resolves an external identity under a serializable lock keyed only by issuer and subject', async () => {
    const connection = new RecordingSqlConnection()
    const { repositories: auth } = repositories(connection)

    await expect(auth.identities.resolve({
      issuer: 'https://accounts.google.com',
      subject: 'provider-subject'
    }, 1_750_000_000_000)).resolves.toBe('resolved-user')

    expect(connection.transactions).toBe(1)
    expect(connection.commands[0]?.statement).toContain('WITH (UPDLOCK, HOLDLOCK)')
    expect(connection.commands[0]?.statement).toContain('Issuer = @issuer AND Subject = @subject')
    expect(connection.commands[0]?.parameters).not.toHaveProperty('email')
  })
})
