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
