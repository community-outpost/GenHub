# Constants Reference

This document provides documentation for constants used in the GenHub application. All constants are defined in static classes within the `GenHub.Core.Constants` namespace.

## Overview

GenHub uses centralized constants to ensure consistency across the application. Constants are organized into logical groups based on their functionality.

## AppConstants Class

Application-wide constants for GenHub.

| Constant | Value | Description |
|----------|-------|-------------|
| `Version` | `"1.0"` | Current version of GenHub |
| `ApplicationName` | `"GenHub"` | Application name |
| `DefaultTheme` | `Theme.Dark` | Default UI theme |
| `DefaultThemeName` | `"Dark"` | Default theme name as string |
| `DefaultUserAgent` | `"GenHub/1.0"` | Default user agent string |

## CasDefaults Class

Default values and limits for Content-Addressable Storage (CAS).

| Constant | Value | Description |
|----------|-------|-------------|
| `MaxCacheSizeBytes` | `53687091200` (50GB) | Default maximum cache size |
| `MaxConcurrentOperations` | `4` | Default maximum concurrent CAS operations |

## DirectoryNames Class

Standard directory names used for organizing content storage.

| Constant | Value | Description |
|----------|-------|-------------|
| `Data` | `"Data"` | Directory for content data |
| `Cache` | `"Cache"` | Directory for cache files |
| `Temp` | `"Temp"` | Directory for temporary files |
| `Logs` | `"Logs"` | Directory for log files |
| `Backups` | `"Backups"` | Directory for backup files |

## FileTypes Class

File and directory name constants for manifest operations.

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

## ManifestConstants Class

Constants related to manifest ID generation, validation, and file operations.

### Manifest ID Generation

| Constant | Value | Description |
|----------|-------|-------------|
| `DefaultManifestSchemaVersion` | `"1.0"` | Default manifest schema version |
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

**Base Game ID Pattern:**

```regex
^(unknown|steam|ea|eaapp|origin|thefirstdecade|rgmechanics|cdiso|wine|retail)\.(generals|zerohour)(?:\.\d+(?:\.\d+)*)?$
```

**Simple ID Pattern:**

```regex
^[a-zA-Z0-9\-\.]+$
```

### Service Configuration

| Constant | Value | Description |
|----------|-------|-------------|
| `ManifestIdGenerationTimeoutMs` | `5000` | Timeout for ID generation (ms) |
| `ManifestValidationTimeoutMs` | `1000` | Timeout for validation (ms) |
| `MaxConcurrentManifestOperations` | `10` | Maximum concurrent operations |

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

| Constant | Value | Description |
|----------|-------|-------------|
| `AutoGcIntervalDays` | `1` | Automatic garbage collection interval (days) |

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

### CAS Operations

```csharp
using GenHub.Core.Constants;

// CAS retry logic
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

## Related Documentation

- [Manifest ID System](manifest-id-system.md) - Complete guide to the manifest ID system
