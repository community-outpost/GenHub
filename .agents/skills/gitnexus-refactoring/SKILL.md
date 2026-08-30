---
name: gitnexus-refactoring
description: "Use when renaming, extracting, splitting, moving, or refactoring code in GenHub safely. Examples: \"Rename ICasStorage method\", \"Extract manifest parser from ContentResolver\", \"Refactor ContentReconciliationService\", \"Split GameLauncher hooks\""
---

# Refactoring with GitNexus

## When to Use

- "Rename a method on `ICasService` or `IContentReconciliationService` safely"
- "Extract a CAS pool verification service from `CasService`"
- "Split platform-specific process launch logic from `GameLauncher`"
- "Move reconciliation audit helpers to a dedicated service"
- Any task involving renaming, extracting, splitting, or restructuring code

## Workflow

```
1. gitnexus_impact({target: "X", direction: "upstream"})  → Map all dependents
2. gitnexus_query({query: "X"})                            → Find execution flows involving X
3. gitnexus_context({name: "X"})                           → See all incoming/outgoing refs
4. Plan update order: interfaces → implementations → callers → tests
```

> If "Index is stale" → run `pnpm exec gitnexus analyze` in terminal.

## Checklists

### Rename Symbol

```
- [ ] gitnexus_rename({symbol_name: "oldName", new_name: "newName", dry_run: true}) — preview all edits
- [ ] Review graph edits (high confidence) and ast_search edits (review carefully)
- [ ] If satisfied: gitnexus_rename({..., dry_run: false}) — apply edits
- [ ] gitnexus_detect_changes() — verify only expected files changed
- [ ] Run tests for affected processes
```

### Extract Module / Service

```
- [ ] gitnexus_context({name: target}) — see all incoming/outgoing refs
- [ ] gitnexus_impact({target, direction: "upstream"}) — find all external callers
- [ ] Define new module interface in GenHub.Core
- [ ] Extract code, register in DependencyInjection module
- [ ] gitnexus_detect_changes() — verify affected scope
- [ ] Run tests for affected processes
```

### Split Function/Service

```
- [ ] gitnexus_context({name: target}) — understand all callees
- [ ] Group callees by responsibility
- [ ] gitnexus_impact({target, direction: "upstream"}) — map callers to update
- [ ] Create new functions/services
- [ ] Update callers
- [ ] gitnexus_detect_changes() — verify affected scope
- [ ] Run tests for affected processes
```

## Tools

**gitnexus_rename** — automated multi-file rename:

```
gitnexus_rename({symbol_name: "MaterializeFileAsync", new_name: "DeployArtifactAsync", dry_run: true})
→ 8 edits across 5 files
→ 6 graph edits (high confidence), 2 ast_search edits (review)
→ Changes: [{file_path, edits: [{line, old_text, new_text, confidence}]}]
```

**gitnexus_impact** — map all dependents first:

```
gitnexus_impact({target: "ContentReconciliationService", direction: "upstream"})
→ d=1: GameLauncher, ProfileLauncherFacade, ReconciliationAuditLog
→ Affected Processes: ProfileLaunchFlow, ProfileWorkspaceReconciliation
```

**gitnexus_detect_changes** — verify your changes after refactoring:

```
gitnexus_detect_changes({scope: "staged"})
→ Changed: 5 files, 8 symbols
→ Affected processes: ProfileLaunchFlow, WorkspaceReconciliation
→ Risk: MEDIUM
```

**gitnexus_cypher** — custom reference queries:

```cypher
MATCH (caller)-[:CodeRelation {type: 'CALLS'}]->(m:Method {name: "ReconcileAsync"})
RETURN caller.name, caller.filePath ORDER BY caller.filePath
```

## Risk Rules

| Risk Factor         | Mitigation                                |
| ------------------- | ----------------------------------------- |
| Many callers (>5)   | Use gitnexus_rename for automated updates |
| Cross-area refs     | Use detect_changes after to verify scope  |
| Platform hosts      | Verify composition in Windows, Linux, macOS |
| External/public API | Check Result pattern contract and error codes |

## Example: Rename `MaterializeFileAsync` to `DeployArtifactAsync`

```
1. gitnexus_rename({symbol_name: "MaterializeFileAsync", new_name: "DeployArtifactAsync", dry_run: true})
   → Preview edits across ICasService.cs, CasService.cs, ContentReconciliationService.cs, and tests

2. Review changes to ensure all cross-platform composition roots and test mocks match

3. gitnexus_rename({symbol_name: "MaterializeFileAsync", new_name: "DeployArtifactAsync", dry_run: false})
   → Applied edits across core interfaces, implementation, and test suites

4. gitnexus_detect_changes({scope: "staged"})
   → Affected: ProfileLaunchFlow, WorkspaceReconciliation
   → Risk: MEDIUM — run targeted tests (dotnet test GenHub/GenHub.Tests/GenHub.Tests.Core/...)
```
