# Constants Reference

This document provides comprehensive documentation for all constants used throughout the GenHub application. Constants are defined in static classes within the `GenHub.Core.Constants` namespace and follow StyleCop conventions.

## Overview

GenHub uses a centralized constants system to ensure consistency across the application. Constants are organized into logical groups for better maintainability and consistency.

---

## ApiConstants Class

API and network related constants.

### User Agents

- `DefaultUserAgent`: Default user agent string for HTTP requests (constructed as `"GenHub/1.0"` from `AppConstants.ApplicationName` and `AppConstants.Version`)

### GitHub

- `GitHubDomain`: GitHub domain name (`"github.com"`)
- `GitHubUrlRegexPattern`: Regex pattern for parsing repository URLs  
  (`@"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)(?:/releases/tag/(?<tag>[^/]+))?"`)

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

| Constant | Value | Description |
|----------|-------|-------------|
| `ApplicationName` | `"GenHub"` | Application name |
| `Version` | `"1.0"` | Current version of GenHub |
| `DefaultTheme` | `Theme.Dark` | Default UI theme |
| `DefaultThemeName` | `"Dark"` | Default theme name as string |
| `DefaultUserAgent` | `"GenHub/1.0"` | Default user agent string |

---

## CasDefaults Class

Default values and limits for Content-Addressable Storage (CAS).

| Constant | Value | Description |
|----------|-------|-------------|
| `MaxCacheSizeBytes` | `53687091200` (50GB) | Default maximum cache size |
| `DefaultMaxCacheSizeGB` | `50` | Default maximum cache size in gigabytes |
| `MaxConcurrentOperations` | `4` | Default maximum concurrent CAS operations |
| `GcGracePeriodDays` | `7` | Default garbage collection grace period in days |

---

## ConfigurationKeys Class

Configuration key constants for `appsettings.json` and environment variables.

### Workspace Configuration
- `WorkspaceDefaultPath`: `"GenHub:Workspace:DefaultPath"`
- `WorkspaceDefaultStrategy`: `"GenHub:Workspace:DefaultStrategy"`

### Cache Configuration
- `CacheDefaultPath`: `"GenHub:Cache:DefaultPath"`

### UI Configuration
- `UiDefaultTheme`: `"GenHub:UI:DefaultTheme"`
- `UiDefaultWindowWidth`: `"GenHub:UI:DefaultWindowWidth"`
- `UiDefaultWindowHeight`: `"GenHub:UI:DefaultWindowHeight"`

### Downloads Configuration
- `DownloadsDefaultTimeoutSeconds`: `"GenHub:Downloads:DefaultTimeoutSeconds"`
- `DownloadsDefaultUserAgent`: `"GenHub:Downloads:DefaultUserAgent"`
- `DownloadsDefaultMaxConcurrent`: `"GenHub:Downloads:DefaultMaxConcurrent"`
- `DownloadsDefaultBufferSize`: `"GenHub:Downloads:DefaultBufferSize"`

### Downloads Policy Configuration
- `DownloadsPolicyMinConcurrent`: `"GenHub:Downloads:Policy:MinConcurrent"`
- `DownloadsPolicyMaxConcurrent`: `"GenHub:Downloads:Policy:MaxConcurrent"`
- `DownloadsPolicyMinTimeoutSeconds`: `"GenHub:Downloads:Policy:MinTimeoutSeconds"`
- `DownloadsPolicyMaxTimeoutSeconds`: `"GenHub:Downloads:Policy:MaxTimeoutSeconds"`
- `DownloadsPolicyMinBufferSizeBytes`: `"GenHub:Downloads:Policy:MinBufferSizeBytes"`
- `DownloadsPolicyMaxBufferSizeBytes`: `"GenHub:Downloads:Policy:MaxBufferSizeBytes"`

### App Data Configuration
- `AppDataPath`: `"GenHub:AppDataPath"`

---

## ConversionConstants Class

Constants for unit conversions used throughout the application.

- `BytesPerKilobyte`: 1024  
- `BytesPerMegabyte`: 1048576  
- `BytesPerGigabyte`: 1073741824  

#### Color Conversion Constants

- `LuminanceRedCoefficient`: Coefficient for red channel in luminance calculation (0.299)
- `LuminanceGreenCoefficient`: Coefficient for green channel in luminance calculation (0.587)
- `LuminanceBlueCoefficient`: Coefficient for blue channel in luminance calculation (0.114)
- `BrightnessThreshold`: Threshold value for determining if a color is light or dark (0.5)

### DirectoryNames

Directory names used for organizing content storage.

| Constant | Value | Description |
|----------|-------|-------------|
| `Data` | `"Data"` | Directory for content data |
| `Cache` | `"Cache"` | Directory for cache files |
| `CasPool` | `"cas-pool"` | Directory for CAS pool |
| `Temp` | `"Temp"` | Directory for temporary files |
| `Logs` | `"Logs"` | Directory for log files |
| `Backups` | `"Backups"` | Directory for backup files |

---

## DownloadDefaults Class

Default values and limits for download operations.

- `BufferSizeBytes`: 81920  
- `BufferSizeKB`: 80.0  
- `MinBufferSizeKB`: 4.0  
- `MaxBufferSizeKB`: 1024.0  
- `MaxConcurrentDownloads`: 3  
- `MaxRetryAttempts`: 3  
- `TimeoutSeconds`: 600  

---

## FileTypes Class

File and directory name constants to prevent typos and ensure consistency.

### Manifest Files

| Constant | Value | Description |
|----------|-------|-------------|
| `ManifestsDirectory` | `"Manifests"` | Directory for manifest files |
| `ManifestFilePattern` | `"*.manifest.json"` | File pattern for manifest files |
| `ManifestFileExtension` | `".manifest.json"` | File extension for manifest files |

### JSON Files

| Constant | Value | Description |
|----------|-------|-------------|
| `JsonFileExtension` | `".json"` | File extension for JSON files |
| `JsonFilePattern` | `"*.json"` | File pattern for JSON files |
| `SettingsFileName` | `"settings.json"` | Default settings file name |

---

## ManifestConstants Class

Constants related to manifest ID generation, validation, and file operations.

### Manifest ID Generation

| Constant | Value | Description |
|----------|-------|-------------|
| `DefaultManifestSchemaVersion` | `1` | Default manifest schema version |
| `PublisherContentIdPrefix` | `"publisher"` | Prefix for publisher content IDs |
| `BaseGameIdPrefix` | `"basegame"` | Prefix for base game IDs |
| `SimpleIdPrefix` | `"simple"` | Prefix for simple test IDs |

### Manifest Validation

| Constant | Value | Description |
|----------|-------|-------------|
| `MaxManifestIdLength` | `256` | Maximum length for manifest IDs |
| `MinManifestIdLength` | `3` | Minimum length for manifest IDs |
| `MaxManifestSegments` | `5` | Maximum number of segments in manifest ID |
| `MinManifestSegments` | `1` | Minimum number of segments in manifest ID |

### Manifest ID Regex Patterns

| Constant | Description |
|----------|-------------|
| `PublisherIdRegexPattern` | Regex for publisher content IDs |
| `GameInstallationIdRegexPattern` | Regex for base game IDs |
| `SimpleIdRegexPattern` | Regex for simple IDs |

**Publisher Content ID Pattern:**

```regex
^(?:[a-zA-Z0-9\-]+\.)+[a-zA-Z0-9\-]+$
```

---

## IoConstants Class

- `DefaultFileBufferSize`: 4096  

---

## ProcessConstants Class

Process and system constants.

### Exit Codes

- `ExitCodeSuccess`: 0

### Windows API Constants

- `SW_RESTORE`: 9  
- `SW_SHOW`: 5  
- `SW_MINIMIZE`: 6  
- `SW_MAXIMIZE`: 3  

---

## StorageConstants Class

Storage and CAS (Content-Addressable Storage) related constants.

### CAS Retry Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `MaxRetries` | `10` | Maximum retry attempts for CAS operations |
| `RetryDelayMs` | `100` | Delay between retry attempts (ms) |
| `MaxRetryDelayMs` | `5000` | Maximum delay for exponential backoff (ms) |

### CAS Directory Structure

| Constant | Value | Description |
|----------|-------|-------------|
| `ObjectsDirectory` | `"objects"` | Directory for CAS objects |
| `LocksDirectory` | `"locks"` | Directory for CAS locks |

### CAS Maintenance

- `AutoGcIntervalDays`: 1  

---

## TimeIntervals Class

- `UpdaterTimeout`: 10 minutes  
- `DownloadTimeout`: 30 minutes  
- `NotificationHideDelay`: 3000ms  

---

#### Status Colors

- `StatusSuccessColor`: Color used to indicate success or positive status (`"#4CAF50"`)
- `StatusErrorColor`: Color used to indicate error or negative status (`"#F44336"`)

### ValidationLimits

- `DefaultWindowWidth`: 1200  
- `DefaultWindowHeight`: 800  

---

## ValidationLimits Class

- `MinConcurrentDownloads`: 1  
- `MaxConcurrentDownloads`: 10  
- `MinDownloadTimeoutSeconds`: 30  
- `MaxDownloadTimeoutSeconds`: 3600  
- `MinDownloadBufferSizeBytes`: 4096  
- `MaxDownloadBufferSizeBytes`: 1048576  

---

## Usage Examples

### Application Configuration

```csharp
using GenHub.Core.Constants;

// Build user agent string
var userAgent = AppConstants.DefaultUserAgent; // "GenHub/1.0"

// Get application info
var appName = AppConstants.ApplicationName;
var version = AppConstants.Version;
```

### Directory Operations

```csharp
using GenHub.Core.Constants;

// Build standard directory paths
var dataPath = Path.Combine(basePath, DirectoryNames.Data);
var cachePath = Path.Combine(basePath, DirectoryNames.Cache);
var tempPath = Path.Combine(basePath, DirectoryNames.Temp);
var casPoolPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    AppConstants.ApplicationName,
    DirectoryNames.CasPool);
```

### File Type Validation

```csharp
using GenHub.Core.Constants;

// Check file types
if (fileName.EndsWith(FileTypes.ManifestFileExtension))
{
    // Handle manifest file
}
else if (fileName.EndsWith(FileTypes.JsonFileExtension))
{
    // Handle JSON file
}
```

### Manifest ID Operations

```csharp
using GenHub.Core.Constants;

// Generate publisher content ID
var publisherId = $"{ManifestConstants.PublisherContentIdPrefix}.{contentName}.{ManifestConstants.DefaultManifestSchemaVersion}";

// Validate manifest ID length
if (manifestId.Length < ManifestConstants.MinManifestIdLength ||
    manifestId.Length > ManifestConstants.MaxManifestIdLength)
{
    throw new ArgumentException("Manifest ID length is invalid");
}
```

### HTTP Operations with ApiConstants

```csharp
using var client = new HttpClient();
client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.DefaultUserAgent);
```

### GitHub URL Validation

```csharp
public bool IsGitHubUrl(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return false;

    return uri.Host.Equals(ApiConstants.GitHubDomain, StringComparison.OrdinalIgnoreCase);
}
```

### Download Configuration

```csharp
var downloadConfig = new DownloadConfiguration
{
    BufferSize = DownloadDefaults.BufferSizeBytes,
    MaxConcurrentDownloads = DownloadDefaults.MaxConcurrentDownloads,
    Timeout = TimeSpan.FromSeconds(DownloadDefaults.TimeoutSeconds),
    MaxRetryAttempts = DownloadDefaults.MaxRetryAttempts
};
```
### CAS Operations

```csharp
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

---

### UI Constants Example

```csharp
var window = new Window
{
    Width = UiConstants.DefaultWindowWidth,
    Height = UiConstants.DefaultWindowHeight
};

// Using status colors for consistent UI theming
var successBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusSuccessColor));
var errorBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusErrorColor));

// Example XAML data binding with BoolToStatusColorConverter
// <TextBlock Text="Status" Foreground="{Binding IsActive, Converter={StaticResource BoolToStatusColorConverter}}" />
```

---

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
public bool ValidateConcurrentDownloads(int value)
{
    return value >= ValidationLimits.MinConcurrentDownloads &&
           value <= ValidationLimits.MaxConcurrentDownloads;
}
```

### Time Intervals Usage

```csharp
var updaterTimeout = TimeIntervals.UpdaterTimeout;
var downloadTimeout = TimeIntervals.DownloadTimeout;
var notificationDelay = TimeIntervals.NotificationHideDelay;
```

---

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
- **ManifestConstants**: Manifest ID and validation constants  
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

---

## GameSettingsConstants Class

Constants for game settings management, including texture quality, resolution, volume levels, and folder names.

### TextureQuality Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `MaxQuality` | `2` | Maximum texture quality level |
| `MinQuality` | `0` | Minimum texture quality level |
| `DefaultQuality` | `1` | Default texture quality level |

### Resolution Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `MinWidth` | `640` | Minimum resolution width |
| `MinHeight` | `480` | Minimum resolution height |
| `MaxWidth` | `7680` | Maximum resolution width |
| `MaxHeight` | `4320` | Maximum resolution height |

### Volume Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `MinVolume` | `0.0f` | Minimum volume level |
| `MaxVolume` | `1.0f` | Maximum volume level |
| `DefaultVolume` | `0.5f` | Default volume level |

### Audio Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `MinAudioLevel` | `0` | Minimum audio level |
| `MaxAudioLevel` | `100` | Maximum audio level |
| `DefaultAudioLevel` | `50` | Default audio level |

### FolderNames Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `GameData` | `"GameData"` | Folder name for game data |
| `MyGames` | `"My Games"` | Folder name for user's games |
| `SavedGames` | `"Saved Games"` | Folder name for saved games |

### ResolutionPresets Constants

Predefined resolution options available in the game settings.

- `"640x480"`  
- `"800x600"`  
- `"1024x768"`  
- `"1024x768"`  
- `"1280x720"`  
- `"1280x1024"`  
- `"1366x768"`  
- `"1600x900"`  
- `"1920x1080"`  
- `"2560x1440"`  
- `"3840x2160"`  

---

## Related Documentation

- [Manifest ID System](manifest-id-system.md)  
- [Complete System Architecture](../architecture.md)
