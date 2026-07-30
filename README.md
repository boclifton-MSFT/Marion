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
- A supported container runtime for the local SQL Server resource
- [Node.js](https://nodejs.org/) 22.19 or newer
- [pnpm](https://pnpm.io/) 11

### Install and run

```powershell
Set-Location src\Marion.Web
corepack pnpm install
Set-Location ..\..
aspire start
```

The Aspire CLI prints the authenticated dashboard URL and starts SQL Server, the API, and the web application. Local SQL data is stored in an Aspire-managed volume and the SQL container uses a persistent lifetime so normal AppHost restarts preserve development data.

### Persistence and health

The AppHost models the SQL Server resource as `sql` and its application database as `mariondb`. Aspire supplies the `mariondb` connection string to the API, where `MarionDbContext` is registered through the Aspire Entity Framework Core SQL Server integration.

The API does not run migrations during startup and currently defines no mortgage-domain schema. SQL connectivity is included in `/health` readiness checks; `/alive` remains a process-only liveness check. On success, both health endpoints return HTTP 200 with the stable JSON response `{ "status": "Healthy" }`; consumers must not rely on a raw-text response. The safe `/api/system/dependencies` response exposes only logical health states and never connection details.

Tests generate unique database configuration and disable the external SQL connectivity check under the `Testing` environment, keeping test state isolated from persistent development data.

### Future Azure SQL configuration

Keep the `mariondb` logical connection name when replacing the local SQL resource with Azure SQL. Provision Microsoft Entra administration and least-privilege database access outside the application, then supply token-authenticated Azure SQL configuration through Aspire and managed identity rather than source-controlled credentials. The API's `MarionDbContext` registration remains the persistence seam; environment-specific hosting and identity configuration belong in the AppHost and deployment layer.

### Common commands

```powershell
# Build the .NET solution
dotnet build src\Marion.slnx

# Run .NET tests
dotnet test src\Marion.slnx

# Check the frontend
Set-Location src\Marion.Web
corepack pnpm lint
corepack pnpm typecheck
corepack pnpm build
```
