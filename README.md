# Marion

Marion is an AI-powered loan officer assistant built to automate the loan origination workflow end to end. It gives loan officers a clearer view of every file, understands the documents and decisions that move a loan forward, and applies AI where it can remove busywork, surface risk, and accelerate the path to closing.

The goal is practical: help loan teams spend less time chasing information and more time serving borrowers and closing loans.

## Core capabilities

- End-to-end workflow automation across the loan origination lifecycle
- Intelligent document and application comprehension
- Proactive identification of missing information, blockers, and next actions
- AI-assisted borrower communication and loan officer decision support
- A unified workspace for moving loans from intake through closing

## Technology

- .NET 10 and ASP.NET Core
- Aspire 13.4 for local orchestration, service discovery, health checks, and telemetry
- SQL Server and Entity Framework Core for relational persistence
- Azure Blob Storage with Azurite for private document storage
- Nuxt 4, Vue 3, Nuxt UI, and TypeScript
- OpenTelemetry for distributed traces, metrics, and logs

## Repository structure

```text
.
|-- aspire.config.json
|-- src
|   |-- Marion.ApiService
|   |-- Marion.AppHost
|   |-- Marion.ServiceDefaults
|   |-- Marion.Web
|   `-- Marion.slnx
`-- README.md
```

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Aspire CLI](https://aspire.dev/get-started/install-cli/)
- A supported container runtime for the local SQL Server and Azurite resources
- [Node.js](https://nodejs.org/) 22.19 or newer
- [pnpm](https://pnpm.io/) 11

### Install and run

```powershell
Set-Location src\Marion.Web
corepack pnpm install
Set-Location ..\..
aspire start
```

The Aspire CLI prints the authenticated dashboard URL and starts SQL Server, Azurite, the API, and the web application. Local SQL and Blob data use Aspire-managed volumes with persistent container lifetimes so normal development AppHost restarts preserve state.

### Persistence and health

The AppHost models the SQL Server resource as `sql` and its application database as `mariondb`. Aspire supplies the `mariondb` connection string to the API, where `MarionDbContext` is registered through the Aspire Entity Framework Core SQL Server integration.

The API does not run migrations during startup and currently defines no mortgage-domain schema. SQL connectivity is included in `/health` readiness checks; `/alive` remains a process-only liveness check. On success, both health endpoints return HTTP 200 with the stable JSON response `{ "status": "Healthy" }`; consumers must not rely on a raw-text response. The safe `/api/system/dependencies` response exposes only logical health states and never connection details.

Tests generate unique database configuration and disable the external SQL connectivity check under the `Testing` environment, keeping test state isolated from persistent development data.

### Document storage

The AppHost models the Azurite-backed storage account as `storage` and the private Blob container resource as `documents`, whose physical container name is `test-files`. Aspire provisions the container and supplies its connection information only to the API; the Nuxt frontend has no storage reference or storage configuration.

Normal Development runs use an Aspire-managed Azurite data volume and persistent container lifetime. `IntegrationTesting` runs use session lifetime, dynamic ports, no data volume, and no frontend so each test graph is isolated and disposable. Fast API tests disable the external Blob readiness registration and use inert client settings, so they do not require Azurite.

Blob container connectivity is included in `/health` under the logical dependency name `documents` and does not participate in `/alive`. When Blob Storage is unavailable, `/health` returns HTTP 503 with `{ "status": "Unhealthy" }`, `/alive` remains HTTP 200 with `{ "status": "Healthy" }`, and `/api/system/dependencies` remains HTTP 200 with only the safe `Unavailable` state. No URI, key, connection data, or exception details are returned.

Development and IntegrationTesting expose `POST /api/system/storage/verify` for a bounded synthetic upload/read/verify/delete check. It uses unique non-sensitive content, always attempts cleanup, and returns only the outcome and duration. The route is not mapped in Production and must not be used for document-domain data.

For future Azure hosting, keep the `documents` logical connection name and provide a Blob service URI plus the physical container name through Aspire. The Aspire client integration uses the environment's default Azure credential for service-URI connections, preserving a managed-identity path without adding account keys to source. Explicit Azure RBAC and production provisioning belong to the deployment layer.

### Future Azure SQL configuration

Keep the `mariondb` logical connection name when replacing the local SQL resource with Azure SQL. Provision Microsoft Entra administration and least-privilege database access outside the application, then supply token-authenticated Azure SQL configuration through Aspire and managed identity rather than source-controlled credentials. The API's `MarionDbContext` registration remains the persistence seam; environment-specific hosting and identity configuration belong in the AppHost and deployment layer.

### Authentication persistence

Google sign-in state lives in the shared `mariondb` database, but only the API talks to it. The Nuxt server runs the OAuth flow and owns the `__Host-` cookies, then calls the API's internal `/internal/auth/*` surface for authorization transactions, sessions, and `(issuer, sub)` identity mappings. Those routes sit outside `/api`, so the frontend's API proxy cannot expose them to a browser, and they require the shared `Auth__BffKey` secret that Aspire injects into both processes. The browser receives neither a database connection nor any provider credential, and the Nuxt process holds no SQL credentials at all.

The API owns the schema through EF Core migrations. Local and integration runs apply them at startup before the health endpoint reports ready; set `Database__ApplyMigrations` to `false` on runtime replicas that must not run DDL, and apply migrations as a controlled deployment step instead. Production operators supply `NUXT_AUTH_BFF_KEY` to the frontend and a matching `Auth__BffKey` to the API through their deployment secret mechanism, backed by a shared transactional SQL Server/Azure SQL database with a least-privilege principal. Process-local Nitro storage, local files, and per-replica SQLite are not supported because they cannot guarantee replay prevention, session revocation, or identity uniqueness across restarts and replicas.

### Common commands

```powershell
# Build the .NET solution
dotnet build src\Marion.slnx

# Run .NET tests (fast; excludes container-backed integration tests)
dotnet test src\Marion.slnx

# Run the integration tests (boots real containers; takes several minutes)
dotnet test src\Marion.slnx --settings src\Marion.ApiService.Tests\integration.runsettings

# Check the frontend
Set-Location src\Marion.Web
corepack pnpm lint
corepack pnpm typecheck
corepack pnpm build
```
