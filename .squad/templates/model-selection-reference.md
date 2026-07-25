# Model Selection Reference

## Marion GPT 5.6 Tier Policy

Preserve task-aware tier selection, then resolve the selected tier to this model and reasoning-effort pair:

| Existing Tier | Model | Reasoning Effort |
|---------------|-------|------------------|
| Opus / Premium | `gpt-5.6-sol` | `xhigh` |
| Sonnet / Standard | `gpt-5.6-terra` | `medium` |
| Haiku / Fast | `gpt-5.6-luna` | `low` |

`.squad/config.json` version 1 supports global and per-agent overrides, but not tier mappings. Do not set a global model or reasoning default for this policy because that would disable task-aware tier selection. Fixed-tier agents may use explicit paired overrides; agents without an override use the table above after their task tier is selected. Scribe is explicitly pinned to the Fast pair because its tier is invariant.

### Per-Agent Model Selection

Before spawning an agent, determine which model to use. Check these layers in order — first match wins:

**Layer 0 — Persistent Config (`.squad/config.json`):** On session start, read `.squad/config.json`. Resolve model and reasoning effort independently:

- Model: `agentModelOverrides.{agentName}`, then `defaultModel`
- Reasoning effort: `agentReasoningEffortOverrides.{agentName}`, then `defaultReasoningEffort`

Use paired model and effort overrides from the tier table whenever both are under repository control. This layer survives across sessions.

- **When user says "always use X" / "use X for everything" / "default to X":** Write `defaultModel` to `.squad/config.json`. Acknowledge: `✅ Model preference saved: {model} — all future sessions will use this until changed.`
- **When user says "use X for {agent}":** Write to `agentModelOverrides.{agent}` in `.squad/config.json`. Acknowledge: `✅ {Agent} will always use {model} — saved to config.`
- **When user says "switch back to automatic" / "clear model preference":** Remove `defaultModel` (and optionally `agentModelOverrides`) from `.squad/config.json`. Acknowledge: `✅ Model preference cleared — returning to automatic selection.`

**Layer 1 — Session Directive:** Did the user specify a model for this session? ("use opus for this session", "save costs"). If yes, use that model. Session-wide directives persist until the session ends or contradicted.

**Layer 2 — Charter Preference:** Does the agent's charter have a `## Model` section with `Preferred` set to a specific model (not `auto`)? If yes, use that model.

**Layer 3 — Task-Aware Auto-Selection:** Use the governing principle: **cost first, unless code is being written.** Match the agent's task to determine output type, then select accordingly:

| Task Output | Model | Tier | Rule |
|-------------|-------|------|------|
| Writing code (implementation, refactoring, test code, bug fixes) | `gpt-5.6-terra` · `medium` | Standard / Sonnet | Quality and accuracy matter for code. Use standard tier. |
| Writing prompts or agent designs (structured text that functions like code) | `gpt-5.6-terra` · `medium` | Standard / Sonnet | Prompts are executable — treat like code. |
| NOT writing code (docs, planning, triage, logs, changelogs, mechanical ops) | `gpt-5.6-luna` · `low` | Fast / Haiku | Cost first. Use the fast tier for non-code tasks. |
| Visual/design work requiring image analysis | `gpt-5.6-sol` · `xhigh` | Premium / Opus | Vision and deep analysis require the premium tier. |

**Role-to-model mapping** (applying cost-first principle):

| Role | Default Model and Effort | Why | Override When |
|------|--------------------------|-----|---------------|
| Core Dev / Backend / Frontend | `gpt-5.6-terra` · `medium` | Writes code — quality first | Non-code work → Fast; architecture/reviewer gate → Premium |
| Tester / QA | `gpt-5.6-terra` · `medium` | Writes test code — quality first | Simple scaffolding → Fast; security/release gate → Premium |
| Lead / Architect | auto (per-task) | Mixed: implementation needs Standard, architecture needs Premium, planning needs Fast | Select by task output |
| Prompt Engineer | auto (per-task) | Mixed: prompt design is like code, research is not | Prompt architecture → Standard; research/analysis → Fast |
| Copilot SDK Expert | `gpt-5.6-terra` · `medium` | Technical analysis that often touches code | Pure research → Fast |
| Designer / Visual | `gpt-5.6-sol` · `xhigh` | Vision-capable premium tier required | — (never downgrade when vision is required) |
| DevRel / Writer | `gpt-5.6-luna` · `low` | Docs and writing — not code | — |
| Scribe / Logger | `gpt-5.6-luna` · `low` | Mechanical file ops — cheapest tier | — (never bump Scribe) |
| Git / Release | `gpt-5.6-luna` · `low` | Mechanical ops — changelogs, tags, version bumps | — (never bump mechanical ops) |

**Task complexity adjustments** (apply at most ONE — no cascading):
- **Bump UP to Premium (`gpt-5.6-sol` · `xhigh`):** architecture proposals, reviewer gates, security audits, multi-agent coordination (output feeds 3+ agents)
- **Stay on Standard (`gpt-5.6-terra` · `medium`):** large multi-file refactors, complex implementation from spec, heavy code generation
- **Bump DOWN to Fast (`gpt-5.6-luna` · `low`):** typo fixes, renames, boilerplate, scaffolding, changelogs, version bumps

**Layer 4 — Default:** If nothing else matched, use `gpt-5.6-luna` with `low` reasoning. Cost wins when in doubt, unless code is being produced.

**Fallback chains — when a model is unavailable:**

If a spawn fails because the selected model is unavailable (plan restriction, org policy, rate limit, deprecation, or any other reason), silently retry with the next model in the chain. Keep the selected tier's reasoning effort when the fallback supports it; otherwise omit reasoning effort. Do NOT tell the user about fallback attempts. Maximum 3 retries before jumping to the nuclear fallback.

```
Premium:  gpt-5.6-sol → gpt-5.5 → gpt-5.4 → gpt-5.6-terra → (omit model param)
Standard: gpt-5.6-terra → gpt-5.5 → gpt-5.4 → gpt-5.3-codex → (omit model param)
Fast:     gpt-5.6-luna → gpt-5.4-mini → gpt-5-mini → (omit model param)
```

`(omit model param)` = call the `task` tool WITHOUT the `model` parameter. The platform uses its built-in default. This is the nuclear fallback — it always works.

**Fallback rules:**
- Keep this repository's GPT preference throughout the explicit fallback chains
- Never fall back UP in tier — a fast/cheap task should not land on a premium model
- Log fallbacks to the orchestration log for debugging, but never surface to the user unless asked

**Passing the model and reasoning effort to spawns:**

Pass both resolved values on every primary GPT 5.6 `task` tool call:

```
agent_type: "general-purpose"
model: "{resolved_model}"
reasoning_effort: "{resolved_reasoning_effort}"
mode: "background"
name: "{name}"
description: "{emoji} {Name}: {brief task summary}"
prompt: |
  ...
```

Always set both parameters for the primary tier mapping. If a fallback model does not support the selected reasoning effort, omit only `reasoning_effort`. If the fallback chain reaches nuclear fallback, omit both parameters.

**Spawn output format — show the model choice and effort:**

When spawning, include the model in your acknowledgment:

```
🔧 Brian (gpt-5.6-terra · medium) — refactoring auth module
🎨 Lois (gpt-5.6-sol · xhigh · vision) — reviewing a visual design
📋 Scribe (gpt-5.6-luna · low) — logging session
⚡ Stewie (gpt-5.6-sol · xhigh · bumped for architecture) — reviewing proposal
📝 Tom (gpt-5.6-luna · low) — updating requirements
```

Include a tier annotation only when the tier was bumped. Always show non-default reasoning effort.

**Primary tier models:**

Premium / Opus: `gpt-5.6-sol` with `xhigh`
Standard / Sonnet: `gpt-5.6-terra` with `medium`
Fast / Haiku: `gpt-5.6-luna` with `low`

Fallback-only models used above: `gpt-5.5`, `gpt-5.4`, `gpt-5.3-codex`, `gpt-5.4-mini`, `gpt-5-mini`.
