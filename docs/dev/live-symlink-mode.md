# Live Symlink Mode (Developer Mode) — Technical Design

Status: implemented in this fork (P0 + safety pass).
Scope: local loose-file content only (Mod / Addon / Patch folders). Remote and
archive content stay on the existing CAS snapshot pipeline.

## 1. Problem

GenHub treats every imported file as an immutable content-addressed blob:

1. `LocalContentService.CreateLocalContentManifestAsync` hashes the folder.
2. `ContentStorageService.StoreContentAsync` copies every file into CAS and
   rewrites the manifest entries to `ContentSourceType.ContentAddressable`.
3. A workspace strategy materializes links to those CAS blobs.

For a mod developer this means every edit requires a re-import or a ModBuilder
build, and a workspace link never points at the folder being edited.

The requested behaviour is the opposite: the source folder is the content, the
workspace points at it, and the folder may change while GenHub is running.

## 2. Goals and non-goals

Goals:

- G1. Register a local folder as live content without hashing or copying it.
- G2. Build a workspace whose entries resolve to the source folder.
- G3. Reflect source additions, deletions, renames and edits without rebuilding
  the profile or restarting GenHub.
- G4. Keep the source folder free of writes made by the game, or make the
  one-way behaviour explicit and configurable.
- G5. Never silently substitute a different link type. Every capability failure
  is reported with a remediation.

Non-goals:

- Live content from archives, URLs or network shares.
- Sharing or exporting profiles that reference live content.
- Reloading assets already loaded by a running game process. The engine reads
  files at load time; "live" means the next map or process start sees them.
- A user-mode copy-on-write filesystem filter. Windows offers no such hook to a
  normal process, so G4 is implemented by policy, not interception.

## 3. Decisions

### D1. Live content is a manifest source mode, not a new content type

`ContentManifest` gains `SourceMode` (`Snapshot` | `Live`). A live manifest has:

- `SourceMode = Live`,
- `SourcePath` = absolute registered root,
- file entries with `SourceType = LocalFile`, `SourcePath` = path relative to the
  root, no hash, and a size captured at registration time.

`LocalFile` already resolves through `WorkspaceCompatibilityHelper.ResolveSourcePath`
(manifest source directory + relative path), so the existing strategy base can
materialize a live file without new resolution rules.

The manifest remains metadata: it is stored through the existing metadata-only
path (`source.path` + manifest JSON) and never enters CAS.

### D2. A second materialization pass, not a new workspace strategy

The workspace is assembled in two phases:

1. the profile's existing strategy (`HardLink`, `FullCopy`, `SymlinkOnly`,
   `HybridCopySymlink`) materializes every snapshot manifest exactly as it does
   today — base game, engine, CAS-backed `.big` packages and every other
   imported package;
2. `LiveWorkspaceMaterializer` overlays the live manifests as links to their
   source roots, then removes stale live links.

Reasons:

- live mode is a property of one imported package, not of the whole profile;
- a normal profile mixes CAS content and live content, so replacing the
  strategy would also change how the base game and the other packages are
  materialized;
- no new enum value, no strategy-selection UI, no change to the player-facing
  strategy behaviour.

`WorkspaceInfo.Strategy` stays the profile's chosen strategy;
`WorkspaceInfo.IsLive` marks the mix.

### D3. A full rescan on prepare, a watcher during the session

- `LiveWorkspaceMaterializer.ApplyAsync` enumerates the source root every time. This
  makes "files added while GenHub was closed" work, and removes stale links.
- `LiveWorkspaceSyncService` watches each registered source root and applies
  add/delete/rename deltas while GenHub runs.
- The watcher is an accelerator, never the source of truth. A buffer overflow or
  a missed event is repaired by the next prepare or by an explicit resync.

### D4. Per-file symlink by default, junction as an option, copy-on-write for
game-writable files

`LiveLinkKind`:

- `FileSymlink` (default): one symlink per file. Overlay-safe: a live mod can
  share `Data/` with the base game and other addons.
- `AutoJunction`: junction a top-level directory only when the live manifest is
  the exclusive owner of that subtree and the subtree contains no
  game-writable file. Everything else stays a per-file symlink. Junctioned
  subtrees see additions instantly and need no watcher events.

Write policy (section 7):

- `ProtectGameWritableFiles` (default on): extensions that the engine commonly
  writes (`.ini`, `.cfg`, `.txt`, `.log`, `.rep`, `.sav`, `.xml`, `.json`) are
  copied into the workspace instead of linked. The watcher refreshes the copy
  when the developer edits the source.
- `PureLink` (explicit opt-in): everything is a symlink; the user accepts that a
  write through the link reaches the source.

### D5. The workspace stays disposable; the source folder is never owned

Cleanup deletes the workspace directory with the existing reparse-point-safe
deleter (`FileOperationsService.DeleteDirectoryIfExists`, which never recurses
into junctions or symlinks). A live workspace must survive:

- `WorkspaceManager.CleanupWorkspaceAsync`,
- `ForceRecreate` on content change (`ProfileEditorFacade`),
- profile deletion and the Danger Zone cleanup,

without deleting or modifying a single source file. Tests must assert byte and
timestamps equality on the source tree after cleanup.

### D6. Live content is machine-local

Profiles and manifests that contain a live source cannot be exported, shared or
uploaded: the manifest carries an absolute local path and the content is not
reproducible. `ProfileSharingService` must refuse with a localized message.

## 4. Data model changes

### 4.1 New enums

`GenHub/GenHub.Core/Models/Enums/ContentSourceMode.cs`

```csharp
public enum ContentSourceMode
{
    Snapshot = 0,
    Live = 1,
}
```

`GenHub/GenHub.Core/Models/Enums/LiveLinkKind.cs`

```csharp
public enum LiveLinkKind
{
    FileSymlink = 0,
    AutoJunction = 1,
}
```

No `WorkspaceStrategy` value is added. The profile keeps its existing strategy,
and the live overlay is applied on top of it (section 6).

### 4.2 `ContentManifest`

Add, and copy in the copy constructor:

```csharp
[JsonPropertyName("sourceMode")]
public ContentSourceMode SourceMode { get; set; } = ContentSourceMode.Snapshot;

[JsonPropertyName("liveLinkKind")]
public LiveLinkKind LiveLinkKind { get; set; } = LiveLinkKind.FileSymlink;
```

Validation in `ContentManifestPool.ValidateManifest`:

- live manifest must have an absolute `SourcePath`,
- live manifest must not contain `ContentAddressable` files,
- live manifest must contain at least one `LocalFile` (unless a future empty
  content case is added).

### 4.3 `LocalContentOptions`

```csharp
public ContentSourceMode SourceMode { get; init; } = ContentSourceMode.Snapshot;
public LiveLinkKind LiveLinkKind { get; init; } = LiveLinkKind.FileSymlink;
public bool ProtectGameWritableFiles { get; init; } = true;
```

### 4.4 `WorkspaceConfiguration`

```csharp
public bool HasLiveSources =>
    Manifests.Any(m => m.SourceMode == ContentSourceMode.Live);
```

### 4.5 `WorkspaceInfo`

```csharp
public bool IsLive { get; set; }
public List<LiveSourceBinding> LiveSources { get; set; } = [];
```

`LiveSourceBinding` records manifest id, absolute root, and link kind. It is
diagnostic and lets the UI show source health; it is not required to resolve
links.

### 4.6 Constants

`GenHub/GenHub.Core/Constants/LiveContentConstants.cs`:

- ignore directory names: `.git`, `.svn`, `.hg`, `.vs`, `.vscode`, `bin`, `obj`,
  `node_modules`, `.genhub`
- ignore file suffixes: `.tmp`, `.log` (source-side logs only), `.pdb`
- protected extensions (section 7)
- watcher debounce (250 ms), buffer size (64 KiB), resync throttle
- link overhead per entry (1 KiB)
- localization keys

## 5. Import flow

### 5.1 UI

`AddLocalContentViewModel`:

- new `IsLiveSource` observable property and a toggle in
  `AddLocalContentView.axaml`;
- when the toggle is on, the selected folder is not copied to `_stagingPath`;
  `SourcePath` is the content root and the tree preview reads the folder
  directly;
- archives and files are rejected in live mode with a localized message;
- `ConfirmAddAsync` validates `Directory.Exists(SourcePath)` and calls the new
  registration method instead of the CAS import.

Warning text (must be explicit): the folder is linked, not copied; GenHub never
owns it; moving or deleting it breaks profiles; the folder is not a backup.

### 5.2 Service

New method on `ILocalContentService`:

```csharp
Task<OperationResult<ContentManifest>> RegisterLiveSourceAsync(
    string sourceRoot,
    string name,
    ContentType contentType,
    GameType targetGame,
    LocalContentOptions? options,
    CancellationToken cancellationToken = default);
```

Implementation in `LocalContentService`:

1. Validate the root:
   - exists, is a directory, is fully qualified;
   - is not the CAS root, the application data root, or the workspace root;
   - is not itself a reparse point;
   - is on a fixed/eligible drive (network roots rejected in P0).
2. Reject `ContentType` values that do not make sense for loose files (maps and
   replays keep the existing user-data path).
3. Build the manifest without hashing (section 5.3).
4. Store metadata only through the new
   `IContentStorageService.RegisterExternalContentAsync`.
5. Return the manifest.

Display names are preserved verbatim. Manifest IDs only accept ASCII
alphanumerics, so a pure Chinese or punctuation-only name would normalize to
an empty string and throw in `ManifestIdGenerator`. `LocalContentService`
therefore derives a deterministic ASCII identity segment: the existing
sanitized name, or `content-<hash8>`, or `<sanitized>-<hash8>` when the name
contains non-ASCII characters. The first four ID segments stay unchanged.

### 5.3 Manifest generation

`ManifestGenerationService.CreateLiveContentManifestAsync(...)` mirrors
`CreateContentManifestAsync` but calls a new builder overload:

```csharp
builder.AddFilesFromDirectoryAsync(
    root,
    ContentSourceType.LocalFile,
    computeHash: false,
    useRelativeSourcePaths: true,
    ...);
```

`ContentManifestBuilder` changes:

- `AddFilesFromDirectoryAsync` gains `bool computeHash = true` and
  `bool useRelativeSourcePaths = false`; defaults preserve every existing call.
- `CreateDirectoryManifestFileAsync` writes `SourcePath = relativePath` in
  relative mode instead of the absolute directory.
- `Size` is still recorded (useful for progress and display), `Hash` stays empty.
- `InstallTarget` keeps the existing rule; only `Workspace` targets are linked by
  the materializer. Maps and replays continue through the user-data linker.

### 5.4 Storage

`IContentStorageService` gains:

```csharp
Task<OperationResult<ContentManifest>> RegisterExternalContentAsync(
    ContentManifest manifest,
    string sourceDirectory,
    CancellationToken cancellationToken = default);
```

`ContentStorageService` implements it as a public wrapper around the existing
private `StoreManifestOnlyAsync` (writes `Data/<manifestId>/source.path`, writes
the manifest JSON, tracks references; live manifests contribute no CAS hashes).
No file copy, no hashing.

### 5.5 Deletion and update

- `DeleteLocalContentAsync` already removes the manifest and the metadata
  directory; the source root is never touched.
- Editing a live item re-registers the same manifest id and rewrites the source
  path. Switching an item between snapshot and live is a new content item in P0
  (mode switching is P2).

## 6. Workspace assembly

### 6.1 Two-phase assembly in `WorkspaceManager`

`WorkspaceManager.PrepareWorkspaceAsync` keeps the profile's strategy for every
snapshot manifest and adds a live overlay:

1. split `configuration.Manifests` into `snapshot` and `live`;
2. build `strategyConfiguration`: same id, roots, game client, strategy and
   `ManifestSourcePaths`, `Manifests = snapshot`;
3. run the selected strategy on `strategyConfiguration` exactly as today. The
   base game, engine binaries, CAS-backed `.big` packages and every other
   imported package are materialized by HardLink / FullCopy / SymlinkOnly /
   Hybrid, unchanged;
4. if `live` is non-empty, run
   `LiveWorkspaceMaterializer.ApplyAsync(workspaceInfo, configuration,
   strategyConfiguration)`;
5. set `workspaceInfo.IsLive = true` and `workspaceInfo.LiveSources`;
6. `ILiveWorkspaceSyncService.Attach(workspaceInfo, configuration)`;
7. finalize validation and metadata with the original `configuration`, so
   `ManifestIds` includes both snapshot and live manifests.

If a profile contains only live manifests (unusual, because the base game is
always snapshot content), step 3 is skipped and the workspace directory is
created empty before the overlay.

When an existing live workspace is available, `WorkspaceManager` runs a live-only
incremental path instead of re-running the snapshot strategy:

- the existing workspace must be `IsLive`, use the same strategy, and its
  snapshot manifest IDs (order and versions) must match the current snapshot set;
- removing a live manifest is incremental only when none of its entries occupied a
  snapshot path; otherwise deleting the entry would leave a hole and the manager
  falls back to a full rebuild;
- when the last live manifest is removed, `LiveWorkspaceMaterializer.ClearAsync`
  deletes only the live entries and keeps the snapshot workspace;
- snapshot content changes, strategy changes, or a hole risk set
  `ForceRecreate = true` and use the existing full-rebuild path.

`ProfileEditorFacade` no longer forces `ForceRecreate` for content changes, and
`GameProfileManager` no longer deletes the workspace when live content is
removed. Both leave the decision to `WorkspaceManager`, which reconciles live-only
changes and rebuilds only when necessary.

### 6.2 `LiveWorkspaceMaterializer`

`GenHub/GenHub/Features/Workspace/Live/LiveWorkspaceMaterializer.cs`

```csharp
Task<OperationResult<WorkspaceInfo>> ApplyAsync(
    WorkspaceInfo workspaceInfo,
    WorkspaceConfiguration fullConfiguration,
    WorkspaceConfiguration strategyConfiguration,
    IProgress<WorkspacePreparationProgress>? progress,
    CancellationToken cancellationToken);
```

Steps:

1. Build the set of snapshot workspace paths, used to log live overrides and to
   detect when removing a live entry would leave a snapshot path empty.
2. Resolve each live root (`ManifestSourcePaths[manifestId]`, falling back to
   `manifest.SourcePath`) and fail when it is missing or is no longer a
   directory.
3. Materialize every workspace-target file under each root through
   `ILiveWorkspaceLinker`:
   - declared entries supply the known `InstallTarget`;
   - discovered entries use the shared install-target resolver extracted from
     `ContentManifestBuilder.DetermineInstallTarget`, so `.map` / `.rep` still go
     to the user-data linker and are not linked into the workspace.
4. Remove stale state entries: links and copies recorded for a manifest that is
   no longer enabled are deleted. A symlink whose target resolves under a live
   root and whose relative path is no longer in the plan is also deleted.
5. Update `FileCount`, `TotalSizeBytes` and `LiveSources`.
6. Persist the new ownership state.

Errors are collected per file and surfaced as one actionable failure; a
privilege failure aborts before any fallback. The materializer never calls
`EnsureExecutableAsync` on a live entry (section 6.4).

### 6.3 Conflict resolution

Live content is the developer's working copy and has absolute precedence over
snapshot content:

- if a live file and a snapshot manifest resolve to the same relative path, the
  live file replaces the snapshot entry. The materializer logs and counts the
  overridden paths;
- among live manifests, the existing rule applies: higher `ContentTypePriority`
  wins, and on a tie the later manifest in profile order wins. This keeps a live
  Mod above a live Addon;
- snapshot-only conflicts are unchanged and still use
  `GetWorkspaceUniqueFileEntries` before the live overlay runs.

The snapshot strategy is never given a live manifest; the split is enforced by
`WorkspaceManager.SplitManifests`.

### 6.4 Link materialization

`ILiveWorkspaceLinker` (shared by the materializer and the watcher):

```csharp
Task<OperationResult<bool>> EnsureEntryAsync(
    LiveWorkspaceContext context,
    string relativePath,
    string sourcePath,
    CancellationToken cancellationToken);
```

Rules:

- Resolve and verify that `sourcePath` is inside a registered live root
  (`Path.GetFullPath` prefix check with a trailing separator).
- Ensure the target directory exists.
- On the initial apply, a snapshot entry at the same path is deleted before the
  live entry is created for copies, and replaced for links. On watcher rescans,
  a newer regular file at a protected path is treated as a game write and kept.
- If the target already exists:
  - symlink resolving to the same source: no-op;
  - symlink to another source: delete the link and recreate;
  - regular file that the state store owns (protected copy): refresh or no-op;
  - regular file not owned by GenHub: replaced on the initial apply because live
    content won; preserved on a watcher rescan.
- Symlink creation uses `FileOperations.CreateSymlinkAsync(...,
  allowFallback: false, ...)`.
- `UnauthorizedAccessException` / `IOException` on Windows is converted to a
  `LiveLinkPrivilegeException` with the remediation message
  (`WorkspaceConstants.ZeroCopyElevationGuidance` plus the link kind advice).
- Junction entries use `IFileOperationsService.CreateDirectoryJunctionAsync`
  (new, implemented with `cmd /c mklink /J`, draining stdout/stderr, timeout and
  kill path per the repository process rules); junction creation does not
  require elevation.
- Never call `EnsureExecutableAsync` on a live entry: on Unix it would chmod the
  source file through the symlink. The materializer owns live entries and skips
  that step; the entry point is the base game executable, materialized by the
  normal strategy.

### 6.5 Directory junction planning (`AutoJunction`, out of scope)

> Not implemented in this fork. Directory junctions are left to the upstream
> author because they change write semantics and directory-level precedence.

For each top-level directory of a live root:

- junction it only if no other manifest declares a file under that prefix and no
  protected path exists under it;
- otherwise keep per-file links for that subtree;
- never junction the workspace root itself.

Ordering matters. Plan the junction roots before materializing any declared
entry, then skip every declared or discovered file under a junctioned prefix.
Otherwise the per-file loop would try to create links inside a directory that
already resolves to the source tree.

Junctioned subtrees need no watcher work; on delete, the existing cleanup handles
a reparse point non-recursively.

### 6.6 Disk estimate

The materializer reports `linkCount * LinkOverheadBytes` plus the sizes of
protected copies; junction entries cost one reparse entry. The profile strategy
keeps its own estimate for the snapshot content.

### 6.7 Cleanup

`WorkspaceManager.CleanupWorkspaceAsync`:

- detach the live session before deleting,
- delete the workspace with the existing reparse-safe deleter,
- never call any recursive delete on a `LinkTarget` or on a source root.

`AnalyzeCleanupAsync` returns no confirmation for live workspaces: links are
cheap and removing them is not data loss.

### 6.8 Relationship to dependency fields and BIG load order

Three mechanisms are often confused with workspace file precedence:

- `ContentDependency.DependencyType` is a `ContentType`, and
  `DependencyInstallBehavior` is `RequireExisting`, `AutoInstall`, `Optional` or
  `Suggest`. These control whether content is installed, not which file wins a
  workspace path.
- The `DependencyType` enum contains `Required`, `Recommended`, `Bundled` and
  `Optional`. `Conflicting` is not an enum value; conflicts are expressed by
  `ContentDependency.ConflictsWith` and `IsExclusive`.
- `ContentManifest.BaseContentId` appears in older documentation but is not
  present in the current model. Addon/base relationships are resolved through
  dependencies; they do not participate in workspace assembly.
- `!Hotkeys_...` is a game-level `.big` convention: Generals and Zero Hour mount
  `.big` archives in alphabetical order, so a leading `!` makes the archive
  override vanilla archives. It does not apply to loose files, and GenHub must not
  rename arbitrary live files. For a `.big` overlay, the addon itself must ship
  the `!`-prefixed filename.

Workspace precedence remains path-based: live content always replaces a snapshot
file with the same relative path. Dependency fields do not override that rule.

### 6.9 Incremental live preparation

`WorkspaceManager.TryPrepareLiveIncrementalAsync` runs before the strategy and
`WorkspaceReconciler` is left untouched:

- it requires an existing workspace with `IsLive == true`, the same strategy, and
  the same snapshot manifest IDs in the same order with the same versions;
- `LiveWorkspaceMaterializer.CanApplyIncrementallyAsync` loads the state store and
  compares removed entries against the snapshot path set. If a removed live entry
  occupied a snapshot path, it returns false and the manager sets
  `ForceRecreate = true`;
- when incremental is safe, `ApplyAsync(..., replaceSnapshotTargets: false)` adds
  new live entries and removes only the state-owned entries of disabled live
  manifests. No snapshot file is rewritten;
- when the last live manifest is removed,
  `CanClearIncrementallyAsync` performs the same hole check and
  `ClearAsync` removes only live entries, sets `IsLive = false`, and keeps the
  snapshot workspace;
- a missing state file for a workspace that records live sources is treated as
  unsafe and rebuilt, because the removed copies cannot be identified;
- a watcher rescan that discovers a deleted live entry which used to shadow a
  snapshot path fails before mutating the workspace, so the next prepare performs
  a full rebuild instead of leaving a hole;
- the fast path also verifies the configured workspace root and `IsPrepared`, and
  runs `WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility` before it
  reports success;
- snapshot changes, strategy changes, a different snapshot order, or a hole risk
  all fall back to the existing full-rebuild path.

This is a live-specific path, not a change to `WorkspaceReconciler`. The original
reconciler still handles non-live workspace reuse and snapshot-only content.

## 7. Write protection

### 7.1 Threat model

A symlink or junction makes the workspace entry and the source entry the same
file. A game or tool that opens a workspace path for writing writes the source.
A hardlink has the same problem (shared inode).

There is no user-mode hook that can intercept the write before it lands. The
design therefore prevents the common cases and makes the residual risk explicit.

### 7.2 Copy-on-write for game-writable files

Implemented in P0 with `LiveContentConstants` and `ILiveWorkspaceStateStore`.
Matching paths are copied from the source into the workspace instead of linked:

- `*.ini`, `*.cfg`, `*.txt`, `*.xml`, `*.json`, `*.log`, `*.rep`, `*.sav`;
- everything the manifest marks as user data is already outside the workspace.

`LiveWorkspaceLinker.EnsureCopy` uses a timestamp heuristic:

- source newer than the workspace copy: the developer edited it, refresh the copy;
- workspace copy newer than the source: the game wrote to it, keep the game write;
- equal: no-op.

On the initial apply, `replaceSnapshotTargets` deletes a snapshot entry that
occupies the same path before the copy is made, because live content always wins
conflict resolution. On watcher rescans the flag is false, so a newer workspace
copy is preserved as a game write. The state store records which entries are
copies; when a source file disappears, the next rescan deletes its workspace
copy. `WorkspaceManager.CleanupWorkspaceAsync` also deletes the state file.

P0 does not check whether a game process is running before refreshing a copy. The
timestamp rule keeps a game write unless the developer edits the source afterwards,
which is the desired precedence. P1 can add process awareness for stricter
protection.

Linked assets (`.big`, `.w3d`, textures, audio) still expose the source to a write
through the link; that is the documented residual risk of live mode.

### 7.3 Optional post-session audit (P2)

For linked files below a size threshold that match a config extension set, take
a shadow copy before launch, compare after the game exits, and offer restore
when the source changed. This covers write paths the pattern list misses without
copying gigabytes of assets.

### 7.4 Explicit modes

- `ProtectGameWritableFiles = true` (default): safest, some config edits are
  reflected with a watcher delay rather than instantly.
- `PureLink`: everything linked, maximum live behaviour, documented pollution
  risk, explicit confirmation in the import dialog.
- `AutoJunction`: junctioned subtrees are read-only by policy; the user is warned
  that a junction exposes the whole subtree to writes.

## 8. Remove silent fallbacks (out of scope for this fork)

> This section is analysis only. The fork does not change
> `SymlinkOnlyStrategy`, `HybridCopySymlinkStrategy` or `ProfileLauncherFacade`
> behavior because that alters existing player-facing semantics. The upstream
> author should decide whether to change those fallbacks. The live overlay itself
> never falls back to a hardlink or copy.

### 8.1 Materializer and strategy level

- `LiveWorkspaceMaterializer` never falls back to hardlink or copy.
- `SymlinkOnlyStrategy.ProcessLocalFileAsync` currently catches
  `UnauthorizedAccessException` and creates a hardlink, then copies
  (`SymlinkOnlyStrategy.cs` around lines 204-218). Remove the fallback and let the
  error surface.
- `HybridCopySymlinkStrategy.ProcessLocalFileAsync` has the same fallback
  (`HybridCopySymlinkStrategy.cs` around lines 298-309). Remove it; the hybrid
  classification already copies essential files by design, so a failed symlink
  for a non-essential file is a real failure.
- `SymlinkOnlyStrategy.RequiresAdminRights` and
  `HybridCopySymlinkStrategy.RequiresAdminRights` return `true` on Windows. That
  rejects Developer Mode machines where symlinks work. Set them to `false` and
  rely on `ISymlinkCapabilityProvider` plus the real creation attempt.

### 8.2 Launcher level

`ProfileLauncherFacade` currently rewrites the profile strategy to `HardLink`
when the capability probe fails (`AdjustWorkspaceStrategyAsync`, around lines
1186-1234; `ResolveSupportedWorkspaceStrategy`, around lines 2144-2150).

Change to:

- for legacy `SymlinkOnly` / `HybridCopySymlink`, return a failure that names the
  strategy and the remediation; the UI offers an explicit "switch to HardLink"
  action. Do not mutate the profile silently.
- keep the capability check before launch so the user sees the message before
  the workspace is touched. The live overlay does not depend on the profile
  strategy, so it is unaffected.

### 8.3 File operations

`FileOperationsService.CreateSymlinkAsync` keeps its `allowFallback` parameter for
existing callers, but the live linker and the symlink strategies pass `false`.
Add a debug log of the actual link type created, and a unit test that a denied
symlink never produces a copy.

## 9. Live sync service

### 9.1 Interface

`GenHub/GenHub.Core/Interfaces/Workspace/ILiveWorkspaceSyncService.cs`

```csharp
public interface ILiveWorkspaceSyncService
{
    OperationResult Attach(WorkspaceInfo workspace, WorkspaceConfiguration configuration);
    void Detach(string workspaceId);
    bool IsAttached(string workspaceId);
    Task<OperationResult<bool>> SyncNowAsync(string workspaceId, CancellationToken cancellationToken = default);
}
```

### 9.2 Lifetime

Registered as a singleton. It must not capture scoped services
(`IFileOperationsService` is scoped), so it resolves a scope per operation
through `IServiceScopeFactory`. `WorkspaceManager` (scoped) injects the singleton
and attaches after a successful prepare.

### 9.3 Session

`LiveWorkspaceSession` holds:

- workspace id and path,
- bindings (manifest id, root, link kind),
- one `FileSystemWatcher` per root (`IncludeSubdirectories = true`,
  `InternalBufferSize = 64 KiB`, `NotifyFilter = FileName | DirectoryName |
  LastWrite | Size`),
- a bounded `Channel<LiveChange>` and one background consumer per session,
- a `SemaphoreSlim` serializing changes.

### 9.4 Event rules

On `Created` / `Changed` (source):

- ignored path: drop;
- `InstallTarget != Workspace`: drop (maps and replays are user data);
- live entry at a snapshot path: keep it; live content always wins;
- protected file: refresh the workspace copy if the game is not running;
- otherwise: ensure the workspace link exists.

On `Deleted` (source):

- protected copy: delete the workspace copy and its state entry;
- symlink whose target is the deleted source: delete the link;
- directory: delete state entries under the prefix.

On `Renamed`: old path deletion plus new path creation.

On watcher error (buffer overflow): enqueue a full `SyncNowAsync`, which rescans
the root and reconciles both directions.

Events are debounced (250 ms) and processed serially per workspace. A failed
event is logged once and repaired by the next rescan; it never crashes the UI.

### 9.5 What the watcher does not do

- It does not follow reparse points inside the source root (loop and double-link
  protection).
- It does not delete a workspace file that GenHub did not create.
- It does not modify the source root, except the P2 restore path, which is
  guarded and user-confirmed.

## 10. Validation and reuse

### 10.1 `WorkspaceValidator`

- `ValidateWorkspaceAsync` file-count check must not follow junctions. Add a
  no-reparse-point enumerator and use it for the count.
- `ValidateSymlinksAsync` already checks link targets; treat a link into a live
  root as expected and skip entries under a junctioned root (the junction itself
  is valid).
- `EnsureEntryPointExecutableAsync`: skip a workspace entry whose final target is
  inside a live root, so a live executable is never chmod-ed through the link.

### 10.2 `WorkspaceManager`

- `ValidateWorkspaceBasics` uses the same no-follow enumerator.
- `TryReuseExistingWorkspaceAsync` remains the non-live path. Live configurations
  first try `TryPrepareLiveIncrementalAsync`; if it cannot reconcile safely, the
  normal strategy path rebuilds the workspace.
- `ValidateAndFinalizeWorkspaceAsync` receives the full configuration, so
  `ManifestIds` covers snapshot and live manifests and a later content change is
  detected.
- `CleanupWorkspaceAsync` detaches the sync session before deleting.

### 10.3 `WorkspaceReconciler`

Live configurations bypass the reuse path in P0, but `AnalyzeCleanupAsync` can
still be called by the UI. Add a guard: when either the current workspace or the
new configuration has live sources, return an empty cleanup confirmation.
P1 makes `AnalyzeWorkspaceDeltaAsync` live-aware: expected live entries come
from a source scan, a live symlink is current when its target matches, and a
workspace link under a live root is never treated as an orphan.

## 11. UI and localization

### 11.1 The per-package switch

The toggle belongs to the imported package, not to the profile. It lives in
`GenHub/GenHub/Features/GameProfiles/Views/AddLocalContentView.axaml`, in the
left form column, after the Source row (line 170) and before the status banner
(line 173):

```text
Content Name
Content Type
Target Game
Source            [folder] [file]
--------------------------------
[x] Developer mode: link this folder live
    The folder is linked, not copied. ...
--------------------------------
Status
```

It is a `CheckBox` bound to `IsLiveSource`, plus a warning Border bound to
`IsLiveSource` and localized text. This dialog is opened once per import from
`GameProfileSettingsViewModel.Commands`, so the setting is naturally per mod
package.

Behaviour:

- `IsLiveSource` can only be enabled for a folder source. `BrowseFileCommand` is
  disabled in live mode and dragging an archive shows the localized reason.
- when enabled, the preview tree reads `SourcePath` directly instead of
  `_stagingPath`; the Add button reads "Register Live Content".
- `LoadFromManifestAsync` detects `item.Manifest.SourceMode == Live` and skips
  `RetrieveContentAsync` (there is no CAS copy), loading the folder instead.
- Editing an existing package reopens the dialog with the toggle in its current
  state. P0 keeps the mode fixed; changing it is P2.

### 11.2 Content cards

`GenHub/GenHub/Features/GameProfiles/Views/GameProfileContentEditorView.axaml`
already renders one card per package in both the Enabled and Available lists
(lines 225-272 and 338+). Add a `LIVE` badge next to Publisher, ContentType and
Version:

- `Features/GameProfiles/ViewModels/ContentDisplayItem.cs` gains
  `public bool IsLive => Manifest?.SourceMode == ContentSourceMode.Live;`
  (raise `OnPropertyChanged` when `Manifest` changes);
- the two card templates add a Border with `IsVisible="{Binding IsLive}"`.

No changes to `GameProfileSettingsViewModel.AvailableWorkspaceStrategies` and no
new strategy label: the profile keeps its HardLink / FullCopy / SymlinkOnly /
Hybrid choice for CAS content.

### 11.3 Profile status

A profile that contains live packages can show a small "Live files" marker
instead of a workspace-strategy label. `WorkspaceInfo.IsLive` is persisted, so
`GameProfileItemViewModel.UpdateWorkspaceStatus` can render it.

### 11.4 Notifications and localization

Notifications go through `INotificationService` with `NotificationDurations`
constants. Add keys to `Strings.resx` and `Strings.zh-Hans.resx` (this checkout
ships en + zh-Hans; keep parity with whatever satellites exist when the change is
prepared): toggle label, warning body, archive rejection, missing source root,
shadowed file count, privilege failure, watcher overflow, re-sync success.

## 12. File-by-file change list

Core (`GenHub/GenHub.Core`):

| File | Change |
| --- | --- |
| `Models/Enums/ContentSourceMode.cs` | new enum |
| `Models/Enums/LiveLinkKind.cs` | new enum |
| `Models/Manifest/ContentManifest.cs` | `SourceMode`, `LiveLinkKind`; copy ctor |
| `Models/Content/LocalContentOptions.cs` | live options |
| `Models/Workspace/WorkspaceConfiguration.cs` | `HasLiveSources` |
| `Models/Workspace/WorkspaceInfo.cs` | `IsLive`, `LiveSources` |
| `Models/Workspace/LiveSourceBinding.cs` | new |
| `Constants/LiveContentConstants.cs` | new |
| `Interfaces/Content/IContentStorageService.cs` | `RegisterExternalContentAsync` |
| `Interfaces/Content/ILocalContentService.cs` | `RegisterLiveSourceAsync` |
| `Interfaces/Workspace/ILiveWorkspaceSyncService.cs` | new |
| `Interfaces/Workspace/ILiveWorkspaceLinker.cs` | new: link primitive contract |
| `Interfaces/Workspace/ILiveWorkspaceStateStore.cs` | new: persisted ownership state |
| `Models/Workspace/LiveWorkspaceState.cs` | new: workspace state model |
| `Models/Workspace/LiveWorkspaceEntry.cs` | new: one owned entry |
| `Constants/LiveContentConstants.cs` | new: protected extension policy |
| `Utilities/SymbolicLinkCreator.cs` | new: unprivileged symlink creation on Windows |
| `Interfaces/Workspace/IFileOperationsService.cs` | `CreateDirectoryJunctionAsync` |

Application (`GenHub/GenHub`):

| File | Change |
| --- | --- |
| `Features/Content/Services/ContentStorageService.cs` | public metadata-only registration |
| `Core/Services/Content/LocalContentService.cs` | live registration branch and validation |
| `Features/Manifest/ManifestGenerationService.cs` | `CreateLiveContentManifestAsync` |
| `Features/Manifest/ContentManifestBuilder.cs` | no-hash, relative-source scan overload |
| `Features/Workspace/Live/LiveWorkspaceMaterializer.cs` | new: overlay live manifests on a prepared workspace |
| `Features/Workspace/Live/LiveSourceFile.cs` | new: scanned file record |
| `Features/Workspace/Live/LiveSourceScanner.cs` | new: enumerate, ignore, reparse guard |
| `Features/Workspace/Live/LiveWorkspaceLinker.cs` | new: link/copy/junction, conflict-safe |
| `Features/Workspace/Live/LiveWorkspaceSyncService.cs` | new: watcher sessions |
| `Features/Workspace/Live/LiveWorkspaceStateStore.cs` | new: app-data ownership state |
| `Features/Workspace/WorkspaceManager.cs` | split snapshot/live, overlay, bypass reuse, attach/detach, cleanup |
| `Features/Workspace/WorkspaceValidator.cs` | no-follow count, live skips |
| `Features/Workspace/WorkspaceReconciler.cs` | live cleanup guard |
| `Features/Workspace/FileOperationsService.cs` | junction, fallback policy, link logging |
| `Features/Workspace/Strategies/SymlinkOnlyStrategy.cs` | remove hardlink/copy fallback |
| `Features/Workspace/Strategies/HybridCopySymlinkStrategy.cs` | remove hardlink/copy fallback |
| `Features/Workspace/Strategies/WorkspaceCompatibilityHelper.cs` | move junction primitive |
| `Features/GameProfiles/Services/ProfileLauncherFacade.cs` | no silent downgrade for legacy strategies (section 8) |
| `Features/GameProfiles/Services/ProfileSharingService.cs` | refuse live export |
| `Features/GameProfiles/ViewModels/AddLocalContentViewModel.cs` | live toggle, no staging copy, live edit load |
| `Features/GameProfiles/Views/AddLocalContentView.axaml` | toggle and warning |
| `Features/GameProfiles/ViewModels/ContentDisplayItem.cs` | `IsLive` badge flag |
| `Features/GameProfiles/Views/GameProfileContentEditorView.axaml` | LIVE badge in both card lists |
| `Features/GameProfiles/ViewModels/GameProfileItemViewModel.cs` | "Live files" profile marker |
| `Infrastructure/DependencyInjection/WorkspaceModule.cs` | register materializer, linker, sync service |
| `GenHub.Windows/Features/Workspace/WindowsSymlinkCapabilityProvider.cs` | use the unprivileged probe |
| `Resources/Localization/Strings.resx` | new keys |
| `Resources/Localization/Strings.zh-Hans.resx` | new keys |

Tests (`GenHub/GenHub.Tests`): see section 13.

## 13. Tests

New test classes (names follow the existing suite):

- `LiveWorkspaceMaterializerTests`
  - links each declared live file to the source;
  - discovered new file is linked;
  - deleted source file removes the workspace link;
  - a live file overrides a snapshot path;
  - denied symlink produces a failure, never a copy or hardlink;
  - cleanup leaves the source tree unchanged (bytes, count, mtimes).
- `LiveWorkspaceSeparationTests`
  - `WorkspaceManager.SplitManifests` separates live from snapshot;
  - removing one live manifest deletes only that manifest's links and copies.
- `LiveMixedWorkspaceTests`
  - a HardLink profile with one live package keeps hardlinks for the base game
    and symlinks for the live package;
  - a FullCopy profile keeps copies for CAS content and links for live content;
  - the snapshot strategy never receives a live manifest.
- `LiveWorkspaceSyncServiceTests`
  - add / change / delete / rename events;
  - debounce and serialization;
  - watcher overflow triggers a full resync;
  - detach disposes watchers;
  - ignored and reparse-point entries are skipped.
- `LiveWorkspaceLinkerTests`
  - protected paths become copies;
  - source edit refreshes the copy;
  - game-created workspace file is not deleted;
  - foreign link target is not treated as owned.
- `LocalContentServiceLiveTests`
  - `StoreContentAsync` is never called;
  - manifest has `SourceMode.Live`, `LocalFile` entries, empty hashes;
  - `source.path` contains the root;
  - invalid roots are rejected.
- `ContentStorageServiceMetadataOnlyTests`
  - `RegisterExternalContentAsync` writes manifest and mapping only.
- `WorkspaceManagerLiveTests`
  - snapshot manifests go to the strategy, live manifests do not;
  - the overlay runs after the strategy and before validation;
  - reuse is bypassed;
  - attach happens after prepare and detach on cleanup;
  - live refs are not tracked.
- `WorkspaceValidatorLiveTests`
  - junctioned workspace file count does not follow the junction;
  - symlink validation passes for live links.
- `LiveContentCompatibilityTests`
  - old manifest without `sourceMode` deserializes as `Snapshot`;
  - workspace metadata with `IsLive` and `LiveSources` round-trips.

Platform facts: reuse `SymlinkFactAttribute` and
`SymlinkProcessExecutionFactAttribute`; junction tests are Windows-only and use
temp directories. Tests must not require administrator (inject a fake
`IFileOperationsService` for the denial cases).

Manual QA matrix:

| Case | Expected |
| --- | --- |
| Windows, Developer Mode on | live links created, no fallback |
| Windows, no privilege | clear error naming Dev Mode / junction option |
| Windows, `AutoJunction` | exclusive read-only dirs junctioned, others linked |
| Source file edited while app runs | workspace entry sees the new bytes |
| Source file added | link appears within the debounce window |
| Source file deleted | workspace link disappears, no ghost |
| Addon unchecked | its links removed on workspace refresh |
| Game writes `Options.ini` | source unchanged (copy-on-write) |
| Source folder moved/deleted | launch blocked with a clear message |
| Workspace cleanup | source tree byte-identical |
| Profile export with live content | refused |

## 14. Performance

- Registration scans the folder without hashing: one directory walk, no I/O on
  file contents.
- Prepare links in parallel with the same degree-of-parallelism heuristic as
  `SymlinkOnlyStrategy` (HDD 4, SSD CPU count).
- The watcher watches one root per live manifest, not one watcher per file.
- Rescans are incremental: the linker checks the existing target before creating
  anything.
- Junctioned subtrees are not enumerated by the watcher.

## 15. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Workspace cleanup deletes source | never follow reparse points; explicit tests; state store only owns links/copies |
| Game writes through a link | copy-on-write for writable patterns; `PureLink` requires explicit opt-in; P2 audit |
| Watcher misses events | full rescan on every prepare, on overflow, and on "Re-sync now" |
| Source path changes | fail at prepare with a localized message; P2 relocate action |
| Path length > 260 on Windows | use long-path-aware APIs where possible; document; P2 |
| OneDrive / antivirus reparse points | reject reparse-point roots; skip reparse entries while scanning |
| Live overrides snapshot content | logged and counted; remove the live package to restore the snapshot file |
| Live manifest exported | `ProfileSharingService` refuses |
| Sonar duplication | share link/copy logic through `LiveWorkspaceLinker`; do not copy `SymlinkOnlyStrategy` wholesale |

## 16. Phases

P0 (minimum usable):

1. manifest `SourceMode`, enums, storage registration;
2. live import path (UI toggle, no staging);
3. `WorkspaceManager` snapshot/live split;
4. `LiveWorkspaceMaterializer` per-file links with hard failure;
5. watcher add/delete/rename;
6. bypass reuse, attach/detach, cleanup safety;
7. no silent downgrade for the live overlay;
8. tests from section 13 that cover P0.

P1 (polish): live-aware reuse for snapshot changes, source health UI,
localization completion, process-aware copy refresh.

P2 (upstream decision, not in this fork): `AutoJunction`, post-session audit,
source relocate, live item editing, conflict report UI.

P3 (optional): per-source ignore rules and per-package link kind override.

## 17. Open questions

1. Should the live toggle be editable after import in P0, or is "re-import to
   change the mode" acceptable for the first cut? The proposal keeps it
   read-only in P0 and changes it in P2.
2. Deferred to upstream: whether a directory-junction or hardlink fallback is
   wanted for Windows sessions without symlink permission, and whether the legacy
   `SymlinkOnly`/`HybridCopySymlink` fallbacks should change. This fork does not
   modify those paths.
3. Should the protected extension list be user-configurable, or is the built-in
   list (`.ini`, `.cfg`, `.txt`, `.xml`, `.json`, `.log`, `.rep`, `.sav`) enough?
4. Should copy refresh check for a running game process in P1, or is the
   timestamp precedence (source edit wins, otherwise keep the game write) enough?
5. Resolved: profiles that contain live content are refused for URI, file and JSON
   export with a localized message instead of being marked unshareable.

## 18. P0 implementation notes

Implemented in this checkout:

- `ContentSourceMode` and `ContentManifest.SourceMode`; older manifests default
  to `Snapshot`.
- Live registration: `LocalContentService.RegisterLiveSourceAsync`,
  `ManifestGenerationService.CreateLiveContentManifestAsync` and
  `ContentStorageService.RegisterExternalContentAsync` (metadata only, no hash,
  no CAS copy).
- `WorkspaceManager` splits snapshot and live manifests, runs the profile
  strategy over the snapshot set, then overlays live content through
  `LiveWorkspaceMaterializer`.
- `LiveSourceScanner`, `LiveWorkspaceLinker`, `LiveWorkspaceMaterializer`,
  `LiveWorkspaceSyncService` (debounced watcher, full rescan as truth).
- Import dialog toggle, `LIVE` badge, and en/zh-Hans resource keys.
- Copy-on-write: `LiveContentConstants` marks `.ini`/`.cfg`/`.txt`/`.xml`/
  `.json`/`.log`/`.rep`/`.sav` as protected; `LiveWorkspaceLinker.EnsureCopy`
  copies them and `ILiveWorkspaceStateStore` records the owned copies.
- `SymbolicLinkCreator` passes
  `SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE` so Developer Mode works without
  an elevated process; `WindowsSymlinkCapabilityProvider` now uses that probe.
- `LocalContentService.RegisterLiveSourceAsync` refuses to register a live
  folder when the session cannot create symlinks, with a message naming the
  remediation.
- Non-ASCII display names are preserved while the manifest ID uses a
  deterministic ASCII identity segment (sanitized name, `content-<hash8>`, or
  `<sanitized>-<hash8>`).
- Removal cleanup:
  - the state store owns links and copies; removing one live manifest deletes only
    that manifest's entries, even when other live manifests remain;
  - removing the last live manifest clears only the live overlay and keeps the
    snapshot workspace when no snapshot path would be left empty;
  - a workspace recorded as `IsLive` is force-recreated only when the removed
    entries occupied snapshot paths or the ownership state is missing;
  - live state is removed only after workspace deletion is verified.
- Live-only incremental preparation:
  - `WorkspaceManager.TryPrepareLiveIncrementalAsync` skips the snapshot strategy
    when the workspace is live, the strategy matches, and snapshot IDs/order/
    versions are unchanged;
  - `CanApplyIncrementallyAsync` / `CanClearIncrementallyAsync` detect when a
    removed live entry would leave a snapshot path empty and force a full rebuild;
  - `ProfileEditorFacade` no longer forces `ForceRecreate`, and `GameProfileManager`
    no longer deletes the workspace on live removal.
- Sharing guard: `ProfileSharingService.BuildPackageFromProfileIdAsync` rejects any
  live manifest before packaging, so URI, file and JSON export all fail with
  `GameProfiles.Share.Error.LiveContent`.
- Localization: live import and live linking errors use the
  `Profiles.AddLocalContent.Error.*` and `Workspace.Live.Error.*` keys in
  `Strings.resx` and `Strings.zh-Hans.resx`; missing keys fall back to English.
- Separation guard: `WorkspaceManager.SplitManifests` is the single split point;
  the manager throws if a live manifest reaches a snapshot strategy and logs
  `Strategy ... will materialize N snapshot manifest(s); M live manifest(s) will
  be overlaid`.
- Live precedence: a live file always replaces a snapshot file at the same
  relative path. The initial assembly passes `replaceSnapshotTargets: true` so a
  protected copy also replaces the snapshot entry; watcher rescans pass `false`
  so a newer workspace copy is kept as a game write. The materializer logs
  `N live file(s) override snapshot paths`.

Troubleshooting: a profile whose `workspaceStrategy` is `SymlinkOnly` symlinks
all content, including the base game and snapshot mods. That is the strategy, not
the live overlay. Set the profile to `HardLink` and let the workspace rebuild to
compare. A live workspace additionally records `IsLive: true` and a non-empty
`LiveSources` list in `workspaces.json`.

Verification performed:

- `dotnet build GenHub.Windows.csproj -c Debug`: 0 warnings, 0 errors.
- The application launches and stays up with the new services registered.
- A standalone smoke harness (outside the repository) exercises
  `LiveWorkspaceMaterializer` and `LiveWorkspaceSyncService` with a fake linker:
  initial mount, new source file, deleted source file, watcher add, and cleanup
  leaving the source tree intact all pass.
- A second smoke harness verifies copy-on-write: a `.ini` becomes a private copy,
  a source edit refreshes it, a simulated game write is not overwritten, deleting
  the source removes the copy, and `.tga` stays a link.
- A third smoke harness verifies separation and removal: the materializer never
  processes a snapshot manifest, and removing one of two live manifests deletes
  only that manifest's link and copy while the other remains.
- A fourth smoke harness drives `WorkspaceManager.PrepareWorkspaceAsync` with no
  snapshot strategy registered and an existing live workspace. Removing live A
  keeps snapshot S and live B, removes only A, and reattaches the watcher.
- The same harness verifies the hole check: when live C had replaced snapshot
  `Data/S.ini`, removing C returns `CanApplyIncrementallyAsync == false`, so the
  manager falls back to a full rebuild.
- A fifth smoke harness drives `ProfileSharingService.ExportProfileToJsonAsync`:
  a profile with a live manifest fails with the localized live-content message,
  while a pure snapshot profile exports successfully. It also verifies the
  `GameProfiles.Share.Error.LiveContent` key exists in both resource files and
  that a localized live import error is returned when `ILocalizationService` is
  available.
- A sixth smoke harness deletes a live source file that had replaced a snapshot
  path, calls `ResyncAsync`, and verifies the call fails before mutating the
  workspace so the next prepare rebuilds it.

Not verified here:

- The test projects cannot be restored in this environment: NuGet is
  unreachable and xunit/Moq are not in the global package cache, so
  `GenHub.Tests.Core` was not compiled or run. The new code compiles in the
  application projects.
- Real symlink creation is blocked in the current session: Developer Mode is
  off and the account is not an administrator. The capability probe returns
  `false`, which is the expected loud failure. This fork intentionally does not
  add a directory-junction or hardlink fallback; the upstream author can decide.

Still open for the next slice:

- Process-aware copy refresh (P0 uses timestamp precedence).
- Profile sharing guard and per-package mode editing.

## 19. Scope boundaries for this fork

In scope:

- live import without CAS, per-package toggle and badge;
- snapshot/live split and live overlay;
- live-over-snapshot path precedence;
- watcher-based add/delete/rename;
- copy-on-write for protected config extensions;
- live-only incremental preparation with a full-rebuild fallback when a removed
  entry occupied a snapshot path;
- refusal to export or share profiles that contain live content;
- localized live import and linking errors;
- removal cleanup and state ownership;
- minimal Windows symlink capability fix.

Out of scope (left to the upstream author):

- directory-junction or hardlink fallback for sessions without symlink
  permission;
- changing the legacy `SymlinkOnly` / `HybridCopySymlink` silent fallbacks;
- UI drag-and-drop ordering of enabled content;
- renaming live files to imitate the `.big` `!` load-order convention.

The fork keeps existing strategy, profile-composition and launcher behavior
untouched unless a live workspace specifically requires a hook.
