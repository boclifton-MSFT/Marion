# Squad Team

> marion

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Stewie | Principal Architect / Technical Lead | `.squad/agents/stewie/charter.md` | 🏗️ Active |
| Brian | Backend & Workflow Engineer | `.squad/agents/brian/charter.md` | 🔧 Active |
| Lois | Frontend & Product Engineer | `.squad/agents/lois/charter.md` | ⚛️ Active |
| Joe | Quality, Security & Compliance Engineer | `.squad/agents/joe/charter.md` | 🧪 Active |
| Cleveland | Cloud Platform & DevOps Engineer | `.squad/agents/cleveland/charter.md` | ⚙️ Active |
| Mort | Data & AI Engineer | `.squad/agents/mort/charter.md` | 📊 Active |
| Tom | Mortgage Domain & Product Analyst | `.squad/agents/tom/charter.md` | 👤 Active |
| Scribe | Session Logger, Memory Manager & Decision Merger | `.squad/agents/scribe/charter.md` | 📋 Always on |
| Ralph | Work Monitor | `.squad/agents/ralph/charter.md` | 🔄 Always on |
| Rai | RAI Reviewer | `.squad/agents/Rai/charter.md` | 🛡️ Always on |
| Fact Checker | Fact Checker | `.squad/agents/fact-checker/charter.md` | 🔍 Always on |


## Coding Agent

<!-- copilot-auto-assign: false -->

| Name | Role | Charter | Status |
|------|------|---------|--------|
| @copilot | Coding Agent | — | 🤖 Coding Agent |

### Capabilities

**🟢 Good fit — auto-route when enabled:**
- Bug fixes with clear reproduction steps
- Test coverage (adding missing tests, fixing flaky tests)
- Lint/format fixes and code style cleanup
- Dependency updates and version bumps
- Small isolated features with clear specs
- Boilerplate/scaffolding generation
- Documentation fixes and README updates

**🟡 Needs review — route to @copilot but flag for squad member PR review:**
- Medium features with clear specs and acceptance criteria
- Refactoring with existing test coverage
- API endpoint additions following established patterns
- Migration scripts with well-defined schemas

**🔴 Not suitable — route to squad member instead:**
- Architecture decisions and system design
- Multi-system integration requiring coordination
- Ambiguous requirements needing clarification
- Security-critical changes (auth, encryption, access control)
- Performance-critical paths requiring benchmarking
- Changes requiring cross-team discussion

## Project Context

- **Project:** Marion, an AI-powered mortgage loan officer assistant
- **Owner:** Bo Clifton
- **Stack:** .NET 10, ASP.NET Core Minimal APIs, Aspire 13.4, Nuxt 4, Nuxt UI 4, Vue 3, TypeScript, Azure Functions, Azure, Entra ID
- **Architecture:** Microservices-style orchestration with service discovery, event-driven workflows, distributed tracing, full observability, and compliance as a future-state requirement
- **Created:** 2026-07-25
