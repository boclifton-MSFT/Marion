# Squad Decisions

## Active Decisions

### 2026-07-25T11:40:40.578-05:00: Initial team composition

**By:** Bo Clifton (via Squad)

**What:** Marion will use a domain team composed of Stewie (architecture), Brian (backend and workflows), Lois (frontend and product), Joe (quality, security, and compliance), Cleveland (cloud platform and DevOps), Mort (data and AI), and Tom (mortgage domain and product), supported by Scribe, Ralph, Rai, and Fact Checker.

**Why:** The project combines .NET 10/Aspire services, Nuxt product work, Azure Functions, Entra identity, AI, mortgage-domain requirements, and future observability and compliance needs.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction

### 2026-07-25T11:46:43.697-05:00: GPT 5.6 team model mapping

**By:** Bo Clifton (via Squad)

**What:** Opus resolves to `gpt-5.6-sol` with `xhigh` reasoning, Sonnet resolves to `gpt-5.6-terra` with `medium` reasoning, and Haiku resolves to `gpt-5.6-luna` with `low` reasoning. Task-aware tier selection remains enabled, with Scribe pinned to Luna/low.

**Why:** Standardize the team's GPT 5.6 model family while preserving role-appropriate and task-aware reasoning defaults.

### 2026-07-25T11:54:11.488-05:00: Keep the API as a single-project vertical-slice modular monolith

**By:** Stewie

**What:** Keep `Marion.ApiService` as the only backend application project. Move the starter-owned `/` and `/weatherforecast` endpoints, their static data, and the `WeatherForecast` response record into `Features/System/SystemEndpoints.cs`, exposed through one internal `MapSystemEndpoints(IEndpointRouteBuilder)` extension. Keep `Program.cs` as the composition root for Aspire ServiceDefaults, Problem Details, OpenAPI, middleware, feature mapping, and default health endpoints. Treat `Infrastructure/Configuration`, `Infrastructure/Identity`, `Infrastructure/Persistence`, `Infrastructure/Storage`, `Infrastructure/Messaging`, `Infrastructure/Health`, and `Common` as reserved locations that are created only when concrete implementations need them; do not add empty folders, interfaces, registration methods, or projects.

**Why:** This establishes a clear vertical-feature boundary and a concise composition root while preserving the current API, Aspire observability, health checks, nullable settings, and build graph. It avoids a ceremonial Clean Architecture split and leaves future infrastructure concerns discoverable without introducing abstractions before requirements exist.
