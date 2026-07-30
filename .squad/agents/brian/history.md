📌 Team update (2026-07-25T12:25:30.0966074-05:00): .NET 10 Minimal API endpoint metadata must use native conventions rather than obsolete `WithOpenApi()` — decided by Brian.

# Project Context

- **Project:** Marion, an AI-powered mortgage loan officer assistant
- **Owner:** Bo Clifton
- **Stack:** .NET 10, ASP.NET Core Minimal APIs, Aspire 13.4, Nuxt 4, Nuxt UI 4, Vue 3, TypeScript, Azure Functions, Azure, Entra ID
- **Architecture:** Microservices-style orchestration with event-driven workflows, distributed tracing, full observability, and future-state compliance
- **Initialized:** 2026-07-25

## Role Context

Brian owns backend APIs, Azure Functions workflows, validation, resilience, and service communication.

📌 Team update (2026-07-25T11:57:39.972-05:00): Stewie's Layer 2.02 handoff approved the single-project vertical-slice modular monolith and reassigned issue #2 to Brian — decided by Stewie.

📌 Team update (2026-07-30T10:41:01.2038306-05:00): `/health` and `/alive` now emit the stable JSON contract `{ status: HealthStatus }` while retaining their status-code semantics; seven focused endpoint tests passed — completed by Brian.
