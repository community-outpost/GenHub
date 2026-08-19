# Claude Code Directives for GenHub

Welcome to **GenHub** — a high-performance cross-platform launcher, profile manager, mod organizer, and content distribution platform for Command & Conquer: Generals & Zero Hour.

---

## 1. Primary Workflow & Branching
- **Default Development Target:** All active development and PRs target the `development` branch.
- **Multi-Platform Targets:** Avalonia UI (Desktop), `GenHub.Core` (domain logic), `GenHub.Windows`, `GenHub.Linux`, `GenHub.MacOS`, and `GenHub.Tests`.

---

## 2. Architecture & Code Intelligence (GitNexus)

GenHub utilizes **GitNexus** to index symbols, call hierarchies, inheritance, and execution flows into a structural knowledge graph (`.gitnexus/`).

### Agent Blast Radius Cadence:
1. **Phase 1 — Discovery (Before modifying core classes/interfaces):**
   - Query caller chains and dependents:
     ```json
     gitnexus_impact({ "target": "<SymbolOrClassName>", "direction": "upstream" })
     ```
   - Review $d=1$ (will break) and $d=2$ (likely affected) dependencies before modifying signatures or contracts.
   - Inspect related processes: `gitnexus://repo/{name}/processes` or `gitnexus_query(...)`.

2. **Phase 2 — Change Detection (Pre-Commit / Batch Check):**
   - Run `gitnexus_detect_changes()` on modified/staged files to map diffs against execution flows.
   - Verify cross-platform call chains (Windows/Linux/macOS UI, CAS storage, and process runners).

3. **Phase 3 — Gatekeeping (Pre-PR & CI):**
   - Ensure corresponding unit and integration tests cover modified code paths.
   - CI builds and caches the authoritative GitNexus graph on push to `development` and `main`.

4. **Stale Graph Handling:**
   - If the index is reported stale after pulling from `development`:
     ```bash
     npx gitnexus analyze
     ```

---

## 3. Solution Structure

- `GenHub/GenHub.Core/` — Pure .NET 8 domain logic, storage (CAS), manifest models, reconciliation (`ReconciliationService`), launchers, downloads. **No UI dependencies.**
- `GenHub/GenHub/` — Avalonia UI layer, MVVM (`CommunityToolkit.Mvvm`), ViewModels, Views, Converters, Dialogs.
- `GenHub/GenHub.Windows/` — Windows host, composition root, Win32 registry & shortcut handlers.
- `GenHub/GenHub.Linux/` — Linux host, composition root, desktop entry generators, X11/Wayland hooks.
- `GenHub/GenHub.MacOS/` — macOS host, composition root, `.app` bundle packaging, quarantine `xattr` management.
- `GenHub/GenHub.Tests/` — Partitioned test suites (`Core`, `Windows`, `Linux`, `MacOS`).
- `.vs/tree.md` — Project file tree reference for discovering existing services and managers.

---

## 4. Mandatory C# Coding Rules & Style

Adhere strictly to `coding-style.md` and `.agent/rules/code-style.md`:

### Constructors & Fields
- **Primary Constructors:** Always use primary constructors for classes and records when dependencies or arguments are injected.
- **No Redundant Fields:** Do not create private instance fields to hold primary constructor parameters. Use constructor parameters directly in class members.

### Collections & Types
- **Prefer `IReadOnlyList<T>` / `IReadOnlyCollection<T>`:** Use over `IEnumerable<T>` for public properties and return types to enable indexed access and avoid multi-enumeration in LINQ.

### Result Pattern (`docs/dev/result-pattern.md`)
- Use `ResultBase`, `OperationResult<T>`, and domain-specific result classes.
- **Do not throw exceptions for predictable control flow** (e.g. file missing, checksum mismatch, network drop, validation error). Return `OperationResult<T>.CreateFailure(...)`.

### Centralized Constants (`docs/dev/constants.md`)
- **NEVER use magic strings or numbers inline.**
- Add all constants to static classes under `GenHub.Core.Constants` (`AppConstants`, `ApiConstants`, `UriConstants`, `StorageConstants`, etc.).

### Syntax, Namespaces & Formatting
- **NEVER use `this.`** to qualify instance members.
- **File-Scoped Namespaces:** Always prefer top-level / file-scoped namespace declarations.
- **Using Directives:** Place all `using` statements at the very top of the file in alphabetical order. No inline namespaces.
- **Comment Casing:** Never capitalize arbitrary words in the middle of a comment; maintain standard sentence casing.
- **Indentation & Braces:** 4 spaces (no tabs), Allman style (opening brace on its own line).
- **Member Ordering (StyleCop):**
  1. Nested types
  2. Static fields
  3. Instance fields
  4. Constructors
  5. Finalizers
  6. Properties
  7. Indexers
  8. Events
  9. Methods (Static first, then instance; ordered `public` -> `protected` -> `internal` -> `private`).

---

## 5. Multi-Platform & Asynchronous Guidelines

- **File Paths:** Always use `Path.Combine` and normalize path separators. Never assume Windows backslashes or case insensitivity.
- **Composition Roots:** Register any new services in platform composition roots (`App.axaml.cs` and platform `Program.cs` files).
- **UI Thread Safety:** Always perform file I/O, hash computing, and network requests asynchronously. Never block the UI thread.
- **Cancellation:** Accept and propagate `CancellationToken` in all long-running service operations.

---

## 6. Build, Test & Tool Commands

```bash
# Build Solution
dotnet build GenHub/GenHub.sln -c Release

# Run Core Tests
dotnet test GenHub/GenHub.Tests/GenHub.Tests.Core/GenHub.Tests.Core.csproj -c Release

# GitNexus Analysis & Freshness
npx gitnexus analyze
npx gitnexus status
npx gitnexus detect-changes
npx gitnexus impact <SymbolName>
```
