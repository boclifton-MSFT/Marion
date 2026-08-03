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

📌 Team update (2026-07-30): Issue #6 local Blob Storage implementation completed on exact remote branch `squad/6-layer-2-06-local-blob-storage`. Draft PR #25 adds persistent Development and ephemeral IntegrationTesting Azurite topology, private `test-files` container resource `documents`, focused Blob services and readiness health, bounded synthetic verification, and 23 passing solution tests including outage/recovery — completed by Brian.

📌 Team update (2026-07-30T16:27:40.106-05:00): Completed issue #7 implementation, pushed commit 081e9f8, and opened draft PR #28 targeting dev.
📌 Team update (2026-07-30T16:37:54.573-05:00): Issue #7 Service Bus implementation in PR #28 passed live integration and focused review, merged to dev, and promoted to main — completed by Brian.
📌 Team update (2026-08-01T10:27:14.327-05:00): Secure server-side Google OIDC/session research for issue #24 was merged into the canonical decision — decided by Joe, Lois, Stewie.

📌 Team update (2026-08-02T13:28:32.111-05:00): PR #31 approved after shared durable SQL auth state, injectable security boundaries, and operator schema-provisioning requirements were recorded — decided by Joe, Stewie, Brian, and Lois.
