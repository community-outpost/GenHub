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
