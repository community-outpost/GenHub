# Constants API Reference

This document provides comprehensive documentation for all constants used throughout the GenHub application. Constants are organized into logical groups for better maintainability and consistency.

## Overview

GenHub uses a centralized constants system to ensure consistency across the application. All constants are defined in static classes within the `GenHub.Core.Constants` namespace and follow StyleCop conventions.

## Constants Files

### ApiConstants

API and network related constants.

#### User Agents

- `DefaultUserAgent`: Default user agent string for HTTP requests (constructed from `AppConstants.AppName` and `AppConstants.AppVersion`)

#### GitHub

- `GitHubDomain`: GitHub domain name (`"github.com"`)
- `GitHubUrlRegexPattern`: GitHub URL regex pattern for parsing repository URLs (`@"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)(?:/releases/tag/(?<tag>[^/]+))?"`)

### UriConstants

URI scheme constants for handling different types of URIs and paths.

- `AvarUriScheme`: URI scheme for Avalonia embedded resources (`"avares://"`)
- `HttpUriScheme`: HTTP URI scheme (`"http://"`)
- `HttpsUriScheme`: HTTPS URI scheme (`"https://"`)
- `GeneralsIconUri`: Icon URI for Generals game type (`"avares://GenHub/Assets/Icons/generals-icon.png"`)
- `ZeroHourIconUri`: Icon URI for Zero Hour game type (`"avares://GenHub/Assets/Icons/zerohour-icon.png"`)
- `DefaultIconUri`: Default icon URI for unknown game types (`"avares://GenHub/Assets/Icons/generalshub-icon.png"`)

### AppConstants

Application-wide constants for GenHub.

- `AppName`: The name of the application (`"GenHub"`)
- `AppVersion`: The version of the application (`"1.0"`)
- `DefaultTheme`: Default UI theme (Theme.Dark)
- `DefaultThemeName`: Default theme name as string (`"Dark"`)

### CasDefaults

Default values and limits for Content-Addressable Storage (CAS).

- `MaxCacheSizeBytes`: Default maximum cache size in bytes (50GB)
- `DefaultMaxCacheSizeGB`: Default maximum cache size in gigabytes (50)
- `MaxConcurrentOperations`: Default maximum concurrent CAS operations (4)
- `GcGracePeriodDays`: Default garbage collection grace period in days (7)

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

### ConversionConstants

Constants for unit conversions used throughout the application.

- `BytesPerKilobyte`: Number of bytes in one kilobyte (1024)
- `BytesPerMegabyte`: Number of bytes in one megabyte (1048576)
- `BytesPerGigabyte`: Number of bytes in one gigabyte (1073741824)

#### Color Conversion Constants

- `LuminanceRedCoefficient`: Coefficient for red channel in luminance calculation (0.299)
- `LuminanceGreenCoefficient`: Coefficient for green channel in luminance calculation (0.587)
- `LuminanceBlueCoefficient`: Coefficient for blue channel in luminance calculation (0.114)
- `BrightnessThreshold`: Threshold value for determining if a color is light or dark (0.5)

### DirectoryNames

Directory names used for organizing content storage.

- `Data`: Directory for storing content data (`"Data"`)
- `Cache`: Directory for storing cache files (`"Cache"`)
- `CasPool`: Directory for Content-Addressable Storage (CAS) pool (`"cas-pool"`)

### DownloadDefaults

Default values and limits for download operations.

- `BufferSizeBytes`: Default buffer size for file download operations (81920)
- `BufferSizeKB`: Default buffer size in kilobytes for display purposes (80.0)
- `MinBufferSizeKB`: Minimum buffer size in kilobytes for validation (4.0)
- `MaxBufferSizeKB`: Maximum buffer size in kilobytes for validation (1024.0)
- `MaxConcurrentDownloads`: Default maximum number of concurrent downloads (3)
- `MaxRetryAttempts`: Default maximum retry attempts for failed downloads (3)
- `TimeoutSeconds`: Default download timeout in seconds (600)
- `FileBufferSizeBytes`: Default buffer size for file operations (4096)

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

### IoConstants

Constants for input/output operations.

- `DefaultFileBufferSize`: Default buffer size for file operations (4096 bytes)

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

### StorageConstants

Storage and CAS (Content-Addressable Storage) related constants.

#### CAS Retry Constants

- `MaxRetries`: Maximum number of retry attempts for CAS operations (10)

#### CAS Maintenance Constants

- `AutoGcIntervalDays`: Default automatic garbage collection interval in days (1)

### TimeIntervals

Time intervals and durations used throughout the application.

- `UpdaterTimeout`: Default timeout for updater operations (10 minutes)
- `DownloadTimeout`: Default timeout for download operations (30 minutes)
- `NotificationHideDelay`: Delay for hiding UI notifications (3000ms)

### UiConstants

UI-related constants for consistent user experience.

- `DefaultWindowWidth`: Default main window width in pixels (1200)
- `DefaultWindowHeight`: Default main window height in pixels (800)

#### Status Colors

- `StatusSuccessColor`: Color used to indicate success or positive status (`"#4CAF50"`)
- `StatusErrorColor`: Color used to indicate error or negative status (`"#F44336"`)

### ValidationLimits

Validation limits and constraints.

- `MinConcurrentDownloads`: Minimum allowed concurrent downloads (1)
- `MaxConcurrentDownloads`: Maximum allowed concurrent downloads (10)
- `MinDownloadTimeoutSeconds`: Minimum allowed download timeout in seconds (30)
- `MaxDownloadTimeoutSeconds`: Maximum allowed download timeout in seconds (3600)
- `MinDownloadBufferSizeBytes`: Minimum allowed download buffer size in bytes (4096)
- `MaxDownloadBufferSizeBytes`: Maximum allowed download buffer size in bytes (1048576)

## Usage Examples

### Directory Operations with DirectoryNames

```csharp
// Using directory constants for consistent paths
var dataPath = Path.Combine(basePath, DirectoryNames.Data);
var cachePath = Path.Combine(basePath, DirectoryNames.Cache);
var casPoolPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    AppConstants.AppName,
    DirectoryNames.CasPool);
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

// DefaultUserAgent is constructed as: $"{AppConstants.AppName}/{AppConstants.AppVersion}"
// Result: "GenHub/1.0"
```

### GitHub URL Validation with ApiConstants

```csharp
// Using GitHub constants for URL validation
public bool IsGitHubUrl(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return false;

    return uri.Host.Equals(ApiConstants.GitHubDomain, StringComparison.OrdinalIgnoreCase);
}

// Using GitHub regex pattern for parsing
public (string owner, string repo, string? tag) ParseGitHubUrl(string url)
{
    var regex = new Regex(ApiConstants.GitHubUrlRegexPattern, RegexOptions.Compiled);
    var match = regex.Match(url);

    if (!match.Success)
        return (null, null, null);

    var owner = match.Groups["owner"].Value;
    var repo = match.Groups["repo"].Value;
    var tag = match.Groups["tag"].Success ? match.Groups["tag"].Value : null;

    return (owner, repo, tag);
}
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
    Height = UiConstants.DefaultWindowHeight
};

// Using status colors for consistent UI theming
var successBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusSuccessColor));
var errorBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusErrorColor));

// Using in XAML data binding with BoolToStatusColorConverter
// <TextBlock Text="Status" Foreground="{Binding IsActive, Converter={StaticResource BoolToStatusColorConverter}}" />
```

### Application Name Usage with AppConstants

```csharp
// Using AppConstants.AppName for consistent application naming
var appDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    AppConstants.AppName);

var userAgent = $"{AppConstants.AppName}/{AppConstants.AppVersion}";
// Result: "GenHub/1.0" (same as ApiConstants.DefaultUserAgent)
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

// Using retry constants for CAS operations
for (int attempt = 0; attempt < StorageConstants.MaxRetries; attempt++)
{
    try
    {
        // CAS operation
        break;
    }
    catch (IOException)
    {
        if (attempt == StorageConstants.MaxRetries - 1)
            throw;
        
        await Task.Delay(100);
    }
}
```

### Time Intervals Usage

```csharp
// Using time intervals for timeouts
var updaterTimeout = TimeIntervals.UpdaterTimeout;
var downloadTimeout = TimeIntervals.DownloadTimeout;
var notificationDelay = TimeIntervals.NotificationHideDelay;
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

- **ApiConstants**: Network and API-related constants
- **AppConstants**: Application-wide settings and metadata
- **CasDefaults**: Content-Addressable Storage defaults
- **ConfigurationKeys**: Configuration file keys and paths
- **ConversionConstants**: Unit conversion constants
- **DirectoryNames**: Standard directory naming conventions
- **DownloadDefaults**: Download operation defaults
- **FileTypes**: File extensions and naming patterns
- **IoConstants**: Input/output operation constants
- **ProcessConstants**: System process and exit code constants
- **StorageConstants**: Storage and CAS operation constants (retry limits, maintenance intervals)
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

- [Complete System Architecture](../architecture.md)
