# GenHub

GenHub is a high-performance, cross-platform launcher, profile manager, mod organizer, and content distribution platform for Command & Conquer: Generals and Zero Hour. An Avalonia UI desktop application sits on top of a pure .NET 8 core engine with Content-Addressable Storage (CAS), atomic workspace reconciliation, and multi-source distribution.

You can think of GenHub as the modern, open source, cross-platform ecosystem replacement for legacy GenLauncher and manual game/mod installations.

## What makes GenHub special?

GenHub serves a vibrant, global Command & Conquer community across multiple operating systems. As we iterate on the codebase, we never compromise on these core pillars:

### 1. Content-Addressable Storage (CAS) & Zero-Copy Workspaces
We do not copy multi-gigabyte game directories or duplicate mod files. Game assets and content patches are indexed by cryptographic hash in a shared CAS pool, then hardlinked, symlinked, or atomically materialized into isolated workspaces. Switching complex mods or profiles must happen in milliseconds.

### 2. Multi-platform at the core
Generals was a 2003 Win32 DirectX 8 title. GenHub makes it first-class on modern **Windows**, **Linux** (Wine/Proton), and **macOS** (Wine/CrossOver/native runners). Platform-specific logic (registry lookups, shortcut generation, desktop entries, macOS quarantine `xattr` removal) is strictly isolated inside platform composition hosts, keeping `GenHub.Core` completely platform-agnostic.

### 3. Shared `development` branch & zero regressions
Every contributor and agent targets the `development` branch. Because changes to core services (storage, reconciliation, manifests, game detectors) ripple across multiple platforms and UI bindings, we do not tolerate blind edits or speculative refactors that break downstream consumers.

### 4. Deterministic architecture & Result pattern
No hidden exceptions for control flow. Operations that can fail (missing files, network drops, checksum mismatches, launch errors) return strongly typed `OperationResult<T>` records. Constants are centralized, constructors are primary, and code is clean, maintainable, and verifiable.

## A note from the maintainers

We like ambitious ideas, simple systems, and software that feels obvious. Do not preserve complexity just because it already exists. Do not introduce machinery because it looks architecturally impressive. Understand the real constraint, then fight for the smallest model that makes the correct behavior unsurprising.

Channel both "measure twice, cut once" and "yagni". Fight scope creep. When touching core logic, inspect caller hierarchies and verify blast radius with GitNexus before writing code.

The rest of this document helps you navigate the codebase and make changes effectively. Think of these instructions as good defaults and firm quality baselines.

## A small glossary

When communicating and reasoning about GenHub, use this language:

- **you** means the agent reading this file and changing GenHub.
- **we, us, and maintainers** mean Community Outpost and the people building GenHub.
- **user** means the player using GenHub to install, mod, and launch Generals / Zero Hour.
- **CAS (Content-Addressable Storage)** means our content-addressable storage pool (`CasService`) where assets are deduplicated by hash.
- **manifest** means the JSON descriptor (`ContentManifest`, `ManifestId`) defining content components, files, hashes, launch targets, and dependencies.
- **reconciliation** means the atomic process (`ReconciliationService`) of turning a clean game installation into a desired profile workspace.
- **workspace** means the active, materialized directory containing linked/deployed game files where the game executable actually runs.
- **profile** means a player-configured setup of game version, active mods, maps, and configuration settings.

## The three ways to hurt yourself

1. **Blind symbol edits.** Never modify core interfaces, storage services, or launcher models without checking caller chains via `gitnexus_impact`. Modifying a signature in `CasService`, `IContentService`, or `IReconciliationService` can break Windows launch receipts, Linux symlink handlers, and macOS composition roots simultaneously.
2. **Throwing exceptions for control flow.** Never throw exceptions for predictable failure states (file missing, validation failure, hash mismatch, cancelled task). Return `OperationResult<T>.CreateFailure(...)`. Reserve exceptions solely for fatal runtime invariants.
3. **Hardcoding paths and magic strings.** Never hardcode backslashes `\`, magic constants, URLs, or regexes inline. Always use `Path.Combine` and centralized constants from `GenHub.Core.Constants`.

## Hit every surface

The most common defect in this repository is a change that works on one platform or layer and silently breaks another. Before calling your work done, walk this list:

- **Platforms:** If you change launcher behavior, file materialization, or OS hooks, verify compatibility across Windows (`GenHub.Windows`), Linux (`GenHub.Linux`), and macOS (`GenHub.MacOS`).
- **Composition Roots:** Any new service registered in `GenHub.Core` or UI must be registered across `App.axaml.cs` and each platform host's `Program.cs`.
- **Result Pattern:** Adhere strictly to `docs/dev/result-pattern.md`. All public service methods return `OperationResult<T>` or `ResultBase`.
- **Constants:** Adhere strictly to `docs/dev/constants.md`. Put constants in `GenHub.Core.Constants` static classes.
- **Cancellation & Async:** Every long-running I/O, download, hashing, or reconciliation task must accept and propagate a `CancellationToken`. Never block the UI thread.
- **Reverse states:** If you add a workspace materializer, add its cleanup/reversion path. If you add a cache entry, handle its eviction.

## Architecture & Code Intelligence (GitNexus)

This repository uses **GitNexus** to maintain an AST-parsed structural knowledge graph of components, symbols, dependencies, and execution flows in `.gitnexus/`.

### The Three-Phase Cadence:

1. **Phase 1 — Discovery (Before Modifying Core Symbols / Interfaces):**
   - Run `gitnexus_impact` to inspect upstream callers and downstream dependents:
     ```json
     gitnexus_impact({ "target": "<SymbolOrClassName>", "direction": "upstream" })
     ```
   - Review $d=1$ (will break) and $d=2$ (likely affected) dependencies before altering signatures.
   - Check affected flows via `gitnexus://repo/{name}/processes` or `gitnexus_query(...)`.

2. **Phase 2 — Change Detection (Pre-Commit / Batch Verification):**
   - Run `gitnexus_detect_changes()` on modified/staged files to map diffs against execution flows.
   - Confirm that changes touching cross-platform abstractions (CAS, launcher, file handlers) stay intact.

3. **Phase 3 — Gatekeeping (Pre-PR & CI):**
   - CI builds, caches, and validates the `.gitnexus/` knowledge graph on push to `development` and `main`.
   - PR CI runs `gitnexus detect-changes` to surface blast radius in GitHub Step Summaries.
   - If the local graph is stale after pulling `development`:
     ```bash
     npx gitnexus analyze
     ```

## Code Conventions & Taste

- **Primary Constructors:** Always use primary constructors for classes and records when dependencies are injected. Remove redundant private instance fields (e.g., `_logger = logger;`) and use constructor parameters directly in class members.
- **Collection Types:** Prefer `IReadOnlyList<T>` / `IReadOnlyCollection<T>` over `IEnumerable<T>` for public properties and return types to ensure indexed access and avoid multi-pass LINQ enumeration.
- **No `this.`:** Never qualify instance members with `this.`.
- **Namespaces:** Always use file-scoped or top-level namespace declarations. Alphabetize all `using` directives at the very top of the file. Never use inline namespaces.
- **Comment Casing:** Use standard sentence casing in comments. Never capitalize arbitrary words mid-comment.
- **Formatting:** 4 spaces indentation, Allman bracing style (opening brace on its own line), nullable reference types enabled.
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

## Dev & Verification

- **Targeted verification:** Run tests for the specific scope you changed.
  ```bash
  # Core tests
  dotnet test GenHub/GenHub.Tests/GenHub.Tests.Core/GenHub.Tests.Core.csproj -c Release
  ```
- **Do not run repo-wide checks unprompted.** CI owns the full multi-platform matrix.
- **Solution build:**
  ```bash
  dotnet build GenHub/GenHub.sln -c Release
  ```
- **GitNexus CLI:**
  ```bash
  npx gitnexus analyze          # Build/refresh graph
  npx gitnexus status           # Inspect status
  npx gitnexus detect-changes   # Map git diff to affected flows
  npx gitnexus impact <Symbol>  # Symbol blast radius
  ```

## Where code lives

- `GenHub/GenHub.Core/` — Pure .NET 8 domain logic, CAS storage (`CasService`), manifest models, atomic reconciliation (`ReconciliationService`), launchers, game detectors. **Zero UI dependencies.**
- `GenHub/GenHub/` — Avalonia MVVM application, ViewModels, Views, Converters, Dialogs.
- `GenHub/GenHub.Windows/` — Windows platform host, composition root, registry discovery, Win32 shortcuts.
- `GenHub/GenHub.Linux/` — Linux platform host, composition root, desktop entries, Wine/Proton runner.
- `GenHub/GenHub.MacOS/` — macOS platform host, composition root, `.app` bundle hooks, quarantine `xattr` removal.
- `GenHub/GenHub.Tests/` — Partitioned test suites (`Core`, `Windows`, `Linux`, `MacOS`).
- `docs/` — Architecture documentation, Result pattern guide (`docs/dev/result-pattern.md`), Constants reference (`docs/dev/constants.md`).
- `.vs/tree.md` — Project file tree for fast service/manager discovery.

## Pull requests

- Target the `development` branch.
- Conventional commit titles in plain language: `feat(core): add CAS pool pruning support`, `fix(launch): clear quarantine on macOS game binaries`.
- Body: the problem in a sentence or two, followed by how you fixed it.
- One concern per PR.
