# Constants API Reference

This document provides comprehensive documentation for all constants used throughout the GenHub application. Constants are organized into logical groups for better maintainability and consistency.

## Overview

GenHub uses a centralized constants system to ensure consistency across the application. All constants are defined in static classes within the `GenHub.Core.Constants` namespace and follow StyleCop conventions.

## Constants Files

### ApiConstants

API and network related constants for GitHub integration and HTTP operations.

#### GitHub API Constants

- `GitHubApiBaseUrl`: Base URL for GitHub API (`"https://api.github.com"`)
- `GitHubRawBaseUrl`: Base URL for raw GitHub content (`"https://raw.githubusercontent.com"`)
- `GitHubRepoApiEndpoint`: Template for repository API endpoints (`"/repos/{owner}/{repo}"`)
- `GitHubReleasesApiEndpoint`: Template for releases API endpoints (`"/repos/{owner}/{repo}/releases"`)
- `GitHubLatestReleaseApiEndpoint`: Template for latest release endpoint (`"/repos/{owner}/{repo}/releases/latest"`)
- `GitHubReleaseAssetsApiEndpoint`: Template for release assets endpoint (`"/repos/{owner}/{repo}/releases/{releaseId}/assets"`)
- `GitHubContentsApiEndpoint`: Template for repository contents endpoint (`"/repos/{owner}/{repo}/contents/{path}"`)

#### HTTP Status Codes

- `HttpOk`: 200 OK
- `HttpCreated`: 201 Created
- `HttpNoContent`: 204 No Content
- `HttpBadRequest`: 400 Bad Request
- `HttpUnauthorized`: 401 Unauthorized
- `HttpForbidden`: 403 Forbidden
- `HttpNotFound`: 404 Not Found
- `HttpInternalServerError`: 500 Internal Server Error

#### Network Timeouts

- `DefaultHttpTimeoutSeconds`: Default timeout for HTTP requests (30 seconds)
- `LongHttpTimeoutSeconds`: Extended timeout for large downloads (300 seconds)
- `ShortHttpTimeoutSeconds`: Quick timeout for fast operations (10 seconds)

#### User Agents

- `DefaultUserAgent`: Default user agent string (`"GenHub/1.0"`)
- `GitHubApiUserAgent`: GitHub API specific user agent (`"GenHub-GitHub-API/1.0"`)

#### Rate Limiting

- `GitHubApiRateLimitAuthenticated`: Rate limit for authenticated requests (5000/hour)
- `GitHubApiRateLimitUnauthenticated`: Rate limit for unauthenticated requests (60/hour)
- `DefaultApiRequestDelayMs`: Default delay between API requests (1000ms)

#### Content Types

- `ContentTypeJson`: JSON content type (`"application/json"`)
- `ContentTypeOctetStream`: Binary data content type (`"application/octet-stream"`)
- `ContentTypeFormUrlEncoded`: Form data content type (`"application/x-www-form-urlencoded"`)

### CasDefaults

Default values and limits for Content-Addressable Storage (CAS) operations.

- `MaxCacheSizeBytes`: Maximum cache size (50GB)
- `MaxConcurrentOperations`: Maximum concurrent CAS operations (4)
- `AutoGcIntervalDays`: Automatic garbage collection interval (1 day)
- `GcGracePeriodDays`: Grace period before unreferenced objects are garbage collected (7 days)
- `MaintenanceErrorRetryDelayMinutes`: Retry delay for maintenance errors (5 minutes)

### ConfigurationKeys

Configuration key constants for appsettings.json and environment variables.

#### Base Configuration

- `GenHubSection`: Base configuration section (`"GenHub"`)

#### Workspace Configuration

- `WorkspaceDefaultPath`: Default workspace path key (`"GenHub:Workspace:DefaultPath"`)
- `WorkspaceDefaultStrategy`: Default workspace strategy key (`"GenHub:Workspace:DefaultStrategy"`)

#### Cache Configuration

- `CacheDefaultPath`: Default cache directory path key (`"GenHub:Cache:DefaultPath"`)

#### UI Configuration Keys

- `UiDefaultTheme`: Default UI theme key (`"GenHub:UI:DefaultTheme"`)
- `UiDefaultWindowWidth`: Default window width key (`"GenHub:UI:DefaultWindowWidth"`)
- `UiDefaultWindowHeight`: Default window height key (`"GenHub:UI:DefaultWindowHeight"`)

#### Downloads Configuration

- `DownloadsDefaultTimeoutSeconds`: Default download timeout key (`"GenHub:Downloads:DefaultTimeoutSeconds"`)
- `DownloadsDefaultUserAgent`: Default user agent key (`"GenHub:Downloads:DefaultUserAgent"`)
- `DownloadsDefaultMaxConcurrent`: Maximum concurrent downloads key (`"GenHub:Downloads:DefaultMaxConcurrent"`)
- `DownloadsDefaultBufferSize`: Default buffer size key (`"GenHub:Downloads:DefaultBufferSize"`)

#### Downloads Policy Configuration

- `DownloadsPolicyMinConcurrent`: Minimum concurrent downloads policy (`"GenHub:Downloads:Policy:MinConcurrent"`)
- `DownloadsPolicyMaxConcurrent`: Maximum concurrent downloads policy (`"GenHub:Downloads:Policy:MaxConcurrent"`)
- `DownloadsPolicyMinTimeoutSeconds`: Minimum timeout policy (`"GenHub:Downloads:Policy:MinTimeoutSeconds"`)
- `DownloadsPolicyMaxTimeoutSeconds`: Maximum timeout policy (`"GenHub:Downloads:Policy:MaxTimeoutSeconds"`)
- `DownloadsPolicyMinBufferSizeBytes`: Minimum buffer size policy (`"GenHub:Downloads:Policy:MinBufferSizeBytes"`)
- `DownloadsPolicyMaxBufferSizeBytes`: Maximum buffer size policy (`"GenHub:Downloads:Policy:MaxBufferSizeBytes"`)

#### Application Data

- `AppDataPath`: Application data path key (`"GenHub:AppDataPath"`)

### DirectoryNames

Standard directory names used for organizing content storage.

- `Data`: Directory for storing content data
- `Cache`: Directory for storing cache files
- `Temp`: Directory for storing temporary files
- `Logs`: Directory for storing log files
- `Backups`: Directory for storing backup files

### DownloadDefaults

Default values and limits for download operations.

- `BufferSizeBytes`: Default buffer size for downloads (81920 bytes / 80KB)
- `BufferSizeKB`: Buffer size in kilobytes for display (80.0)
- `MaxConcurrentDownloads`: Maximum concurrent downloads (3)
- `MaxRetryAttempts`: Maximum retry attempts for failed downloads (3)
- `TimeoutSeconds`: Default download timeout (600 seconds / 10 minutes)
- `RetryDelaySeconds`: Delay between retry attempts (1 second)

### FileTypes

File and directory name constants to prevent typos and ensure consistency.

#### Manifest Files

- `ManifestsDirectory`: Directory for manifest files (`"Manifests"`)
- `ManifestFilePattern`: File pattern for manifest files (`"*.manifest.json"`)
- `ManifestFileExtension`: File extension for manifest files (`".manifest.json"`)

#### JSON Files

- `JsonFileExtension`: File extension for JSON files (`".json"`)
- `JsonFilePattern`: File pattern for JSON files (`"*.json"`)

#### Settings

- `SettingsFileName`: Default settings file name (`"settings.json"`)

### ProcessConstants

Process and system constants, including Windows API constants.

#### Exit Codes

- `ExitCodeSuccess`: Successful execution (0)
- `ExitCodeGeneralError`: General error (1)
- `ExitCodeInvalidArguments`: Invalid arguments (2)
- `ExitCodeFileNotFound`: File not found (3)
- `ExitCodeAccessDenied`: Access denied (5)

#### Windows API Constants

- `SW_RESTORE`: Restore minimized window (9)
- `SW_SHOW`: Show window in current state (5)
- `SW_MINIMIZE`: Minimize window (6)
- `SW_MAXIMIZE`: Maximize window (3)

#### Process Priority Classes

- `REALTIME_PRIORITY_CLASS`: Real-time priority (0x00000100)
- `HIGH_PRIORITY_CLASS`: High priority (0x00000080)
- `ABOVE_NORMAL_PRIORITY_CLASS`: Above normal priority (0x00008000)
- `NORMAL_PRIORITY_CLASS`: Normal priority (0x00000020)
- `BELOW_NORMAL_PRIORITY_CLASS`: Below normal priority (0x00004000)
- `IDLE_PRIORITY_CLASS`: Idle priority (0x00000040)

### StorageConstants

Storage and CAS (Content-Addressable Storage) related constants.

#### CAS Retry Constants

- `MaxRetries`: Maximum retry attempts for CAS operations (10)
- `RetryDelayMs`: Delay between retry attempts (100ms)
- `MaxRetryDelayMs`: Maximum delay for exponential backoff (5000ms)

#### File System Constants

- `DefaultBufferSize`: Default buffer size for file operations (8192 bytes)
- `LargeBufferSize`: Large buffer size for high-throughput operations (65536 bytes)
- `SmallBufferSize`: Small buffer size for low-memory operations (4096 bytes)

#### Hash Algorithm Constants

- `Sha256Algorithm`: SHA-256 algorithm name (`"SHA256"`)
- `Sha1Algorithm`: SHA-1 algorithm name (`"SHA1"`)
- `Md5Algorithm`: MD5 algorithm name (`"MD5"`)

#### Storage Limits

- `MaxInMemoryFileSize`: Maximum file size for in-memory processing (100MB)
- `MaxConcurrentFileOperations`: Maximum concurrent file operations (5)
- `FileOperationTimeoutSeconds`: Timeout for file operations (30 seconds)

#### CAS Directory Structure

- `ObjectsDirectory`: Directory for CAS objects (`"objects"`)
- `TempDirectory`: Directory for CAS temporary files (`"temp"`)
- `MetadataDirectory`: Directory for CAS metadata (`"metadata"`)
- `IndexDirectory`: Directory for CAS indexes (`"index"`)

### TimeIntervals

Time intervals and durations used throughout the application.

- `DownloadProgressInterval`: Default progress reporting interval for downloads (500ms)
- `UpdaterTimeout`: Default timeout for updater operations (10 minutes)
- `CasMaintenanceRetryDelay`: CAS maintenance error retry delay (5 minutes)
- `MemoryUpdateInterval`: Memory update interval for UI (2 seconds)

### UiConstants

UI-related constants for consistent user experience.

- `DefaultWindowWidth`: Default main window width (1200 pixels)
- `DefaultWindowHeight`: Default main window height (800 pixels)
- `MinWindowWidth`: Minimum allowed window width (800 pixels)
- `MinWindowHeight`: Minimum allowed window height (600 pixels)
- `MemoryUpdateIntervalSeconds`: Memory update interval in seconds (2)

### ValidationLimits

Validation limits and constraints for configuration values.

#### Concurrent Downloads

- `MinConcurrentDownloads`: Minimum allowed concurrent downloads (1)
- `MaxConcurrentDownloads`: Maximum allowed concurrent downloads (10)

#### Download Timeout

- `MinDownloadTimeoutSeconds`: Minimum allowed download timeout (30 seconds)
- `MaxDownloadTimeoutSeconds`: Maximum allowed download timeout (3600 seconds / 1 hour)

#### Download Buffer Size

- `MinDownloadBufferSizeBytes`: Minimum allowed buffer size (4096 bytes / 4KB)
- `MaxDownloadBufferSizeBytes`: Maximum allowed buffer size (1048576 bytes / 1MB)

## Usage Examples

### Using Configuration Keys

```csharp
// Reading configuration values
var workspacePath = configuration["GenHub:Workspace:DefaultPath"];
var maxConcurrent = configuration.GetValue<int>("GenHub:Downloads:DefaultMaxConcurrent");

// Using the constants for consistency
var workspacePath = configuration[ConfigurationKeys.WorkspaceDefaultPath];
var maxConcurrent = configuration.GetValue<int>(ConfigurationKeys.DownloadsDefaultMaxConcurrent);
```

### HTTP Operations with API Constants

```csharp
// Using API constants for GitHub integration
var client = new HttpClient();
client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.GitHubApiUserAgent);

var releasesUrl = $"{ApiConstants.GitHubApiBaseUrl}{ApiConstants.GitHubReleasesApiEndpoint}";
var url = releasesUrl.Replace("{owner}", "microsoft").Replace("{repo}", "vscode");
```

### File Operations with Storage Constants

```csharp
// Using storage constants for file operations
using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
    StorageConstants.LargeBufferSize, FileOptions.Asynchronous);

// CAS operations with retry logic
var retryCount = 0;
while (retryCount < StorageConstants.MaxRetries)
{
    try
    {
        // Perform CAS operation
        break;
    }
    catch (Exception)
    {
        retryCount++;
        await Task.Delay(StorageConstants.RetryDelayMs * retryCount);
    }
}
```

### UI Configuration

```csharp
// Setting up window dimensions
var window = new Window
{
    Width = UiConstants.DefaultWindowWidth,
    Height = UiConstants.DefaultWindowHeight,
    MinWidth = UiConstants.MinWindowWidth,
    MinHeight = UiConstants.MinWindowHeight
};
```

### Process Management

```csharp
// Setting process priority
var process = Process.Start(processInfo);
process.PriorityClass = (ProcessPriorityClass)ProcessConstants.HIGH_PRIORITY_CLASS;

// Handling exit codes
if (exitCode == ProcessConstants.ExitCodeSuccess)
{
    // Success handling
}
else if (exitCode == ProcessConstants.ExitCodeFileNotFound)
{
    // File not found handling
}
```

## Best Practices

1. **Always use constants instead of magic numbers/strings**: This ensures consistency and makes maintenance easier.

2. **Group related constants**: Constants are organized by functionality (API, UI, Storage, etc.) for better discoverability.

3. **Use descriptive names**: All constants have clear, descriptive names that indicate their purpose.

4. **Document constants**: Each constant includes XML documentation explaining its purpose and usage.

5. **Centralize configuration keys**: All configuration keys are defined in `ConfigurationKeys.cs` to prevent typos and ensure consistency.

6. **Use appropriate data types**: Time intervals use `TimeSpan`, sizes use appropriate numeric types.

## Testing

Constants should be tested for:

- Correct values
- Proper grouping and organization
- Usage in dependent code
- Configuration key resolution

Example test:

```csharp
[Test]
public void ApiConstants_GitHubUrls_ShouldBeValid()
{
    // Arrange & Act
    var baseUrl = ApiConstants.GitHubApiBaseUrl;
    var releasesEndpoint = ApiConstants.GitHubReleasesApiEndpoint;

    // Assert
    Assert.That(baseUrl, Is.EqualTo("https://api.github.com"));
    Assert.That(releasesEndpoint, Does.Contain("/repos/"));
    Assert.That(releasesEndpoint, Does.Contain("/releases"));
}
```

## Maintenance

When adding new constants:

1. Choose the appropriate constants file based on functionality
2. Follow naming conventions (PascalCase for constants)
3. Add comprehensive XML documentation
4. Update this documentation
5. Add tests for new constants
6. Ensure StyleCop compliance

## Related Documentation

- [Configuration Guide](../configuration.md)
- [API Integration Guide](../api-integration.md)
- [Storage Architecture](../storage-architecture.md)
