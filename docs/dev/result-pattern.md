---
title: Result Pattern
description: Documentation for the Result pattern used in GenHub
---

GenHub uses a consistent Result pattern for handling operations that may succeed or fail. This pattern provides a standardized way to return data and error information from methods.

## Overview

The Result pattern in GenHub consists of several key components:

- `ResultBase`: The base class for all result types
- `OperationResult<T>`: Generic result for operations that return data
- Specific result types for different domains

## ResultBase

`ResultBase` is the foundation of the result pattern. It provides common properties for success/failure status, errors, and timing information.

### ResultBase Properties

- `Success`: Indicates if the operation was successful
- `Failed`: Indicates if the operation failed (opposite of Success)
- `HasErrors`: Indicates if there are any errors
- `Errors`: Read-only list of error messages
- `FirstError`: The first error message, or null if no errors
- `AllErrors`: All error messages joined into a single string
- `Elapsed`: Time taken for the operation
- `CompletedAt`: Timestamp when the operation completed

### ResultBase Constructors

```csharp
// Result with optional errors
protected ResultBase(bool success, IEnumerable<string>? errors = null, TimeSpan elapsed = default)

// Success/failure with single error
protected ResultBase(bool success, string? error = null, TimeSpan elapsed = default)
```

## `OperationResult<T>`

`OperationResult<T>` extends `ResultBase` and adds support for returning data from operations.

### OperationResult Properties

- `Data`: The data returned by the operation (nullable)
- `FirstError`: The first error message, or null if no errors

### OperationResult Factory Methods

```csharp
// Create successful result
OperationResult<T> CreateSuccess(T data, TimeSpan elapsed = default)

// Create failed result with single error
OperationResult<T> CreateFailure(string error, TimeSpan elapsed = default)

// Create failed result with multiple errors
OperationResult<T> CreateFailure(IEnumerable<string> errors, TimeSpan elapsed = default)

// Create failed result with single error and partial data
OperationResult<T> CreateFailure(string error, T data, TimeSpan elapsed)

// Create failed result with multiple errors and partial data
OperationResult<T> CreateFailure(IEnumerable<string> errors, T data, TimeSpan elapsed)

// Create failed result copying errors from another result
OperationResult<T> CreateFailure(ResultBase result, TimeSpan elapsed = default)
```

## Specific Result Types

GenHub includes several specialized result types for different domains:

### LaunchResult

Result of a game launch operation.

**Properties:**

- `ProcessId`: The launched process ID
- `Exception`: Exception that occurred during launch
- `StartTime`: When the launch started
- `LaunchDuration`: How long the launch took
- `FirstError`: First error message

**Factory Methods:**

```csharp
LaunchResult CreateSuccess(int processId, DateTime startTime, TimeSpan launchDuration)
LaunchResult CreateFailure(string errorMessage, Exception? exception = null)
```

### ValidationResult

Result of a validation operation.

**Properties:**

- `ValidatedTargetId`: ID of the validated target
- `Issues`: List of validation issues
- `IsValid`: Whether validation passed
- `CriticalIssueCount`: Number of critical issues
- `WarningIssueCount`: Number of warning issues
- `InfoIssueCount`: Number of informational issues

**Constructors:**

```csharp
// Standard constructor
public ValidationResult(string validatedTargetId, IEnumerable<ValidationIssue> issues)
```

**Factory Methods:**

```csharp
// Result with no issues
public static ValidationResult CreateSuccess(string validatedTargetId)

// Failure with issues
public static ValidationResult CreateFailure(string validatedTargetId, IEnumerable<ValidationIssue> issues)
```


### ContentUpdateCheckResult

Result of a content update check operation.

**Properties:**

- `IsUpdateAvailable`: Whether an update is available
- `CurrentVersion`: Current content version
- `LatestVersion`: Latest available version
- `DownloadUrl`: URL for the update package
- `Changelog`: Release notes or changelog content
- `HasErrors`: Inherited from `ResultBase`, indicates whether any errors are present
- `FirstError`: Inherited from `ResultBase`, provides the first error message, if any

**Factory Methods:**

- `ContentUpdateCheckResult.CreateUpdateAvailable(string latestVersion, ...)`: When an update for existing content is found.
- `ContentUpdateCheckResult.CreateNoUpdateAvailable(string currentVersion, ...)`: When the current version is up to date.
- `ContentUpdateCheckResult.CreateContentAvailable(string latestVersion, ...)`: **Semantic Difference**: Use this when search returns content that is *not currently installed* but available for first-time acquisition.
- `ContentUpdateCheckResult.CreateFailure(string error, ...)`: When the update check itself fails.

> [!TIP]
> Always check `result.Success` before accessing version properties, as they may be null in failure results.

```csharp
var result = await updateService.CheckForUpdatesAsync(manifest);
if (result.Success && result.IsUpdateAvailable)
{
    // Handle update
}
```

### `DetectionResult<T>`

Generic result for detection operations.

**Properties:**

- `Items`: Detected items

**Factory Methods:**

```csharp
DetectionResult<T> Succeeded(IEnumerable<T> items, TimeSpan elapsed)
DetectionResult<T> Failed(string error)
```

### DownloadResult

Result of a file download operation.

**Properties:**

- `FilePath`: Path to the downloaded file
- `BytesDownloaded`: Number of bytes downloaded
- `HashVerified`: Whether hash verification passed
- `AverageSpeedBytesPerSecond`: Download speed
- `FormattedBytesDownloaded`: Formatted bytes (e.g., "1.2 MB")
- `FormattedSpeed`: Formatted speed (e.g., "1.2 MB/s")
- `FirstError`: First error message

**Factory Methods:**

```csharp
DownloadResult CreateSuccess(string filePath, long bytesDownloaded, TimeSpan elapsed, bool hashVerified = false)
DownloadResult CreateFailure(string errorMessage, long bytesDownloaded = 0, TimeSpan elapsed = default)
```

### GitHubUrlParseResult

Result of parsing GitHub repository URLs.

**Properties:**

- `Owner`: Repository owner
- `Repo`: Repository name
- `Tag`: Release tag

**Factory Methods:**

```csharp
GitHubUrlParseResult CreateSuccess(string owner, string repo, string? tag)
GitHubUrlParseResult CreateFailure(params string[] errors)
```

### CAS Results

#### CasGarbageCollectionResult

Result of CAS garbage collection.

**Properties:**

- `ObjectsDeleted`: Number of objects deleted
- `BytesFreed`: Bytes freed
- `ObjectsScanned`: Total objects scanned
- `ObjectsReferenced`: Objects kept (referenced)
- `PercentageFreed`: Percentage of objects freed relative to scanned objects

**Factory Methods:**

- `CreateSuccess(int deleted, long bytes, int scanned, int referenced, TimeSpan elapsed)`
- `CreateFailure(string error, TimeSpan elapsed)` or `CreateFailure(IEnumerable<string> errors, TimeSpan elapsed)`

#### CasValidationResult

Result of CAS integrity validation.

**Properties:**

- `Issues`: Validation issues
- `IsValid`: Whether validation passed
- `ObjectsValidated`: Objects validated
- `ObjectsWithIssues`: Objects with issues

**Constructors:**

- `CasValidationResult()`: Creates a successful validation result with no issues.
- `CasValidationResult(issues, objectsValidated, elapsed)`: Creates a result with validation issues. Note that `Success` will be `false` only if critical issues are present.

#### CasStats

Summary of CAS system state.

**Properties:**

- `TotalObjects`: Number of objects in CAS
- `TotalBytes`: Total disk space consumed
- `LastGcTimestamp`: When garbage collection was last run
- `IsGcPending`: Whether a cleanup is recommended

**Factory Methods:**

- `Create(objectCount, totalSize, spaceSaved, hitRate, recentAccesses)`

## Usage Examples

### Basic Operation Result

```csharp
public OperationResult<User> GetUserById(int id)
{
    try
    {
        var user = _userRepository.GetById(id);
        if (user == null)
        {
            return OperationResult<User>.CreateFailure("User not found");
        }
        return OperationResult<User>.CreateSuccess(user);
    }
    catch (Exception ex)
    {
        return OperationResult<User>.CreateFailure($"Database error: {ex.Message}");
    }
}
```

### Validation Result

```csharp
public ValidationResult ValidateGameInstallation(string path)
{
    var issues = new List<ValidationIssue>();

    if (!Directory.Exists(path))
    {
        issues.Add(new ValidationIssue("Installation directory does not exist", ValidationSeverity.Error, path));
    }

    // More validation logic...

    return new ValidationResult(path, issues);
}
```

### Launch Result

```csharp
public async Task<LaunchResult> LaunchGame(GameProfile profile)
{
    try
    {
        var startTime = DateTime.UtcNow;
        var process = Process.Start(profile.ExecutablePath);

        if (process == null)
        {
            return LaunchResult.CreateFailure("Failed to start process");
        }

        var launchDuration = DateTime.UtcNow - startTime;
        return LaunchResult.CreateSuccess(process.Id, startTime, launchDuration);
    }
    catch (Exception ex)
    {
        return LaunchResult.CreateFailure("Launch failed", ex);
    }
}
```

### Content Update Check Result

```csharp
public async Task<ContentUpdateCheckResult> CheckForUpdatesAsync(ContentManifest manifest)
{
    try
    {
        var latestRelease = await _gitHubService.GetLatestReleaseAsync(manifest.Publisher.Id);

        if (latestRelease.Version == manifest.Version)
        {
            return ContentUpdateCheckResult.CreateNoUpdateAvailable(manifest.Version);
        }

        return ContentUpdateCheckResult.CreateUpdateAvailable(
            latestRelease.Version,
            manifest.Version,
            manifest.Publisher.Id,
            manifest.Publisher.Name,
            manifest.Id,
            manifest.Name,
            latestRelease.ReleaseDate,
            latestRelease.DownloadUrl,
            latestRelease.Changelog);
    }
    catch (Exception ex)
    {
        return ContentUpdateCheckResult.CreateFailure($"Update check failed: {ex.Message}");
    }
}
```

## Best Practices

1. **Always check Success/Failed**: Before accessing Data or other properties, check if the operation succeeded.

2. **Use appropriate result types**: Choose the most specific result type for your operation.

3. **Provide meaningful errors**: Include descriptive error messages that help users understand what went wrong.

4. **Include timing information**: Pass elapsed time when available for performance monitoring.

5. **Handle exceptions**: Convert exceptions to appropriate result failures.

6. **Test thoroughly**: Ensure all success and failure paths are tested.

## Testing

All result types include comprehensive unit tests covering:

- Constructor behavior
- Property access
- Factory method functionality
- Edge cases and error conditions

See the test files in `GenHub.Tests.Core` for examples.
