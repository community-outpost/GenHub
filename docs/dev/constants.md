# Constants API Reference

This document provides comprehensive documentation for all constants used throughout the GenHub application. Constants are organized into logical groups for better maintainability and consistency.

## Overview

GenHub uses a centralized constants system to ensure consistency across the application. All constants are defined in static classes within the `GenHub.Core.Constants` namespace and follow StyleCop conventions.

## Constants Files

### ApiConstants

API and network related constants for HTTP operations and GitHub integration.

#### GitHub API Endpoints

- `GitHubApiBaseUrl`: GitHub API base URL (`"https://api.github.com"`)
- `GitHubRawBaseUrl`: GitHub raw content base URL (`"https://raw.githubusercontent.com"`)
- `GitHubRepoApiEndpoint`: Repository API endpoint template (`"/repos/{owner}/{repo}"`)
- `GitHubReleasesApiEndpoint`: Releases API endpoint template (`"/repos/{owner}/{repo}/releases"`)
- `GitHubLatestReleaseApiEndpoint`: Latest release API endpoint template (`"/repos/{owner}/{repo}/releases/latest"`)
- `GitHubReleaseAssetsApiEndpoint`: Release assets API endpoint template (`"/repos/{owner}/{repo}/releases/{releaseId}/assets"`)
- `GitHubContentsApiEndpoint`: Repository contents API endpoint template (`"/repos/{owner}/{repo}/contents/{path}"`)

#### HTTP Status Codes

- `HttpOk`: HTTP OK status code (200)
- `HttpCreated`: HTTP Created status code (201)
- `HttpNoContent`: HTTP No Content status code (204)
- `HttpBadRequest`: HTTP Bad Request status code (400)
- `HttpUnauthorized`: HTTP Unauthorized status code (401)
- `HttpForbidden`: HTTP Forbidden status code (403)
- `HttpNotFound`: HTTP Not Found status code (404)
- `HttpInternalServerError`: HTTP Internal Server Error status code (500)

#### Network Timeouts

- `DefaultHttpTimeoutSeconds`: Default HTTP request timeout in seconds (30)
- `LongHttpTimeoutSeconds`: Long HTTP request timeout for large downloads in seconds (300)
- `ShortHttpTimeoutSeconds`: Short HTTP request timeout for quick operations in seconds (10)

#### User Agents

- `DefaultUserAgent`: Default user agent string for HTTP requests (`"GenHub/1.0"`)
- `GitHubApiUserAgent`: GitHub API user agent string (`"GenHub-GitHub-API/1.0"`)

#### Rate Limiting

- `GitHubApiRateLimitAuthenticated`: GitHub API rate limit per hour for authenticated requests (5000)
- `GitHubApiRateLimitUnauthenticated`: GitHub API rate limit per hour for unauthenticated requests (60)
- `DefaultApiRequestDelayMs`: Default delay between API requests in milliseconds (1000)

#### Content Types

- `ContentTypeJson`: JSON content type (`"application/json"`)
- `ContentTypeOctetStream`: Octet stream content type for binary data (`"application/octet-stream"`)
- `ContentTypeFormUrlEncoded`: Form URL encoded content type (`"application/x-www-form-urlencoded"`)

### AppConstants

Application-wide constants for GenHub.

- `Version`: Current version of GenHub (`"1.0"`)
- `ApplicationName`: Application name (`"GenHub"`)
- `DefaultTheme`: Default UI theme (Theme.Dark)
- `DefaultThemeName`: Default theme name as string (`"Dark"`)
- `DefaultUserAgent`: Default user agent string (`"GenHub/1.0"`)

### CasDefaults

Default values and limits for Content-Addressable Storage (CAS).

- `MaxCacheSizeBytes`: Default maximum cache size in bytes (50GB)
- `MaxConcurrentOperations`: Default maximum concurrent CAS operations (4)

### ConfigurationKeys

Configuration key constants for appsettings.json and environment variables.

#### Workspace Configuration

- `WorkspaceDefaultPath`: Configuration key for default workspace path (`"GenHub:Workspace:DefaultPath"`)
- `WorkspaceDefaultStrategy`: Configuration key for default workspace strategy (`"GenHub:Workspace:DefaultStrategy"`)

#### Cache Configuration

- `CacheDefaultPath`: Configuration key for default cache directory path (`"GenHub:Cache:DefaultPath"`)

#### UI Configuration

- `UiDefaultTheme`: Configuration key for default UI theme (`"GenHub:UI:DefaultTheme"`)
- `UiDefaultWindowWidth`: Configuration key for default window width (`"GenHub:UI:DefaultWindowWidth"`)
- `UiDefaultWindowHeight`: Configuration key for default window height (`"GenHub:UI:DefaultWindowHeight"`)

#### Downloads Configuration

- `DownloadsDefaultTimeoutSeconds`: Configuration key for default download timeout (`"GenHub:Downloads:DefaultTimeoutSeconds"`)
- `DownloadsDefaultUserAgent`: Configuration key for default user agent (`"GenHub:Downloads:DefaultUserAgent"`)
- `DownloadsDefaultMaxConcurrent`: Configuration key for default maximum concurrent downloads (`"GenHub:Downloads:DefaultMaxConcurrent"`)
- `DownloadsDefaultBufferSize`: Configuration key for default download buffer size (`"GenHub:Downloads:DefaultBufferSize"`)

#### Downloads Policy Configuration

- `DownloadsPolicyMinConcurrent`: Configuration key for minimum concurrent downloads policy (`"GenHub:Downloads:Policy:MinConcurrent"`)
- `DownloadsPolicyMaxConcurrent`: Configuration key for maximum concurrent downloads policy (`"GenHub:Downloads:Policy:MaxConcurrent"`)
- `DownloadsPolicyMinTimeoutSeconds`: Configuration key for minimum download timeout policy (`"GenHub:Downloads:Policy:MinTimeoutSeconds"`)
- `DownloadsPolicyMaxTimeoutSeconds`: Configuration key for maximum download timeout policy (`"GenHub:Downloads:Policy:MaxTimeoutSeconds"`)
- `DownloadsPolicyMinBufferSizeBytes`: Configuration key for minimum download buffer size policy (`"GenHub:Downloads:Policy:MinBufferSizeBytes"`)
- `DownloadsPolicyMaxBufferSizeBytes`: Configuration key for maximum download buffer size policy (`"GenHub:Downloads:Policy:MaxBufferSizeBytes"`)

#### App Data Configuration

- `AppDataPath`: Configuration key for application data path (`"GenHub:AppDataPath"`)

### DirectoryNames

Standard directory names used for organizing content storage.

- `Data`: Directory for storing content data
- `Cache`: Directory for storing cache files
- `Temp`: Directory for storing temporary files
- `Logs`: Directory for storing log files
- `Backups`: Directory for storing backup files

### DownloadDefaults

Default values and limits for download operations.

- `BufferSizeBytes`: Default buffer size for file download operations (80KB)
- `BufferSizeKB`: Default buffer size in kilobytes for display purposes (80.0)
- `MaxConcurrentDownloads`: Default maximum number of concurrent downloads (3)
- `MaxRetryAttempts`: Default maximum retry attempts for failed downloads (3)
- `TimeoutSeconds`: Default download timeout in seconds (600)
- `RetryDelaySeconds`: Default retry delay in seconds (1)

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

Process and system constants.

#### Exit Codes

- `ExitCodeSuccess`: Standard exit code indicating successful execution (0)
- `ExitCodeGeneralError`: Standard exit code indicating general error (1)
- `ExitCodeInvalidArguments`: Exit code indicating invalid arguments (2)
- `ExitCodeFileNotFound`: Exit code indicating file not found (3)
- `ExitCodeAccessDenied`: Exit code indicating access denied (5)

#### Windows API Constants

- `SW_RESTORE`: Windows API constant for restoring a minimized window (9)
- `SW_SHOW`: Windows API constant for showing a window in its current state (5)
- `SW_MINIMIZE`: Windows API constant for minimizing a window (6)
- `SW_MAXIMIZE`: Windows API constant for maximizing a window (3)

#### Process Priority Constants

- `REALTIME_PRIORITY_CLASS`: Process priority class for real-time priority (0x00000100)
- `HIGH_PRIORITY_CLASS`: Process priority class for high priority (0x00000080)
- `ABOVE_NORMAL_PRIORITY_CLASS`: Process priority class for above normal priority (0x00008000)
- `NORMAL_PRIORITY_CLASS`: Process priority class for normal priority (0x00000020)
- `BELOW_NORMAL_PRIORITY_CLASS`: Process priority class for below normal priority (0x00004000)
- `IDLE_PRIORITY_CLASS`: Process priority class for idle priority (0x00000040)

### StorageConstants

Storage and CAS (Content-Addressable Storage) related constants.

#### CAS Retry Constants

- `MaxRetries`: Maximum retry attempts for CAS operations (10)
- `RetryDelayMs`: Delay between retry attempts (100ms)
- `MaxRetryDelayMs`: Maximum delay for exponential backoff (5000ms)

#### CAS Directory Structure

- `ObjectsDirectory`: Directory for CAS objects (`"objects"`)
- `LocksDirectory`: Directory for CAS locks (`"locks"`)

#### CAS Maintenance Constants

- `AutoGcIntervalDays`: Default automatic garbage collection interval in days (1)

### TimeIntervals

Time intervals and durations used throughout the application.

- `DownloadProgressInterval`: Default progress reporting interval for downloads (500ms)
- `UpdaterTimeout`: Default timeout for updater operations (10 minutes)
- `CasMaintenanceRetryDelay`: Default CAS maintenance error retry delay (5 minutes)
- `MemoryUpdateInterval`: Memory update interval for UI (2 seconds)

### UiConstants

UI-related constants for consistent user experience.

- `DefaultWindowWidth`: Default main window width in pixels (1200)
- `DefaultWindowHeight`: Default main window height in pixels (800)
- `MinWindowWidth`: Minimum allowed window width (800)
- `MinWindowHeight`: Minimum allowed window height (600)
- `MemoryUpdateIntervalSeconds`: Memory update interval in seconds for UI display (2)

### ValidationLimits

Validation limits and constraints.

- `MinConcurrentDownloads`: Minimum allowed concurrent downloads (1)
- `MaxConcurrentDownloads`: Maximum allowed concurrent downloads (10)
- `MinDownloadTimeoutSeconds`: Minimum allowed download timeout in seconds (30)
- `MaxDownloadTimeoutSeconds`: Maximum allowed download timeout in seconds (3600)
- `MinDownloadBufferSizeBytes`: Minimum allowed download buffer size in bytes (4096)
- `MaxDownloadBufferSizeBytes`: Maximum allowed download buffer size in bytes (1048576)

## Usage Examples

### File Operations with Storage Constants

```csharp
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

### Directory Operations with DirectoryNames

```csharp
// Using directory constants for consistent paths
var dataPath = Path.Combine(basePath, DirectoryNames.Data);
var cachePath = Path.Combine(basePath, DirectoryNames.Cache);
var tempPath = Path.Combine(basePath, DirectoryNames.Temp);
```

### File Type Validation with FileTypes

```csharp
// Using file type constants for validation
if (fileName.EndsWith(FileTypes.ManifestFileExtension))
{
    // Handle manifest file
}
else if (fileName.EndsWith(FileTypes.JsonFileExtension))
{
    // Handle JSON file
}
```

### HTTP Operations with ApiConstants

```csharp
// Using API constants for HTTP requests
using var client = new HttpClient();
client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.DefaultUserAgent);
client.Timeout = TimeSpan.FromSeconds(ApiConstants.DefaultHttpTimeoutSeconds);

var response = await client.GetAsync($"{ApiConstants.GitHubApiBaseUrl}/repos/{owner}/{repo}");
```

### Download Configuration with DownloadDefaults

```csharp
// Using download defaults for configuration
var downloadConfig = new DownloadConfiguration
{
    BufferSize = DownloadDefaults.BufferSizeBytes,
    MaxConcurrentDownloads = DownloadDefaults.MaxConcurrentDownloads,
    Timeout = TimeSpan.FromSeconds(DownloadDefaults.TimeoutSeconds),
    MaxRetryAttempts = DownloadDefaults.MaxRetryAttempts
};
```

### UI Configuration with UiConstants

```csharp
// Using UI constants for window sizing
var window = new Window
{
    Width = UiConstants.DefaultWindowWidth,
    Height = UiConstants.DefaultWindowHeight,
    MinWidth = UiConstants.MinWindowWidth,
    MinHeight = UiConstants.MinWindowHeight
};
```

### Validation with ValidationLimits

```csharp
// Using validation limits for input validation
public bool ValidateConcurrentDownloads(int value)
{
    return value >= ValidationLimits.MinConcurrentDownloads &&
           value <= ValidationLimits.MaxConcurrentDownloads;
}
```

### CAS Configuration with CasDefaults

```csharp
// Using CAS defaults for storage configuration
var casConfig = new CasConfiguration
{
    MaxCacheSizeBytes = CasDefaults.MaxCacheSizeBytes,
    MaxConcurrentOperations = CasDefaults.MaxConcurrentOperations,
    AutoGcInterval = TimeSpan.FromDays(StorageConstants.AutoGcIntervalDays)
};
```

### Time Intervals Usage

```csharp
// Using time intervals for scheduling
var timer = new Timer(UpdateProgress, null,
    TimeSpan.Zero, TimeIntervals.DownloadProgressInterval);

var memoryTimer = new Timer(UpdateMemory, null,
    TimeSpan.Zero, TimeIntervals.MemoryUpdateInterval);
```

## Maintenance

When adding new constants:

1. Choose the appropriate constants file based on functionality
2. Follow naming conventions (PascalCase for constants)
3. Add comprehensive XML documentation
4. Update this documentation
5. Add tests for new constants
6. Ensure StyleCop compliance

### Constants File Organization

- **ApiConstants**: Network, HTTP, and API-related constants
- **AppConstants**: Application-wide settings and metadata
- **CasDefaults**: Content-Addressable Storage defaults
- **ConfigurationKeys**: Configuration file keys and paths
- **DirectoryNames**: Standard directory naming conventions
- **DownloadDefaults**: Download operation defaults
- **FileTypes**: File extensions and naming patterns
- **ProcessConstants**: System process and exit code constants
- **StorageConstants**: Storage and CAS operation constants
- **TimeIntervals**: Time spans and intervals
- **UiConstants**: User interface sizing and behavior
- **ValidationLimits**: Input validation boundaries

### Best Practices

1. **Centralization**: All constants should be defined in the appropriate constants file
2. **Documentation**: Every constant should have XML documentation explaining its purpose
3. **Testing**: Constants should be tested for correctness and reasonable values
4. **Consistency**: Use constants instead of magic numbers or strings throughout the codebase
5. **Naming**: Use descriptive names that clearly indicate the constant's purpose
6. **Grouping**: Related constants should be grouped together within their respective files

## Related Documentation

- [Storage Architecture](../storage-architecture.md)
- [Configuration Management](../configuration-management.md)
- [API Integration Guide](../api-integration.md)
