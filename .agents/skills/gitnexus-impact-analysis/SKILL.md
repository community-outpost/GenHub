---
name: gitnexus-impact-analysis
description: "Use when analyzing blast radius or safety before modifying core GenHub symbols/interfaces (e.g. ICasService, IContentReconciliationService, IGameLauncher). Examples: \"Is it safe to change ICasService?\", \"What depends on ContentReconciliationService?\", \"What will break if I modify GameLauncher?\""
---

# Impact Analysis with GitNexus

## When to Use

- "Is it safe to modify `ICasService` method signatures?"
- "What will break if I change `IContentReconciliationService.ReconcileAsync`?"
- "Show me the blast radius of modifying `IGameProcessManager` across Windows, Linux, and macOS hosts"
- "Who uses this code?"
- Before making non-trivial code changes to core abstractions
- Before committing — to understand what your changes affect

## Workflow

```
1. gitnexus_impact({target: "X", direction: "upstream"})  → What depends on this
2. READ gitnexus://repo/{name}/processes                   → Check affected execution flows
3. gitnexus_detect_changes()                               → Map current git changes to affected flows
4. Assess risk and report to user
```

> If "Index is stale" → run `pnpm exec gitnexus analyze` in terminal.

## Checklist

```
- [ ] gitnexus_impact({target, direction: "upstream"}) to find dependents
- [ ] Review d=1 items first (these WILL BREAK)
- [ ] Check high-confidence (>0.8) dependencies
- [ ] READ processes to check affected execution flows
- [ ] gitnexus_detect_changes() for pre-commit check
- [ ] Assess risk level and report to user
```

## Understanding Output

| Depth | Risk Level       | Meaning                  |
| ----- | ---------------- | ------------------------ |
| d=1   | **WILL BREAK**   | Direct callers/importers |
| d=2   | LIKELY AFFECTED  | Indirect dependencies    |
| d=3   | MAY NEED TESTING | Transitive effects       |

## Risk Assessment

| Affected                       | Risk     |
| ------------------------------ | -------- |
| <5 symbols, few processes      | LOW      |
| 5-15 symbols, 2-5 processes    | MEDIUM   |
| >15 symbols or many processes  | HIGH     |
| Critical path (CAS, launcher, reconciliation, platform runners) | CRITICAL |

## Tools

**gitnexus_impact** — the primary tool for symbol blast radius:

```
gitnexus_impact({
  target: "ICasService",
  direction: "upstream",
  minConfidence: 0.8,
  maxDepth: 3
})

→ d=1 (WILL BREAK):
  - CasService (GenHub/Services/CasService.cs) [IMPLEMENTS, 100%]
  - ContentReconciliationService (GenHub/Features/Content/ContentReconciliationService.cs) [CALLS, 100%]
  - InstallationCasPoolService (GenHub.Core/Features/Storage/InstallationCasPoolService.cs) [CALLS, 100%]

→ d=2 (LIKELY AFFECTED):
  - GameLauncher (GenHub/Features/Launching/GameLauncher.cs) [CALLS, 95%]
  - ProfileEditorFacade (GenHub/Features/GameProfiles/ProfileEditorFacade.cs) [CALLS, 90%]
```

**gitnexus_detect_changes** — git-diff based impact analysis:

```
gitnexus_detect_changes({scope: "staged"})

→ Changed: 3 symbols in CasService.cs, ICasService.cs
→ Affected: ProfileLaunchFlow, ContentReconciliationFlow, CasPoolIngestion
→ Risk: HIGH
```

## Example: "What breaks if I change ICasService?"

```
1. gitnexus_impact({target: "ICasService", direction: "upstream"})
   → d=1: CasService, ContentReconciliationService, InstallationCasPoolService (WILL BREAK)
   → d=2: GameLauncher, ProfileLauncherFacade (LIKELY AFFECTED)

2. READ gitnexus://repo/GenHub/processes
   → ProfileLaunchFlow and ModInstallationFlow depend on ICasService

3. Risk: 3 direct dependents, 2 core execution flows = HIGH (Verify callers across Windows, Linux, macOS hosts)
```
