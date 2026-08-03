# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture & technical direction | Stewie | System boundaries, ADRs, cross-service contracts, technical trade-offs |
| Backend & workflows | Brian | Minimal APIs, service communication, Azure Functions triggers, orchestration |
| Frontend & product experience | Lois | Nuxt pages, Nuxt UI components, composables, product interaction flows |
| Quality, security & compliance | Joe | Test strategy, security reviews, compliance controls, release gates |
| Cloud platform & DevOps | Cleveland | Aspire hosting, Azure deployment, CI/CD, infrastructure, observability |
| Data & AI | Mort | Data models, retrieval, AI workflows, evaluation, grounding |
| Mortgage domain & product analysis | Tom | Loan workflows, mortgage terminology, requirements, acceptance criteria |
| Scope & priorities | Tom + Stewie | Product sequencing, trade-offs, decisions |
| Code review | Joe | Review PRs, check quality, security, and compliance risks |
| Testing | Joe | Write tests, find edge cases, verify fixes |
| Work monitoring | Ralph | Queue health, issue flow, keep-alive |
| Session logging | Scribe | Automatic — never needs routing |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review |
| Claim verification & pre-mortems | Fact Checker | Verify APIs, packages, requirements, and architecture assumptions |
| Bug fixes (isolated, test-covered) | @copilot 🤖 | Single-file fixes, test additions |
| Documentation updates | @copilot 🤖 | README, API docs, inline comments |
| Test coverage gaps | @copilot 🤖 | Adding missing test cases |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Stewie |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, **Stewie** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Stewie's review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. The Lead handles all `squad` (base label) triage.
8. **Check for simplicity before coding.** Especially for multi-file changes, confirm the approach is the simplest way to meet the goal; consult relevant documentation when helpful; prefer clear, human-readable code over unnecessary robustness that adds complexity.
9. **Direct push exception** — Bo's explicit authorization to push a named change directly to `main` applies only to that change; otherwise, follow the dev/PR workflow.
