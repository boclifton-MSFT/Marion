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

### 2025-07-25T11:54:11.488-05:00: Keep the API as a single-project vertical-slice modular monolith

**By:** Stewie

**What:** Keep `Marion.ApiService` as the only backend application project. Move the starter-owned `/` and `/weatherforecast` endpoints, their static data, and the `WeatherForecast` response record into `Features/System/SystemEndpoints.cs`, exposed through one internal `MapSystemEndpoints(IEndpointRouteBuilder)` extension. Keep `Program.cs` as the composition root for Aspire ServiceDefaults, Problem Details, OpenAPI, middleware, feature mapping, and default health endpoints. Treat `Infrastructure/Configuration`, `Infrastructure/Identity`, `Infrastructure/Persistence`, `Infrastructure/Storage`, `Infrastructure/Messaging`, `Infrastructure/Health`, and `Common` as reserved locations that are created only when concrete implementations need them; do not add empty folders, interfaces, registration methods, or projects.

**Why:** This establishes a clear vertical-feature boundary and a concise composition root while preserving the current API, Aspire observability, health checks, nullable settings, and build graph. It avoids a ceremonial Clean Architecture split and leaves future infrastructure concerns discoverable without introducing abstractions before requirements exist.

### 2026-07-30T10:41:01.2038306-05:00: PR #23 health failure semantics quality gate (consolidated)

**By:** Joe

**What:** PR #23 initially remained blocked because its automated tests did not protect healthy readiness, unavailable/degraded dependency mapping, `/health` failure status, or `/alive` independence. The independent Brian revision added deterministic Aspire SQL health coverage: it verifies healthy readiness and dependency state, stops the real SQL resource, then verifies `/health` returns 503 while `/alive` remains 200 and the dependency endpoint reports `Unavailable` without diagnostics. It also covers Degraded and Unhealthy mapping with bounded timeouts. Joe approved the revised PR after review.

**Why:** The original testing environment disabled the SQL health check and asserted only registration metadata plus the self health entry, leaving a blocking quality gap. The revised deterministic coverage closes that gap without production-behavior, security, schema-invention, or development-topology regressions.

### 2026-07-25T12:25:30.0966074-05:00: Draft PR first for future issue work

**By:** Bo Clifton (via Squad)

**What:** For future issues, create a draft pull request before beginning implementation; do the work against that draft PR and convert it to non-draft when the work is complete.

**Why:** Keep issue work traceable from the start and make progress visible throughout implementation.

### 2026-07-25T12:25:30.0966074-05:00: Use native Minimal API metadata instead of `WithOpenApi()`

**By:** Brian

**What:** For .NET 10 Minimal APIs using `AddOpenApi()` and `MapOpenApi()`, do not call obsolete `WithOpenApi()`. Express endpoint OpenAPI metadata with supported conventions such as `WithName`, `WithSummary`, `WithDescription`, and `Produces`.

**Why:** `WithOpenApi()` is deprecated in ASP.NET Core and the native endpoint metadata conventions continue to supply the existing OpenAPI behavior.

### 2025-07-25T18-06-01: Use the user token for Copilot assignment workflow chaining

**By:** Cleveland

**What:** Use COPILOT_ASSIGN_TOKEN for triage label/comment API calls and for the dedicated Copilot assignment steps. A GITHUB_TOKEN-authored squad:copilot label does not start another workflow, so triage must use the user token to let squad-issue-assign run. Assignment requests must resolve repoData.default_branch at runtime, target copilot-swe-agent[bot] with agent_assignment, and fail visibly instead of falling back to a generic assignee.

**References:** .github/workflows/squad-triage.yml, .github/workflows/squad-issue-assign.yml, .github/workflows/squad-heartbeat.yml, .squad/templates/workflows/squad-triage.yml, .squad/templates/workflows/squad-issue-assign.yml, .squad/templates/workflows/squad-heartbeat.yml

### 2026-07-30T16:27:40.106-05:00: Select issue #7 as the next ready Layer 2 work item

**By:** Stewie

**What:** Select issue #7, “Layer 2.07 — Add local Azure Service Bus emulator through Aspire,” as the next ready Layer 2 work item and assign Brian. Defer #24 until #7 completes.

**Why:** Layer 2.06 is merged to `main` (PR #25) and closed; #7 depends only on that completed work. #24 and #8 explicitly depend on #7. Brian can extend the established API-side Azure Storage integration pattern with the Service Bus publisher, synthetic versioned contract, readiness check, and AppHost reference while preserving the existing vertical-slice boundary.

### 2026-07-30T16:37:54.573-05:00: Service Bus emulator review and promotion gate (consolidated)

**By:** Cleveland, Joe

**What:** Keep the required `sbemulatorns` emulator-configuration override and add a logical subscription to the `loan-events` topic (or remove the topic until it has a consumer) before waiting on the `messaging` resource. Issue #7/PR #28 was reviewable only with repeatable evidence that Aspire starts the official emulator, the API publishes a synthetic versioned and traceable `PlatformIntegrationRequested` event, readiness reports messaging accurately when available and unavailable, and source-controlled configuration contains no SAS keys, connection strings, or embedded secrets. PR #28 passed the live integration evidence and 16 focused tests, with no credentials present; the client remains compatible with a future fully qualified namespace and default credential. Final independent review found no blocking functional or security defect, but noted that the PR head had no CI check runs, so future merge/release gates should wait for configured or successful CI when available. PR #28 was subsequently merged to `dev` and promoted to `main`; issue #7 closed automatically and its completed worktree and branches were removed.

**Why:** The Service Bus emulator requires at least one subscription per modeled topic and otherwise exits during generated-configuration validation; this is an emulator limitation, not an Aspire health-check or namespace-resolution failure. The review controls cover local infrastructure startup, asynchronous delivery, health semantics, credential boundaries, future identity-based Azure deployment, and explicit CI visibility before release where CI is configured.

### 2026-08-02T13:28:32.111-05:00: Google sign-in uses server-owned OIDC with shared durable authentication state and a fixed normal-mode Aspire callback (consolidated)

**By:** Joe, Lois, Stewie, Brian

**What:** For issue #24 and follow-on issue #30, implement Google sign-in entirely through dedicated Nuxt Nitro server routes: `GET /auth/google` starts authorization and `GET /auth/google/callback` completes it outside the `/api/[...path]` proxy. Use server-side OIDC discovery, authorization-code exchange, S256 PKCE, state, nonce, and full ID-token claim validation; persist and consume a short-lived authorization transaction server-side. Keep Google credentials, callback origin, and session secret/password private to server runtime configuration or process environment. After validation, key identity by `(iss, sub)` and issue a browser-opaque server-owned session in an HttpOnly, Secure, SameSite=Lax `__Host-marion_session` cookie (nuxt-auth-utils may seal the cookie); never expose client secret, codes, tokens, or session keys to browser JavaScript. Authentication transactions, session rotation/revocation, and external-identity mappings must use a shared SQL Server/Azure SQL repository with transactional conditional operations; process-local Nitro storage is not an allowed production implementation. The event-scoped dependency bundle (clock, cryptographic random source, OIDC client, telemetry sink, atomic transaction store, session-revocation store, and external-identity repository) is injectable for deterministic tests, while production defaults must preserve the same boundaries and fixed telemetry event names. Production deployments must configure shared durable storage privately, provision the schema once, and disable replica-time provisioning; development may provision schema only when explicitly enabled. Establish the exact local HTTPS origin `https://localhost:7257` for Google registration, using the normal AppHost HTTPS endpoint and developer certificate. `aspire start --isolated` deliberately assigns random resource ports and is not a callback-origin validation mode; validate this contract with a normal Aspire run. Authenticated application/API authorization and broader persistence remain follow-on scope.

**Why:** This preserves the existing API proxy boundary, prevents Google bearer-token exposure, adds CSRF/replay protections required for OAuth, avoids email-based identity ambiguity, and ensures cross-instance session continuity and revocation. Shared transactional state is required for replay prevention and identity consistency; process-local state would fail across replicas. Stable HTTPS callback registration requires an exact origin, and normal-mode Aspire is the only valid local-origin contract.

### 2026-08-02T11:06:41.115-05:00: PR #29 local Azure Service Bus emulator approval

**By:** Joe

**What:** Joe independently approved GitHub PR #29 (`feat: add local Azure Service Bus emulator`). No high-confidence correctness, security, resilience, secret-handling, or Aspire lifecycle findings were identified. Local `main` was fast-forwarded to `daf00e5`; `git worktree prune` found no additional worktrees to clean.

**Why:** The reviewed change met the independent quality and release-readiness bar without identified blocking defects or lifecycle and secret-handling concerns.
