---
name: gitnexus-exploring
description: "Use when exploring GenHub architecture, tracing execution flows, or understanding subsystems (e.g. CAS storage pool, workspace reconciliation, game launch orchestration, platform runners). Examples: \"How does CAS materialization work?\", \"Show me the game launch flow\", \"How does GenHub detect game installations?\""
---

# Exploring Codebases with GitNexus

## When to Use

- "How does Content-Addressable Storage (CAS) deduplicate game assets?"
- "What is the workspace reconciliation lifecycle?"
- "Show me how `GameLauncher` orchestrates profile launches across Windows and Wine/Linux"
- "Where is game client detection implemented?"
- Understanding subsystems you haven't worked with before

## Workflow

```
1. READ gitnexus://repos                          → Discover indexed repos
2. READ gitnexus://repo/{name}/context             → Codebase overview, check staleness
3. gitnexus_query({query: "<what you want to understand>"})  → Find related execution flows
4. gitnexus_context({name: "<symbol>"})            → Deep dive on specific symbol
5. READ gitnexus://repo/{name}/process/{name}      → Trace full execution flow
```

> If step 2 says "Index is stale" → run `pnpm exec gitnexus analyze` in terminal.

## Checklist

```
- [ ] READ gitnexus://repo/{name}/context
- [ ] gitnexus_query for the concept you want to understand
- [ ] Review returned processes (execution flows)
- [ ] gitnexus_context on key symbols for callers/callees
- [ ] READ process resource for full execution traces
- [ ] Read source files for implementation details
```

## Resources

| Resource                                | What you get                                            |
| --------------------------------------- | ------------------------------------------------------- |
| `gitnexus://repo/{name}/context`        | Stats, staleness warning (~150 tokens)                  |
| `gitnexus://repo/{name}/clusters`       | All functional areas with cohesion scores (~300 tokens) |
| `gitnexus://repo/{name}/cluster/{name}` | Area members with file paths (~500 tokens)              |
| `gitnexus://repo/{name}/process/{name}` | Step-by-step trace                        |

## Tools

**gitnexus_query** — find execution flows related to a concept:

```
gitnexus_query({query: "profile workspace reconciliation"})
→ Processes: ProfileLaunchFlow, ContentReconciliation, CasPoolIngestion
→ Symbols grouped by flow (ContentReconciliationService, CasService, ManifestResolver)
```

**gitnexus_context** — 360-degree view of a symbol:

```
gitnexus_context({name: "CasService"})
→ Incoming calls: ContentReconciliationService, InstallationCasPoolService
→ Outgoing calls: FileHashProvider, StorageLocationService
→ Processes: ProfileLaunchFlow (step 2/5), ModInstallationFlow (step 3/4)
```

## Example: "How does profile launch and workspace reconciliation work?"

```
1. READ gitnexus://repo/GenHub/context       → C# .NET 8 desktop engine, CAS storage, multi-platform runners
2. gitnexus_query({query: "profile launch reconciliation"})
   → ProfileLaunchFlow: ProfileLauncherFacade.LaunchProfileAsync → ContentReconciliationService.ReconcileAsync → WineGameProcessManager.StartAsync
3. gitnexus_context({name: "ContentReconciliationService"})
   → Incoming: GameLauncher, ProfileLauncherFacade
   → Outgoing: CasService.MaterializeFileAsync, ManifestVerificationService.Verify
4. Read GenHub/GenHub.Core/Features/Content/ContentReconciliationService.cs for implementation details
```
