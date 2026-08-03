📌 Team update (2026-07-25T12:25:30.0966074-05:00): Quality review confirmed removing obsolete `WithOpenApi()` is the correct .NET 10 remediation; optional OpenAPI-document coverage remains non-blocking — decided by Brian and Joe.

# Project Context

- **Project:** Marion, an AI-powered mortgage loan officer assistant
- **Owner:** Bo Clifton
- **Stack:** .NET 10, ASP.NET Core Minimal APIs, Aspire 13.4, Nuxt 4, Nuxt UI 4, Vue 3, TypeScript, Azure Functions, Azure, Entra ID
- **Architecture:** Microservices-style orchestration with event-driven workflows, distributed tracing, full observability, and future-state compliance
- **Initialized:** 2026-07-25

## Role Context

Joe owns quality, security, compliance, testing, and release-readiness review.

📌 Team update (2026-07-30T10:41:01.2038306-05:00): Endpoint consumers, request examples, and SQL-readiness assertions were aligned with the stable health JSON contract. Live Aspire verification requires restarting the pre-change running process — identified by Joe.

📌 Team update (2026-07-30T16:27:40.106-05:00): Defined the Service Bus acceptance/security gate and approved draft PR #28 after live integration and focused-test evidence.
📌 Team update (2026-07-30T16:37:54.573-05:00): Final independent review of PR #28 found no blocking functional or security defect; advisory noted absent CI check runs. PR #28 was merged and promoted to main — decided by Joe.
📌 Team update (2026-08-01T10:27:14.327-05:00): Server-side state, nonce, S256 PKCE, opaque session cookie, and (iss, sub) identity are canonical Google sign-in controls — decided by Joe, Lois, Stewie.

📌 Team update (2026-08-02T11:06:41.115-05:00): Independently approved GitHub PR #29 (`feat: add local Azure Service Bus emulator`) with no high-confidence correctness, security, resilience, secret-handling, or Aspire lifecycle findings. Local `main` fast-forwarded to `daf00e5`; worktree prune found nothing additional — decided by Joe.
