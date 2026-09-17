# GenHub

GenHub is a high-performance, cross-platform launcher, profile manager, mod organizer, and content distribution platform for Command & Conquer: Generals and Zero Hour. An Avalonia UI desktop application sits on top of a pure .NET 8 core engine with Content-Addressable Storage (CAS), atomic workspace reconciliation, and multi-source distribution.

You can think of GenHub as the modern, open source, cross-platform ecosystem replacement for legacy GenLauncher and manual game/mod installations.

## What makes GenHub special?

GenHub serves a vibrant, global Command & Conquer community across multiple operating systems. As we iterate on the codebase, we never compromise on these core pillars:

### 1. Content-Addressable Storage (CAS) & Zero-Copy Workspaces

We do not copy multi-gigabyte game directories or duplicate mod files. Game assets and content patches are indexed by cryptographic hash in a shared CAS pool, then hardlinked, symlinked, or atomically materialized into isolated workspaces. Switching complex mods or profiles must happen in milliseconds.

### 2. Multi-platform at the core

Generals was a 2003 Win32 DirectX 8 title. GenHub makes it first-class on modern **Windows**, **Linux** (Wine/Proton), and **macOS** (Wine/CrossOver/native runners). Platform-specific logic (registry lookups, shortcut generation, desktop entries, macOS quarantine `xattr` removal) is strictly isolated inside platform composition hosts, keeping core services portable.

### 3. Shared `development` branch & zero regressions

Every contributor and agent targets the `development` branch. Because changes to core services (storage, reconciliation, manifests, game detectors) ripple across multiple platforms and UI bindings, we do not tolerate blind edits or speculative refactors that break downstream consumers.

### 4. Deterministic architecture & Result pattern

No hidden exceptions for control flow. Operations that can fail (missing files, network drops, checksum mismatches, launch errors) return strongly typed `OperationResult<T>` records. Constants are centralized, constructors are primary, and code is clean, maintainable, and verifiable.

## A note from the maintainers

We like ambitious ideas, simple systems, and software that feels obvious. Do not preserve complexity just because it already exists. Do not introduce machinery because it looks architecturally impressive. Understand the real constraint, then fight for the smallest model that makes the correct behavior unsurprising.

Channel both "measure twice, cut once" and "yagni". Fight scope creep. When touching core logic, inspect caller hierarchies and verify blast radius before writing code.

The rest of this document helps you navigate the codebase and make changes effectively. Think of these instructions as good defaults and firm quality baselines.

## A small glossary

When communicating and reasoning about GenHub, use this language:

- **you** means the agent reading this file and changing GenHub.
- **we, us, and maintainers** mean Community Outpost and the people building GenHub.
- **user** means the player using GenHub to install, mod, and launch Generals / Zero Hour.
- **CAS (Content-Addressable Storage)** means our content-addressable storage pool (`ICasService`, `CasService`) where assets are deduplicated by hash.
- **manifest** means the JSON descriptor (`ContentManifest`, `ManifestId`) defining content components, files, hashes, launch targets, and dependencies.
- **reconciliation** means the atomic process (`ContentReconciliationService`, `IContentReconciliationService`) of turning a clean game installation into a desired profile workspace.
- **workspace** means the active, materialized directory containing linked/deployed game files where the game executable actually runs.
- **profile** means a player-configured setup of game version, active mods, maps, and configuration settings.

## The four ways to hurt yourself

1. **Blind symbol edits.** Never modify core interfaces, storage services, or launcher models without checking caller chains. Modifying a signature in `ICasService`, `IProfileContentService`, or `IContentReconciliationService` can break Windows launch receipts, Linux symlink handlers, and macOS composition roots simultaneously.
2. **Throwing exceptions for control flow.** Never throw custom exceptions for predictable domain failure states (file missing, validation failure, hash mismatch, network failure). Return `OperationResult<T>.CreateFailure(...)`. Cooperative cancellation (`OperationCanceledException`) and contract invariant violations (`ArgumentNullException`, invalid arguments) should follow standard .NET exception semantics.
3. **Hardcoding paths and magic strings.** Never hardcode backslashes `\`, magic constants, URLs, or regexes inline. Always use `Path.Combine` and centralized constants from `GenHub.Core.Constants`.
4. **Hardcoding UI strings.** Never hardcode user-facing strings in XAML views or ViewModels. Always define strings in `GenHub/GenHub/Resources/Localization/Strings.resx` (and maintain strict 1:1 parity with satellite resources: `Strings.ar.resx`, `Strings.ru.resx`, etc.) and reference them via `{localization:Localize ResourceKey}` (namespace `clr-namespace:GenHub.Common.Markup`) in XAML or `ILocalizationService.GetString("ResourceKey")` in C# code. Dynamic lists and view model collections must react to culture change events from `ILocalizationService`.

## Hit every surface

The most common defect in this repository is a change that works on one platform or layer and silently breaks another. Before calling your work done, walk this list:

- **Platforms:** If you change launcher behavior, file materialization, or OS hooks, verify compatibility across Windows (`GenHub.Windows`), Linux (`GenHub.Linux`), and macOS (`GenHub.MacOS`).
- **Composition Roots:** Register shared services in the applicable module under `GenHub/GenHub/Infrastructure/DependencyInjection/` and ensure that module is invoked by `AppServices.ConfigureApplicationServices`. Register platform-specific implementations in the applicable Windows (`WindowsServicesModule`), Linux (`LinuxServicesModule`), and macOS (`MacOSServicesModule`) service modules, and verify each host composes them through its `Program.cs`.\n- **Result Pattern:** Adhere strictly to `docs/dev/result-pattern.md`. All fallible operations (I/O, network, reconciliation, launch, validation) return `OperationResult<T>` or specialized domain result types (`LaunchResult`, `ValidationResult`, `DetectionResult<T>`) rather than throwing exceptions for control flow. Infallible lookups, getters, and predicates return direct types.
- **Constants:** Adhere strictly to `docs/dev/constants.md`. Put constants in `GenHub.Core.Constants` static classes.
- **UI & Styling:** Adhere strictly to `docs/dev/ui-styling.md` and `docs/dev/window-styling.md`. All views and controls must bind to semantic theme tokens from `ThemeResources.axaml` via `{DynamicResource ...}` and use shared controls from `GenHub.Common.Controls` (such as `SidebarLayout`). Never use hardcoded color hexes or custom sidebars. When working on UI, views, or styling, use relevant UI, UX, and design skills to verify layout, accessibility, and visual consistency.
- **User Feedback & Notification Toasts (`INotificationService`):** All user-facing operation results, completion notices (e.g., saves, downloads, deletions, hotkey updates, preset applications, installs), warnings, and error alerts MUST be delivered via `INotificationService` toast notifications (`ShowSuccess`, `ShowInfo`, `ShowWarning`, `ShowError`) with appropriate `NotificationDurations` constants. Never create or rely on ad-hoc status labels, status text blocks, or inline status properties (such as `<TextBlock Text="{Binding StatusMessage}" />` or `StatusText` labels at the bottom of views). Status labels are easily missed and fragment UX; `INotificationService` provides animated, auto-dismissing toasts and archives them into the persistent notification feed.
- **Localization:** Adhere strictly to `docs/dev/localization.md`. Every user-facing UI string (labels, button text, tooltips, dialog titles/messages, placeholders, status messages, faction names, error messages shown to users) must be cataloged in `GenHub/GenHub/Resources/Localization/Strings.resx` with strict 1:1 parity in `Strings.ar.resx` and `Strings.ru.resx`. In Avalonia views, bind with `{localization:Localize Key}` (`xmlns:localization="clr-namespace:GenHub.Common.Markup"`). In ViewModels/services, inject `ILocalizationService` and call `_localizationService.GetString("Key")`. In navigation/sidebar collections, refresh localized titles on `ILocalizationService.PropertyChanged`. Never hardcode user-facing text strings in XAML or code behind.
- **Cancellation & Async:** Every long-running I/O, download, hashing, or reconciliation task must accept and propagate a `CancellationToken`. Never block the UI thread.
- **Reverse states:** If you add a workspace materializer, add its cleanup/reversion path. If you add a cache entry, handle its eviction.

## Code Conventions & Taste

- **Coding Style Authority:** Follow `coding-style.md` and repository [`.editorconfig`](.editorconfig).
- **Toast Notifications Over Status Labels:** Never add inline status labels (e.g., `<TextBlock Text="{Binding StatusMessage}" />`) or ViewModel status strings as the primary way to report success, failure, or progress to the user. Always inject `INotificationService` into ViewModels and dispatch modern toast notifications (`ShowSuccess`, `ShowInfo`, `ShowWarning`, `ShowError`) using `NotificationDurations` constants. Ad-hoc status labels in views are an anti-pattern; user feedback belongs in toast notifications.
- **Localization Standards:** All user-facing UI text (labels, buttons, tooltips, dialogs, validation messages) must be placed in `GenHub/GenHub/Resources/Localization/Strings.resx`.
  - Maintain strict 1:1 parity across `Strings.resx`, `Strings.ar.resx`, and `Strings.ru.resx`. No keys should be missing from satellite resources.
  - Use dot-separated hierarchical keys (`<Feature>.<Context>.<Element>` e.g., `Settings.Appearance.Language.Label`, `Navigation.Settings`).
  - In XAML views: declare `xmlns:localization="clr-namespace:GenHub.Common.Markup"` and bind using `{localization:Localize Key}`.
  - In C# ViewModels / Services: inject `ILocalizationService` and call `_localizationService.GetString("Key")` or `_localizationService.GetString("Key", args)`.
  - For dynamic navigation lists (e.g., `SettingsViewModel.Sections`), subscribe to `ILocalizationService.PropertyChanged` on `CurrentCulture` to re-localize sidebar items live upon language switches.
  - For tool names and plugins, use `LocalizedToolNameConverter` for dynamic translation falling back to the plugin's metadata title.
  - Avoid duplicate keys differing only by case (MSBuild resource generation is case-insensitive on Windows).
  - Do NOT localize internal/technical strings: log templates, JSON property names, protocol values, CLI arguments, or exception messages intended for developers.
- **Primary Constructors:** Always use primary constructors for classes and records when dependencies are injected. Remove redundant private instance fields (e.g., `_logger = logger;`) and use constructor parameters directly in class members.
- **Collection Types:** Prefer `IReadOnlyList<T>` when callers need indexed access and known count, and `IReadOnlyCollection<T>` when only count and enumeration are needed. Avoid raw `IEnumerable<T>` for public properties and return types to prevent unintended deferred multiple enumerations; materialize eagerly (e.g., `.ToList()`, `.ToArray()`, or `ImmutableArray<T>`) when returning collections from services or queries.
- **No `this.`:** Never qualify instance members with `this.`.
- **Namespaces:** Always use file-scoped or top-level namespace declarations. Alphabetize all `using` directives at the very top of the file. Never use inline namespaces.
- **Comment Casing:** Use standard sentence casing in comments. Never capitalize arbitrary words mid-comment.
- **Variables & Declarations:** Always initialize local variables upon declaration. Never leave uninitialized variables (`CS-W1022`) or unused variables (`CS-W1100`). Use discards (`_`) for unused `using` scopes or out parameters.
- **Switch Statements:** Always include a `default` case (`CS-W1009`) in `switch` statements and expressions.
- **Exception Handling:** Never catch generic `Exception` (`CS-R1008`) unless explicitly required for top-level process/worker boundaries. Always catch specific exception types (`IOException`, `UnauthorizedAccessException`, etc.) or re-throw.
- **Cognitive Complexity:** Keep method cognitive complexity strictly below 15 (SonarCloud S3776). Break up complex orchestration into cohesive, single-responsibility private helper methods.
- **Constants & URIs:** Fallback and default gateway/API endpoints must be centralized in `GenHub.Core.Constants.ApiConstants` with environment variable override support (e.g., `GENHUB_UPLOAD_GATEWAY_URL`). Do not scatter URI literals across features.
- **No Unicode Emojis:** Never use emojis in UI views, button labels, tooltips, notifications, logs, code, or comments. Always use clean semantic text, theme brush tokens, or vector SVG StreamGeometry PathIcons.
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

### Static Analysis & Bot Pre-Emption (DeepSource & SonarCloud)

To avoid review roundtrips and CI Quality Gate failures from automated bots, adhere to these enforced baselines:

- **Async Naming Convention (`CS-R1005`):** Any method returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` MUST end with the `Async` suffix (e.g., `DownloadFileAsync`, `ComputeCrcAsync`). Interface implementations and public API contracts must maintain this consistently.
- **SonarCloud Duplication Threshold (< 3.0% on New Code):** SonarCloud fails the CI Quality Gate if duplicated lines on new code exceed 3.0%. When authoring similar ViewModels, dialogs, or service methods, extract shared boilerplate logic into private helper methods or shared base classes before opening a PR.
- **Modern C# Scopes & Lambdas (`CS-R1085`):**
  - Use `using var stream = ...;` statements over nested `using (var stream = ...) { ... }` blocks to prevent excessive indentation.
  - Simplify single-statement lambdas to expression bodies: `x => x.Value` instead of `x => { return x.Value; }`.
  - Avoid deeply nested or multi-level ternary expressions (`? :`). Use `if / else` blocks, switch expressions, or pattern matching instead.
  - Merge adjacent nested `if` statements when there are no `else` branches (`if (a && b)`).
- **Time Representation:** Always use `DateTime.UtcNow` or `DateTimeOffset.UtcNow` for timestamps, file manifests, and metrics. Never use machine-local `DateTime.Now`.
- **Concurrency & Synchronization:** Never lock on `this`, `typeof(...)`, or string literals. Use a dedicated `private readonly object _syncLock = new();` or asynchronous synchronization primitives like `SemaphoreSlim`.
- **CancellationToken Propagation:** Forward `CancellationToken` through every inner async call (`FileStream.ReadAsync`, `HttpClient.SendAsync`, `Task.Delay`). Do not drop cancellation tokens midway through async pipelines.\n\n## Dev & Verification\n\n- **Targeted verification:** Run tests for the specific scope you changed.\n\n  ```bash\n  # Core tests\n  dotnet test GenHub/GenHub.Tests/GenHub.Tests.Core/GenHub.Tests.Core.csproj -c Release\n\n  # Platform-specific tests (on matching OS host)\n  dotnet test GenHub/GenHub.Tests/GenHub.Tests.Windows/GenHub.Tests.Windows.csproj -c Release\n  dotnet test GenHub/GenHub.Tests/GenHub.Tests.Linux/GenHub.Tests.Linux.csproj -c Release\n  dotnet test GenHub/GenHub.Tests/GenHub.Tests.MacOS/GenHub.Tests.MacOS.csproj -c Release\n  ```\n\n- **Do not run repo-wide checks unprompted.** CI owns the full multi-platform matrix.\n- **Solution build:**\n\n  ```bash\n  dotnet build GenHub/GenHub.sln -c Release\n  ```\n\n## Where code lives\n\n- `GenHub/GenHub.Core/` — Core interfaces (`ICasService`, `IContentReconciliationService`, `IToolPlugin`), domain models (`ContentManifest`, `ManifestId`), launcher/detector contracts, constants, and utilities.\n- `GenHub/GenHub/` — Avalonia MVVM application, ViewModels, Views, Converters, Dialogs, and feature implementations (`CasService`, `ContentReconciliationService`, `GameLauncher`, `GameProcessManager`).\n- `GenHub/GenHub.Windows/` — Windows platform host, composition root, registry discovery, Win32 shortcuts.\n- `GenHub/GenHub.Linux/` — Linux platform host, composition root, desktop entries, Wine/Proton runner.\n- `GenHub/GenHub.MacOS/` — macOS platform host, composition root, `.app` bundle hooks, quarantine `xattr` removal.\n- `GenHub/GenHub.Tests/` — Partitioned test suites (`Core`, `Windows`, `Linux`, `MacOS`).\n- `docs/` — Architecture documentation, Result pattern guide (`docs/dev/result-pattern.md`), Constants reference (`docs/dev/constants.md`), UI styling guide (`docs/dev/ui-styling.md`), Window styling standard (`docs/dev/window-styling.md`).\n\n## Pull requests\n\n- Never make a PR unless the developer explicitly asks you to do so.\n- Conventional commit titles, plain language: `fix(core): CAS pool pruning handles locked files`.\n- Body: the problem in a sentence or two, then how you fixed it. End with the model and harness that did the work.\n- UI changes need before/after images. Motion or timing needs a short video.\n- **Never push while checks are running:** NEVER push new commits while CI workflows, platform builds (Windows, Linux, macOS), tests, DeepSource analyzers, or AI bot reviews (CodeRabbit, Kilo) are in progress or queued. Always wait until EVERY check run reaches `status == completed`. Consolidate all fixes and review resolutions into a single pass before pushing.\n- When babysitting: poll checks and all bot comments (including inline review threads and summary 'Outside diff range' findings) newer than the last push. Verify each finding against the source and fix real ones in code. For automated bot threads (DeepSource, Qodo, CodeRabbit, etc.), resolve the discussion directly without posting reply comments; only reply to human maintainers if discussion or clarification is needed. For extended PR workflows, invoke the `pull-request` and `babysit-pr` skills. Stay quiet when nothing is new. Stop when all checks pass on the latest commit with all threads resolved.\n