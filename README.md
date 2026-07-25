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
- [Node.js](https://nodejs.org/) 22.19 or newer
- [pnpm](https://pnpm.io/) 11

### Install and run

```powershell
Set-Location src\Marion.Web
corepack pnpm install
Set-Location ..\..
aspire start
```

The Aspire CLI prints the authenticated dashboard URL and starts both the API and web application.

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
