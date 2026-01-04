---
title: Result Pattern
description: Documentation for the Result pattern used in GenHub
---

# Result Pattern Documentation

GenHub uses a consistent Result pattern for handling operations that may succeed or fail. This pattern provides a standardized way to return data and error information from methods.

## Overview

The Result pattern in GenHub consists of several key components:

- `ResultBase`: The base class for all result types
- `OperationResult<T>`: Generic result for operations that return data
- Specific result types for different domains

## ResultBase

`ResultBase` is the foundation of the result pattern. It provides common properties for success/failure status, errors, and timing information.

### Properties

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
// Success with no errors
protected ResultBase(bool success, IEnumerable&lt;string&gt;? errors = null, TimeSpan elapsed = default)

// Success/failure with single error
protected ResultBase(bool success, string? error = null, TimeSpan elapsed = default)
```

## OperationResult&lt;T&gt;

`OperationResult&lt;T&gt;` extends `ResultBase` and adds support for returning data from operations.

### Properties

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

### UpdateCheckResult

Result of an update check operation.

**Properties:**
- `IsUpdateAvailable`: Whether an update is available
- `CurrentVersion`: Current application version
- `LatestVersion`: Latest available version
- `UpdateUrl`: URL for the update
- `ReleaseNotes`: Release notes
- `ReleaseTitle`: Release title
- `ErrorMessages`: List of error messages
- `Assets`: Release assets
- `HasErrors`: Whether there are errors

**Factory Methods:**
```csharp
UpdateCheckResult NoUpdateAvailable()
UpdateCheckResult UpdateAvailable(GitHubRelease release)
UpdateCheckResult Error(string errorMessage)
```

### DetectionResult&lt;T&gt;

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
- `ObjectsScanned`: Objects scanned
- `ObjectsReferenced`: Objects kept
- `PercentageFreed`: Percentage of storage freed

#### CasValidationResult

Result of CAS integrity validation.

**Properties:**
- `Issues`: Validation issues
- `IsValid`: Whether validation passed
- `ObjectsValidated`: Objects validated
- `ObjectsWithIssues`: Objects with issues

#### CasStats

Summary of CAS system state.

**Properties:**
- `TotalObjects`: Number of objects in CAS
- `TotalBytes`: Total disk space consumed
- `LastGcTimestamp`: When garbage collection was last run
- `IsGcPending`: Whether a cleanup is recommended

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
