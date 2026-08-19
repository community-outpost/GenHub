---
name: gitnexus-debugging
description: "Use when debugging a bug, tracing an error, or investigating unexpected behavior in GenHub (e.g. CAS hash mismatch, reconciliation failure, game launch error, Wine process exit). Examples: \"Why is CasService failing to materialize files?\", \"Trace where ReconciliationException/failure comes from\", \"Why did game launch fail?\""
---

# Debugging with GitNexus

## When to Use

- "Why is `CasService.MaterializeFileAsync` failing?"
- "Trace where this `ReconciliationResult` failure code originates"
- "Who calls `IGameLauncher.LaunchAsync` and how are errors handled?"
- "Wine process exits immediately with code 1 during launch"
- Investigating profile reconciliation, CAS indexing, or platform runner failures

## Workflow

```
1. gitnexus_query({query: "<error or symptom>"})            → Find related execution flows
2. gitnexus_context({name: "<suspect>"})                    → See callers/callees/processes
3. READ gitnexus://repo/{name}/process/{name}                → Trace execution flow
4. gitnexus_cypher({query: "MATCH path..."})                 → Custom traces if needed
```

> If "Index is stale" → run `pnpm exec gitnexus analyze` in terminal.

## Checklist

```
- [ ] Understand the symptom (error message, unexpected behavior, Result failure code)
- [ ] gitnexus_query for error text, domain constants, or related code
- [ ] Identify the suspect function or service from returned processes
- [ ] gitnexus_context to see callers and callees
- [ ] Trace execution flow via process resource if applicable
- [ ] gitnexus_cypher for custom call chain traces if needed
- [ ] Read source files to confirm root cause
```

## Debugging Patterns

| Symptom              | GitNexus Approach                                          |
| -------------------- | ---------------------------------------------------------- |
| Error message / Result code | `gitnexus_query` for error text / constant → `context` on failure sites |
| Wrong return value   | `context` on the method → trace callees for data flow    |
| Intermittent failure | `context` → look for external I/O, file locks, async dependencies |
| Performance issue    | `context` → find symbols with many callers (hot paths like hashing) |
| Recent regression    | `detect_changes` to see what your changes affect           |

## Tools

**gitnexus_query** — find code and execution flows related to an error or symptom:

```
gitnexus_query({query: "CAS hash mismatch materialization"})
→ Processes: WorkspaceReconciliationFlow, CasPoolIngestion
→ Symbols: CasService, ContentReconciliationService, CasHashMismatch
```

**gitnexus_context** — full context for a suspect symbol:

```
gitnexus_context({name: "ReconcileAsync"})
→ Incoming calls: GameLauncher.LaunchAsync, ProfileEditorFacade.ApplyProfile
→ Outgoing calls: CasService.MaterializeFileAsync, ManifestVerificationService.Verify
→ Processes: ProfileLaunchFlow (step 2/5)
```

**gitnexus_cypher** — custom call chain traces:

```cypher
MATCH path = (a)-[:CodeRelation {type: 'CALLS'}*1..2]->(b:Method {name: "MaterializeFileAsync"})
RETURN [n IN nodes(path) | n.name] AS chain
```

## Example: "Game launch fails during profile workspace reconciliation"

```
1. gitnexus_query({query: "workspace reconciliation launch failure"})
   → Processes: GameLaunchFlow, ProfileReconciliation
   → Symbols: GameLauncher, ContentReconciliationService, CasService

2. gitnexus_context({name: "GameLauncher.LaunchAsync"})
   → Outgoing calls: ContentReconciliationService.ReconcileAsync, IGameProcessManager.StartAsync

3. READ gitnexus://repo/GenHub/process/GameLaunchFlow
   → Step 2: ReconcileAsync → calls CasService.MaterializeFileAsync

4. Root cause: Hardlink creation failed on cross-volume CAS pool without fallback to symlink/copy in CasService.
```
