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

### Manifest

Content manifest describing files and metadata.

```csharp
public class Manifest
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public List<ManifestDependency> Dependencies { get; set; }
    public List<ManifestFile> Files { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
```

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

#### UserDataSwitchInfo

Analysis results for user data impact when switching profiles.

```csharp
public class UserDataSwitchInfo
{
    public string OldProfileId { get; set; }
    public string NewProfileId { get; set; }
    public int FileCount { get; set; }
    public long TotalSizeBytes { get; set; }
}
```

**Purpose**: Provides information to the UI about what user data would be affected by a profile switch, enabling informed user decisions.

#### UserDataManifest

Tracks installed user data files for a specific profile.

```csharp
public class UserDataManifest
{
    public string ManifestId { get; set; }
    public string ProfileId { get; set; }
    public List<InstalledFile> InstalledFiles { get; set; }
    public bool IsActive { get; set; }
    public DateTime InstalledAt { get; set; }
}
```

**Purpose**: Maintains the relationship between content manifests and the files they install, enabling activation/deactivation and cleanup operations.

#### InstalledFile

Represents a single user data file installed by a manifest.

```csharp
public class InstalledFile
{
    public string SourcePath { get; set; }
    public string TargetPath { get; set; }
    public string Hash { get; set; }
    public long SizeBytes { get; set; }
    public bool IsHardLink { get; set; }
}
```

**Purpose**: Tracks individual file installations, supporting verification, cleanup, and efficient storage via hard links.

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
