---
title: Models
description: Data models and domain objects used in GenHub
---

# Models

This document describes the core data models and domain objects used throughout the GenHub system.

## Result Types

### ResultBase

Base class for all result objects providing common success/failure semantics.

```csharp
public abstract class ResultBase
{
    public bool Success { get; }
    public bool Failed => !Success;
    public bool HasErrors => Errors.Count > 0;
    public IReadOnlyList<string> Errors { get; }
    public string? FirstError => Errors.FirstOrDefault();
    public string AllErrors => string.Join(Environment.NewLine, Errors);
    public TimeSpan Elapsed { get; }
    public DateTime CompletedAt { get; }
}
```

### OperationResult&lt;T&gt;

Generic result for operations that return data.

```csharp
public class OperationResult<T> : ResultBase
{
    public T? Data { get; }
    public string? FirstError => Errors.FirstOrDefault();
}
```

### ValidationResult

Result of validation operations.

```csharp
public class ValidationResult : ResultBase
{
    public string ValidatedTargetId { get; }
    public IReadOnlyList<ValidationIssue> Issues { get; }
    public bool IsValid => Success;
    public int CriticalIssueCount { get; }
    public int WarningIssueCount { get; }
    public int InfoIssueCount { get; }
}
```

### LaunchResult

Result of game launch operations.

```csharp
public class LaunchResult : ResultBase
{
    public int? ProcessId { get; }
    public Exception? Exception { get; }
    public DateTime StartTime { get; }
    public TimeSpan LaunchDuration => Elapsed;
}
```

## Domain Models

### GameProfile

Represents a game installation profile.

```csharp
public class GameProfile
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string ExecutablePath { get; set; }
    public string WorkspacePath { get; set; }
    public string BaseContentId { get; set; }
    public List<string> EnabledMods { get; set; }
    public Dictionary<string, string> LaunchArguments { get; set; }
    public string? ToolContentId { get; set; }
    public bool IsToolProfile => !string.IsNullOrWhiteSpace(ToolContentId);
}
```

### ContentManifest

Comprehensive manifest for content distribution in GenHub ecosystem.

```csharp
public class ContentManifest
{
    public string ManifestVersion { get; set; }
    public ManifestId Id { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public ContentType ContentType { get; set; }
    public GameType TargetGame { get; set; }
    public PublisherInfo Publisher { get; set; }
    public ContentMetadata Metadata { get; set; }
    public string? OriginalPublisherName { get; set; }
    public string? OriginalContentId { get; set; }
    public string? SourcePath { get; set; }
    public List<ContentDependency> Dependencies { get; set; }
    public List<ContentReference> ContentReferences { get; set; }
    public List<string> KnownAddons { get; set; }
    public List<ManifestFile> Files { get; set; }
    public List<string> RequiredDirectories { get; set; }
    public InstallationInstructions InstallationInstructions { get; set; }
}
```

**Purpose**: Central contract between content publishers and the GenHub launcher, describing all aspects of a content package including files, dependencies, metadata, and installation instructions.

### ValidationIssue

Represents a validation problem.

```csharp
public class ValidationIssue
{
    public string Message { get; }
    public ValidationSeverity Severity { get; }
    public string? Category { get; }
    public string? TargetPath { get; }
}
```

### User Data Models

Models for managing user-generated content across game profiles.

#### UserDataManifest

Tracks installed user data files for a specific profile.

```csharp
public class UserDataManifest
{
    public string ManifestId { get; set; }
    public string ProfileId { get; set; }
    public List<UserDataFileEntry> InstalledFiles { get; set; }
    public bool IsActive { get; set; }
    public DateTime InstalledAt { get; set; }
}
```

**Purpose**: Maintains the relationship between content manifests and the files they install, enabling activation/deactivation and cleanup operations.

#### UserDataFileEntry

Represents a single file that has been installed to the user's data directory.

```csharp
public class UserDataFileEntry
{
    public string RelativePath { get; set; }
    public string AbsolutePath { get; set; }
    public string SourceHash { get; set; }
    public long FileSize { get; set; }
    public ContentInstallTarget InstallTarget { get; set; }
    public bool WasOverwritten { get; set; }
    public string? BackupPath { get; set; }
    public DateTime InstalledAt { get; set; }
    public bool IsHardLink { get; set; }
    public string? CasHash { get; set; }
}
```

**Purpose**: Tracks individual file installations to user data directories, supporting verification, cleanup, conflict resolution, and efficient storage via hard links from CAS.

### WorkspaceDelta

Represents a delta operation for workspace reconciliation.

```csharp
public class WorkspaceDelta
{
    public WorkspaceDeltaOperation Operation { get; set; }
    public ManifestFile File { get; set; }
    public string WorkspacePath { get; set; }
    public string Reason { get; set; }
}
```

**Purpose**: Describes a single file operation (add, update, remove) needed to reconcile workspace state with desired manifest configuration.

### WorkspaceInfo

Information about a prepared workspace.

```csharp
public class WorkspaceInfo
{
    public string Id { get; set; }
    public string WorkspacePath { get; set; }
    public string GameClientId { get; set; }
    public WorkspaceStrategy Strategy { get; set; }
    public bool IsPrepared { get; set; }
    public List<ValidationIssue> ValidationIssues { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public long TotalSizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool IsValid { get; set; }
    public string ExecutablePath { get; set; }
    public string WorkingDirectory { get; set; }
    public List<string> ManifestIds { get; set; }
    public Dictionary<string, string> ManifestVersions { get; set; }
}
```

**Purpose**: Tracks workspace state including preparation status, validation results, and manifest versions for change detection.

### ContentSearchResult

Represents a single result from a content search operation.

```csharp
public class ContentSearchResult
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public object? Data { get; set; }
    public string Version { get; set; }
    public ContentType ContentType { get; set; }
    public bool IsInferred { get; set; }
    public GameType TargetGame { get; set; }
    public string PublisherName { get; set; }
    public string? AuthorName { get; set; }
    public string? IconUrl { get; set; }
    public string? BannerUrl { get; set; }
    public IList<string> ScreenshotUrls { get; }
    public IList<string> Tags { get; }
    public DateTime? LastUpdated { get; set; }
    public long DownloadSize { get; set; }
    public int DownloadCount { get; set; }
    public float Rating { get; set; }
    public IDictionary<string, string> Metadata { get; }
    public bool IsInstalled { get; set; }
    public bool HasUpdate { get; set; }
    public bool RequiresResolution { get; set; }
    public string? ResolverId { get; set; }
    public string? SourceUrl { get; set; }
    public IDictionary<string, string> ResolverMetadata { get; }
    public ParsedWebPage? ParsedPageData { get; set; }
}
```

**Purpose**: Provides rich metadata about discovered content from various publishers, supporting search, browsing, and content resolution workflows.

### ContentDiscoveryResult

Represents the result of a content discovery operation with pagination.

```csharp
public class ContentDiscoveryResult
{
    public IEnumerable<ContentSearchResult> Items { get; init; }
    public bool HasMoreItems { get; init; }
    public int? TotalItems { get; init; }
}
```

**Purpose**: Wraps search results with pagination metadata for efficient content browsing.

### ManifestFile

Represents a file entry in a content manifest.

```csharp
public class ManifestFile
{
    public string RelativePath { get; set; }
    public ContentSourceType SourceType { get; set; }
    public ContentInstallTarget InstallTarget { get; set; }
    public long Size { get; set; }
    public string Hash { get; set; }
    public FilePermissions Permissions { get; set; }
    public bool IsExecutable { get; set; }
    public string? DownloadUrl { get; set; }
    public bool IsRequired { get; set; }
    public string? SourcePath { get; set; }
    public string? PatchSourceFile { get; set; }
    public ExtractionConfiguration? PackageInfo { get; set; }
}
```

**Purpose**: Describes a single file in a content package, including its source, destination, verification hash, and installation requirements.

### ContentDependency

Enhanced dependency specification with advanced relationship management.

```csharp
public class ContentDependency
{
    public ManifestId Id { get; set; }
    public string Name { get; set; }
    public ContentType DependencyType { get; set; }
    public string? PublisherType { get; set; }
    public bool StrictPublisher { get; set; }
    public string? MinVersion { get; set; }
    public string? MaxVersion { get; set; }
    public string? ExactVersion { get; set; }
    public List<string> CompatibleVersions { get; set; }
    public List<GameType> CompatibleGameTypes { get; set; }
    public bool IsExclusive { get; set; }
    public List<ManifestId> ConflictsWith { get; set; }
    public DependencyInstallBehavior InstallBehavior { get; set; }
    public bool IsOptional { get; set; }
    public List<string> RequiredPublisherTypes { get; set; }
    public List<string> IncompatiblePublisherTypes { get; set; }
}
```

**Purpose**: Defines complex dependency relationships between content packages, supporting version constraints, publisher requirements, conflicts, and installation behaviors.

### WorkspaceCleanupConfirmation

Contains information about workspace cleanup operations requiring user confirmation.

```csharp
public class WorkspaceCleanupConfirmation
{
    public int FilesToRemove { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<string> AffectedManifests { get; set; }
    public List<WorkspaceDelta> RemovalDeltas { get; set; }
    public bool IsCleanupNeeded => FilesToRemove > 0;
}
```

**Purpose**: Provides information to the UI about workspace cleanup impact, enabling informed user decisions before removing files.

### WorkspaceConfiguration

Configuration for workspace preparation operations.

```csharp
public class WorkspaceConfiguration
{
    public string Id { get; set; }
    public List<ContentManifest> Manifests { get; set; }
    public GameClient GameClient { get; set; }
    public string WorkspaceRootPath { get; set; }
    public string BaseInstallationPath { get; set; }
    public Dictionary<string, string> ManifestSourcePaths { get; set; }
    public WorkspaceStrategy Strategy { get; set; }
    public bool ForceRecreate { get; set; }
    public bool ValidateAfterPreparation { get; set; }
    public List<WorkspaceDelta>? ReconciliationDeltas { get; set; }
    public bool SkipCleanup { get; set; }  // NEW: Preserve files when switching profiles
}
```

**New Property**: `SkipCleanup` - When `true`, files that exist in workspace but not in new manifests will be preserved. This is useful when switching profiles to avoid deleting large map packs.

### NetworkSettings

Represents network-related settings in Options.ini.

```csharp
public class NetworkSettings
{
    public string? GameSpyIPAddress { get; set; }  // NEW: IP for LAN/online play
    public Dictionary<string, string> AdditionalProperties { get; set; }
}
```

**New Property**: `GameSpyIPAddress` - IP address for GameSpy/networking services, used for LAN and online multiplayer. See [Game Settings](../features/game-settings/) for details.

### LaunchPhase

Represents the phases of a game launch operation.

```csharp
public enum LaunchPhase
{
    ValidatingProfile,
    ResolvingContent,
    AwaitingCleanupConfirmation,
    PreparingWorkspace,
    PreparingUserData,  // NEW: User data preparation via hard links from CAS
    Starting,
    Running,
    Completed,
    Failed
}
```

**New Phase**: `PreparingUserData` - Indicates the launcher is preparing user data content (maps, replays, etc.) via hard links from CAS.

### LaunchProgress

Represents the progress of a game launch operation.

```csharp
public class LaunchProgress
{
    public LaunchPhase Phase { get; set; }
    public int PercentComplete { get; set; }
    public WorkspaceCleanupConfirmation? CleanupConfirmation { get; set; }  // NEW
}
```

**New Property**: `CleanupConfirmation` - Workspace cleanup confirmation data when awaiting user decision.

## Configuration Models

### AppUpdateOptions

Configuration for the app update feature.

```csharp
public class AppUpdateOptions
{
    public bool AutoCheckForUpdates { get; set; } = true;
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(24);
    public string RepositoryOwner { get; set; } = "community-outpost";
    public string RepositoryName { get; set; } = "GenHub";
}
```

### CasOptions

Configuration for Content Addressable Storage.

```csharp
public class CasOptions
{
    public string StoragePath { get; set; } = "./cas";
    public bool EnableCompression { get; set; } = true;
    public long MaxFileSizeBytes { get; set; } = 100 * 1024 * 1024;
    public TimeSpan GcInterval { get; set; } = TimeSpan.FromHours(24);
}
```

## Enumerations

### CasPoolType

Identifies which CAS (Content-Addressable Storage) pool to use for content storage.

```csharp
public enum CasPoolType
{
    Primary,      // Maps, mods, user content (app data drive)
    Installation  // Game clients and installations (game drive)
}
```

**Purpose**: Enables multi-pool CAS architecture for cross-drive optimization and hard-link support. See [Storage & CAS](../features/storage.md) for details.

### TextureQuality

Represents texture quality levels for game settings.

```csharp
public enum TextureQuality
{
    Low = 0,
    Medium = 1,
    High = 2,
    VeryHigh = 3  // TheSuperHackers client only
}
```

**Note**: The `VeryHigh` option is only available when using the TheSuperHackers game client.

### ValidationSeverity

Severity levels for validation issues.

```csharp
public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
    Critical
}
```

### ProcessPriorityClass

Process priority levels for launched games.

```csharp
public enum ProcessPriorityClass
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
    RealTime
}
```

### ContentSourceType

Defines the source of content files in a manifest.

```csharp
public enum ContentSourceType
{
    Unknown = 0,           // Content source is unknown or undefined
    GameInstallation = 1,  // Content comes from the game installation
    ContentAddressable = 2, // Content is stored in CAS system
    LocalFile = 3,         // Content is a local file on the filesystem
    RemoteDownload = 4,    // Content needs to be downloaded from a remote URL
    ExtractedPackage = 5,  // Content is extracted from a package/archive file
    PatchFile = 6,         // Content is a patch file that modifies existing content
}
```

**Purpose**: Properly separates content origins from workspace placement strategies, enabling flexible content sourcing.

### ContentInstallTarget

Defines the target installation location for content.

```csharp
public enum ContentInstallTarget
{
    Workspace = 0,              // Install to game's workspace directory (default)
    UserDataDirectory = 1,      // Install to user's Documents folder for the game
    UserMapsDirectory = 2,      // Install to Maps subdirectory within user data
    UserReplaysDirectory = 3,   // Install to Replays subdirectory within user data
    UserScreenshotsDirectory = 4, // Install to Screenshots subdirectory within user data
    System = 5,                 // Install to system location (requires elevation)
}
```

**Purpose**: Different content types may need to be installed to different locations. Maps go to UserMapsDirectory, replays to UserReplaysDirectory, while mods and patches go to Workspace.

### PackageType

Defines the type of a content package.

```csharp
public enum PackageType : byte
{
    None,       // No package type specified / unknown
    Zip,        // A standard ZIP archive
    Tar,        // A tarball archive
    TarGz,      // A GZipped tarball archive
    SevenZip,   // A 7-Zip archive
    Installer,  // A self-contained installer executable
}
```

**Purpose**: Identifies archive format for extraction operations.

### GameType

Represents the type of Command and Conquer game.

```csharp
public enum GameType
{
    Generals,   // Command and Conquer: Generals
    ZeroHour,   // Command and Conquer: Generals – Zero Hour
    Unknown,    // Unknown game type
}
```

**Purpose**: Distinguishes between base game and expansion for content compatibility and user data paths.

### ContentType

Defines the type of content in a manifest.

```csharp
public enum ContentType
{
    // Foundation types
    GameInstallation,   // EA/Steam/Disk installation
    GameClient,         // Independent game executable

    // Content types
    Mod,                // Major gameplay changes
    Patch,              // Balance/configuration changes
    Addon,              // Utilities/tools
    MapPack,            // Map collections
    LanguagePack,       // Localization

    // Meta types
    ContentBundle,      // Collection of multiple contents
    PublisherReferral,  // Link to other publisher content
    ContentReferral,    // Link to specific content

    // Individual content
    Mission,            // Story-driven gameplay with objectives
    Map,                // Free-play or skirmish mode on a map
    Skin,               // UI customization skins
    Video,              // Video content (trailers, gameplay recordings)
    Replay,             // Game replay files
    Screensaver,        // Screensaver files
    Executable,         // Standalone executable file
    ModdingTool,        // Modding and mapping tools/utilities
    UnknownContentType, // Unknown content type
}
```

**Purpose**: Categorizes content for proper handling, installation, and user interface presentation.

### DependencyInstallBehavior

Defines how a dependency should be handled during installation.

```csharp
public enum DependencyInstallBehavior
{
    RequireExisting = 0, // Dependency must already exist, don't auto-install
    AutoInstall = 1,     // Install if missing
    Optional = 2,        // User can choose to install
    Suggest = 3,         // Recommend but don't require
}
```

**Purpose**: Controls automatic dependency resolution and installation workflows.

## Model Validation

All models include data validation attributes:

```csharp
public class GameProfile
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Id { get; set; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; }

    [Required]
    [FileExists]
    public string ExecutablePath { get; set; }
}
```

## Serialization

Models support JSON serialization with proper handling of:

- Nullable properties
- Complex object graphs
- Circular references
- Custom converters for special types

## Immutability

Many models are designed to be immutable:

```csharp
public class ValidationIssue
{
    public ValidationIssue(string message, ValidationSeverity severity, string? category = null, string? targetPath = null)
    {
        Message = message;
        Severity = severity;
        Category = category;
        TargetPath = targetPath;
    }

    public string Message { get; }
    public ValidationSeverity Severity { get; }
    public string? Category { get; }
    public string? TargetPath { get; }
}
```

This ensures thread safety and prevents accidental modification of model state.

---

## Universal Parser Models

Models used by the `IWebPageParser` system to extract rich content from provider websites.

### ParsedWebPage

The root container for all data extracted from a single web page.

```csharp
public record ParsedWebPage(
    string Url,
    GlobalContext Context,
    List<ContentSection> Sections,
    PageType PageType);
```

### GlobalContext

Standard metadata extracted from the page header or sidebar.

```csharp
public record GlobalContext(
    string Title,
    string Developer,
    DateTime? ReleaseDate,
    string? GameName = null,
    string? IconUrl = null,
    string? Description = null);
```

### Content Sections

All extracted content is categorized into sections that inherit from `ContentSection`.

| Model     | Description                                           |
| --------- | ----------------------------------------------------- |
| `Article` | News posts, articles, or blog entries                 |
| `File`    | Downloadable files with metadata (size, hash, etc.)   |
| `Video`   | Embedded videos from YouTube, Vimeo, etc.             |
| `Image`   | Gallery images or screenshots                         |
| `Review`  | User reviews with ratings and content                 |
| `Comment` | User discussion comments with karma/creator info      |

#### ContentSection (Base)

```csharp
public abstract record ContentSection(
    SectionType Type,
    string Title);
```

### Enums

#### PageType

Defines the structural role of the page.

- `List`: A gallery or listing of multiple items.
- `Summary`: A news feed or overview page.
- `Detail`: A deep-dive page for a specific mod or addon.
- `FileDetail`: A targeted page for a specific file download.

#### SectionType

Identifies the type of a `ContentSection` (Article, Video, Image, File, Review, Comment).
