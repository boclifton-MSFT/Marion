import type * as mssql from 'mssql'
import type { ExternalIdentity, MarionSession, RandomSource } from './security'
import type {
  AtomicTransactionStore,
  AuthRepositories,
  IdentityRepository,
  SessionRevocationStore
} from './storage'

export interface AuthStoreSettings {
  connectionString: string
  provisionSchema: boolean
}

type SqlValue = string | number | Date

interface SqlResult<T> {
  recordset: T[]
  rowsAffected: number[]
}

interface SqlRequest {
  input(name: string, value: SqlValue): SqlRequest
  query<T>(statement: string): Promise<SqlResult<T>>
}

interface SqlConnection {
  request(): SqlRequest
}

interface SqlTransaction extends SqlConnection {
  begin(isolationLevel: number): Promise<void>
  commit(): Promise<void>
  rollback(): Promise<void>
}

export interface AuthSqlConnection {
  query<T>(statement: string, parameters?: Record<string, SqlValue>): Promise<SqlResult<T>>
  transaction<T>(operation: (connection: AuthSqlConnection) => Promise<T>): Promise<T>
}

interface SessionRow {
  SessionId: string
  UserId: string
  IssuedAt: Date
  LastActiveAt: Date
}

interface IdentityRow {
  UserId: string
}

const schemaStatement = `
DECLARE @lockResult int;
EXEC @lockResult = sp_getapplock
    @Resource = N'marion-auth-schema',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 10000;
IF @lockResult < 0
    THROW 51000, 'Unable to acquire the Marion auth schema lock.', 1;

IF OBJECT_ID(N'dbo.MarionAuthTransactions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MarionAuthTransactions (
        TransactionId nvarchar(128) NOT NULL PRIMARY KEY,
        ExpiresAt datetime2(3) NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.MarionAuthSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MarionAuthSessions (
        SessionId nvarchar(128) NOT NULL PRIMARY KEY,
        UserId nvarchar(128) NOT NULL,
        IssuedAt datetime2(3) NOT NULL,
        LastActiveAt datetime2(3) NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.MarionExternalIdentities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MarionExternalIdentities (
        Issuer nvarchar(2048) NOT NULL,
        Subject nvarchar(255) NOT NULL,
        UserId nvarchar(128) NOT NULL,
        CreatedAt datetime2(3) NOT NULL,
        CONSTRAINT PK_MarionExternalIdentities PRIMARY KEY (Issuer, Subject),
        CONSTRAINT UQ_MarionExternalIdentities_UserId UNIQUE (UserId)
    );
END;`

class MssqlAuthConnection implements AuthSqlConnection {
  constructor(
    private readonly connection: SqlConnection,
    private readonly sql: typeof mssql
  ) {}

  async query<T>(
    statement: string,
    parameters: Record<string, SqlValue> = {}
  ): Promise<SqlResult<T>> {
    const request = this.connection.request()
    for (const [name, value] of Object.entries(parameters)) {
      request.input(name, value)
    }

    return request.query<T>(statement)
  }

  async transaction<T>(operation: (connection: AuthSqlConnection) => Promise<T>): Promise<T> {
    const transaction = new this.sql.Transaction(this.connection as mssql.ConnectionPool)
    await transaction.begin(this.sql.ISOLATION_LEVEL.SERIALIZABLE)
    const transactionalConnection = new MssqlAuthConnection(
      transaction as unknown as SqlTransaction,
      this.sql
    )

    try {
      const result = await operation(transactionalConnection)
      await transaction.commit()
      return result
    } catch (error) {
      await transaction.rollback()
      throw error
    }
  }
}

let pooledConnectionString: string | undefined
let pooledConnection: Promise<AuthSqlConnection> | undefined

async function sqlConnection(connectionString: string): Promise<AuthSqlConnection> {
  if (!pooledConnection || pooledConnectionString !== connectionString) {
    pooledConnectionString = connectionString
    pooledConnection = (async () => {
      const sql = await import('mssql')
      const pool = await new sql.ConnectionPool(connectionString).connect()
      return new MssqlAuthConnection(pool as unknown as SqlConnection, sql)
    })().catch((error: unknown) => {
      pooledConnection = undefined
      throw error
    })
  }

  return pooledConnection
}

function asDate(value: number): Date {
  return new Date(value)
}

function asEpoch(value: unknown): number | undefined {
  const time = value instanceof Date
    ? value.getTime()
    : typeof value === 'string' ? Date.parse(value) : Number.NaN
  return Number.isSafeInteger(time) && time >= 0 ? time : undefined
}

function sessionFromRow(row: SessionRow | undefined): MarionSession | null {
  const issuedAt = row && asEpoch(row.IssuedAt)
  const lastActiveAt = row && asEpoch(row.LastActiveAt)
  if (!row?.SessionId || !row.UserId || issuedAt === undefined || lastActiveAt === undefined) {
    return null
  }

  return {
    sessionId: row.SessionId,
    userId: row.UserId,
    issuedAt,
    lastActiveAt
  }
}

class SqlAuthRepositories implements AuthRepositories {
  readonly transactions: AtomicTransactionStore = {
    create: async (transactionId, expiresAt) => {
      const connection = await this.connection()
      const now = new Date()
      await connection.query(`
DELETE FROM dbo.MarionAuthTransactions WHERE ExpiresAt < @now;
INSERT INTO dbo.MarionAuthTransactions (TransactionId, ExpiresAt)
VALUES (@transactionId, @expiresAt);`, {
        now,
        transactionId,
        expiresAt: asDate(expiresAt)
      })
    },
    consume: async (transactionId, now) => {
      const connection = await this.connection()
      const result = await connection.query(`
DELETE FROM dbo.MarionAuthTransactions
OUTPUT DELETED.TransactionId
WHERE TransactionId = @transactionId AND ExpiresAt >= @now;`, {
        transactionId,
        now: asDate(now)
      })
      return result.rowsAffected.some(count => count === 1)
    }
  }

  readonly sessions: SessionRevocationStore = {
    create: async (session) => {
      const connection = await this.connection()
      await connection.query(`
INSERT INTO dbo.MarionAuthSessions (SessionId, UserId, IssuedAt, LastActiveAt)
VALUES (@sessionId, @userId, @issuedAt, @lastActiveAt);`, sessionParameters(session))
    },
    get: async (sessionId) => {
      const connection = await this.connection()
      const result = await connection.query<SessionRow>(`
SELECT SessionId, UserId, IssuedAt, LastActiveAt
FROM dbo.MarionAuthSessions
WHERE SessionId = @sessionId;`, { sessionId })
      return sessionFromRow(result.recordset[0])
    },
    touch: async (session, now) => {
      const connection = await this.connection()
      const result = await connection.query<SessionRow>(`
UPDATE dbo.MarionAuthSessions
SET LastActiveAt = @now
OUTPUT INSERTED.SessionId, INSERTED.UserId, INSERTED.IssuedAt, INSERTED.LastActiveAt
WHERE SessionId = @sessionId
  AND UserId = @userId
  AND IssuedAt = @issuedAt
  AND LastActiveAt = @lastActiveAt;`, {
        ...sessionParameters(session),
        now: asDate(now)
      })
      return sessionFromRow(result.recordset[0])
    },
    rotate: async (previousSessionId, session) => {
      const connection = await this.connection()
      await connection.transaction(async (transaction) => {
        if (previousSessionId) {
          await transaction.query(`
DELETE FROM dbo.MarionAuthSessions WHERE SessionId = @previousSessionId;`, {
            previousSessionId
          })
        }
        await transaction.query(`
INSERT INTO dbo.MarionAuthSessions (SessionId, UserId, IssuedAt, LastActiveAt)
VALUES (@sessionId, @userId, @issuedAt, @lastActiveAt);`, sessionParameters(session))
      })
    },
    revoke: async (sessionId) => {
      const connection = await this.connection()
      await connection.query(`
DELETE FROM dbo.MarionAuthSessions WHERE SessionId = @sessionId;`, { sessionId })
    }
  }

  readonly identities: IdentityRepository = {
    resolve: async (identity, now) => this.resolveIdentity(identity, now)
  }

  private schemaReady: Promise<void> | undefined

  constructor(
    private readonly settings: AuthStoreSettings,
    private readonly random: RandomSource,
    private readonly connect: (connectionString: string) => Promise<AuthSqlConnection> = sqlConnection
  ) {}

  private async connection(): Promise<AuthSqlConnection> {
    const connection = await this.connect(this.settings.connectionString)
    if (this.settings.provisionSchema) {
      this.schemaReady ??= connection.transaction(async (transaction) => {
        await transaction.query(schemaStatement)
      }).catch((error: unknown) => {
        this.schemaReady = undefined
        throw error
      })
      await this.schemaReady
    }
    return connection
  }

  private async resolveIdentity(identity: ExternalIdentity, now: number): Promise<string> {
    const connection = await this.connection()
    return connection.transaction(async (transaction) => {
      const existing = await transaction.query<IdentityRow>(`
SELECT UserId
FROM dbo.MarionExternalIdentities WITH (UPDLOCK, HOLDLOCK)
WHERE Issuer = @issuer AND Subject = @subject;`, {
        issuer: identity.issuer,
        subject: identity.subject
      })
      if (existing.recordset[0]?.UserId) {
        return existing.recordset[0].UserId
      }

      const userId = this.random.uuid()
      await transaction.query(`
INSERT INTO dbo.MarionExternalIdentities (Issuer, Subject, UserId, CreatedAt)
VALUES (@issuer, @subject, @userId, @createdAt);`, {
        issuer: identity.issuer,
        subject: identity.subject,
        userId,
        createdAt: asDate(now)
      })
      return userId
    })
  }
}

function sessionParameters(session: MarionSession): Record<string, SqlValue> {
  return {
    sessionId: session.sessionId,
    userId: session.userId,
    issuedAt: asDate(session.issuedAt),
    lastActiveAt: asDate(session.lastActiveAt)
  }
}

export function createSqlAuthRepositories(
  settings: AuthStoreSettings,
  random: RandomSource,
  connect?: (connectionString: string) => Promise<AuthSqlConnection>
): AuthRepositories {
  return new SqlAuthRepositories(settings, random, connect)
}
