# GenHub Agent Directives

Welcome to **GenHub** — a high-performance, cross-platform launcher, profile manager, mod organizer, and content distribution platform for Command & Conquer: Generals & Zero Hour.

GenHub is a multi-contributor project where all contributors and agents target the `development` branch. To prevent regressions and interface breaks across multi-platform components, all autonomous and pair-programming agents **must** follow these directives.

---

## 1. Architecture & Code Intelligence (GitNexus)

This repository integrates **GitNexus** to maintain a live, AST-parsed structural knowledge graph of components, symbols, dependencies, and execution flows in `.gitnexus/`.

### Three-Phase Blast Radius Cadence

Agents must not blindly modify code without inspecting call chains and downstream consumers. Follow this three-phase workflow:

```
[ Phase 1: Discovery ]         -> Run gitnexus_impact before editing core classes/interfaces
[ Phase 2: Change Detection ]  -> Run gitnexus_detect_changes before commit/batch completion
[ Phase 3: Gatekeeping ]       -> CI verification & test suite execution on development/PR
```

#### Phase 1 — Discovery (Before Modifying Core Symbols / Interfaces)
- Before altering public methods, service interfaces, storage providers, or launcher cores, inspect the caller hierarchy:
  ```json
  gitnexus_impact({ "target": "<SymbolOrClassName>", "direction": "upstream" })
  ```
- Review all $d=1$ (direct callers) and $d=2$ (indirect dependents) to understand downstream contracts.
- Check affected execution flows via `gitnexus://repo/{name}/processes` or query related flows via `gitnexus_query`.

#### Phase 2 — Change Detection (Pre-Commit / Batch Completion)
- Run `gitnexus_detect_changes()` on modified/staged files before committing.
- Verify that changes to shared core logic do not introduce unexpected side effects in platform-specific UI or background workers.
- When touching multi-platform abstractions (Windows, Linux, macOS file handlers or launcher processes), verify cross-module call flows.

#### Phase 3 — Gatekeeping (Pre-PR & CI)
- Verify that every altered execution path has corresponding unit or integration test coverage.
- CI automatically validates and updates the knowledge graph cache on push to `development` and `main`.

#### Stale Graph Handling
- If the MCP server reports that the index is stale after syncing with `upstream/development`, refresh it locally:
  ```bash
  npx gitnexus analyze
  ```

---

## 2. Solution Structure & Module Boundaries

GenHub follows a clean, modular architecture:

| Project | Responsibility | Dependencies / Rules |
| --- | --- | --- |
| `GenHub/GenHub.Core/` | Domain models, storage engines, Content-Addressable Storage (CAS), manifest generation, reconciliation engine (`ReconciliationService`), game detectors, launcher engines, download managers. | **No UI dependencies.** Pure .NET 8 domain logic. |
| `GenHub/GenHub/` | Avalonia UI application layer, MVVM (`CommunityToolkit.Mvvm`), ViewModels, Views, XAML controls, Converters, Dialog services. | References `GenHub.Core`. Presentation layer only. |
| `GenHub/GenHub.Windows/` | Windows platform host, composition root, Windows registry readers, shortcut generators, Win32 launcher integrations. | References `GenHub` and `GenHub.Core`. |
| `GenHub/GenHub.Linux/` | Linux platform host, composition root, desktop entry generators, X11/Wayland hooks, Linux runner scripts. | References `GenHub` and `GenHub.Core`. |
| `GenHub/GenHub.MacOS/` | macOS platform host, composition root, `.app` bundle hooks, gatekeeper/quarantine attribute managers (`xattr`). | References `GenHub` and `GenHub.Core`. |
| `GenHub/GenHub.Tests/` | Unit and integration test suites partitioned per project (`GenHub.Tests.Core`, `GenHub.Tests.Windows`, `GenHub.Tests.Linux`, `GenHub.Tests.MacOS`). | Tests must not rely on active game installations. |

> **Tip:** For project-wide file discovery, consult `.vs/tree.md` for an index of existing services, managers, and view models.

---

## 3. Mandatory C# Coding Standards & Conventions

All code generated for GenHub must strictly adhere to the project's coding standards (`coding-style.md` and `.agent/rules/code-style.md`):

### 1. Primary Constructors
- **Always use primary constructors** for classes, structs, and records when dependencies or parameters are provided at instantiation.
- **Remove redundant private instance fields**: Do not assign primary constructor parameters to private readonly fields (e.g. `_logger = logger;`). Use constructor parameters directly throughout class members.

### 2. Collection Types
- **Prefer `IReadOnlyList<T>` or `IReadOnlyCollection<T>` over `IEnumerable<T>`** for public properties, method parameters, and return types where indexing, count checks, or multi-pass LINQ evaluation are required.

### 3. Result Pattern Architecture
- **Strictly adhere to the Result Pattern (`docs/dev/result-pattern.md`)**.
- Use `ResultBase`, `OperationResult<T>`, and domain-specific result classes (`CreateSuccess`, `CreateFailure`).
- **Do not throw exceptions for predictable control flow** (e.g. file missing, checksum mismatch, download error, validation failure). Return an `OperationResult<T>` with meaningful error descriptions.
- Reserve exceptions solely for unrecoverable runtime states or invalid argument invariants.

### 4. Centralized Constants Pattern
- **Strictly adhere to the Constants Reference (`docs/dev/constants.md`)**.
- **NEVER use magic strings, numbers, or hardcoded URLs/regex patterns in implementation logic.**
- Place all constants in designated static classes within `GenHub.Core.Constants` (e.g., `AppConstants`, `ApiConstants`, `UriConstants`, `StorageConstants`, `LauncherConstants`).

### 5. Syntax & Cleanliness Directives
- **NEVER use `this.`**: Do not qualify instance members with `this.` (e.g. use `Property` or `_field`, never `this.Property`).
- **File-Scoped Namespaces & Usings**:
  - Always prefer file-scoped or top-level namespace declarations (`namespace GenHub.Core.Services;`).
  - Place all `using` directives at the very top of the file, ordered alphabetically.
  - Never use inline namespaces.
- **Comment Casing**: Maintain consistent standard sentence casing in code comments. **Never capitalize arbitrary words** in the middle of a comment.
- **XML Documentation**: Provide XML doc comments (`/// <summary>`) on all `public` and `protected` classes, interfaces, properties, and methods.

### 6. Strict Member Ordering (StyleCop Compliance)
Class members must strictly follow this order:
1. Nested types (enums, inner classes)
2. Static fields (const, static readonly)
3. Instance fields (private fields prefixed with `_camelCase`)
4. Constructors (primary constructor or explicit constructors)
5. Finalizers
6. Properties
7. Indexers
8. Events
9. Methods:
   - Static methods first, then instance methods.
   - Ordered by visibility: `public`, `protected`, `internal`, `private`.

---

## 4. Multi-Platform & Runtime Directives

- **Cross-Platform Path Handling**: Always use `Path.Combine` or cross-platform relative path utilities. Never hardcode Windows-style backslashes `\` or assume case-insensitive file systems.
- **Platform Composition Roots**: When registering new services in dependency injection, ensure registrations are appropriately added to `App.axaml.cs` and platform-specific entry points (`GenHub.Windows/Program.cs`, `GenHub.Linux/Program.cs`, `GenHub.MacOS/Program.cs`).
- **UI Thread Safety**: Never block the UI thread. Perform I/O, hash calculations, file copies, and network requests asynchronously using `async`/`await`.
- **Cancellation Token Support**: All asynchronous service operations that execute long-running tasks (downloads, reconciliation, hashing, process monitoring) **must accept and honor a `CancellationToken`**.

---

## 5. Development & Verification Commands

### Build Commands
```bash
# Build the entire solution
dotnet build GenHub/GenHub.sln -c Release

# Build specific platform projects
dotnet build GenHub/GenHub.Windows/GenHub.Windows.csproj -c Release
dotnet build GenHub/GenHub.Linux/GenHub.Linux.csproj -c Release
dotnet build GenHub/GenHub.MacOS/GenHub.MacOS.csproj -c Release
```

### Test Commands
```bash
# Run Core tests
dotnet test GenHub/GenHub.Tests/GenHub.Tests.Core/GenHub.Tests.Core.csproj -c Release

# Run all non-platform-restricted tests
dotnet test GenHub/GenHub.Tests/GenHub.Tests.Core/GenHub.Tests.Core.csproj --verbosity normal
```

### GitNexus CLI Commands
```bash
# Analyze & refresh knowledge graph
npx gitnexus analyze

# Check graph status
npx gitnexus status

# Detect changes from current git diff
npx gitnexus detect-changes

# Impact / blast radius analysis on a symbol
npx gitnexus impact <SymbolName>
```
