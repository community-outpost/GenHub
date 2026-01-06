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

| Constant                  | Value/Type          | Description                                      |
| ------------------------- | ------------------- | ------------------------------------------------ |
| `AppName`                 | `"GenHub"`          | The name of the application                      |
| `AppVersion`              | Dynamic (lazy)      | Full semantic version from assembly              |
| `DisplayVersion`          | `"v" + AppVersion`  | Display version for UI                           |
| `GitShortHash`            | Dynamic             | Short git commit hash (7 chars)                  |
| `GitShortHashLength`      | `7`                 | Length of git short hash                         |
| `PullRequestNumber`       | Dynamic             | PR number if PR build                            |
| `BuildChannel`            | Dynamic             | Build channel (Dev, PR, CI, Release)             |
| `IsCiBuild`               | bool                | Whether this is a CI/CD build                    |
| `FullDisplayVersion`      | string              | Full display version with hash                   |
| `GitHubRepositoryUrl`     | `"https://github.com/community-outpost/GenHub"` | GitHub repository URL                            |
| `GitHubRepositoryOwner`   | `"community-outpost"`                           | GitHub repository owner                          |
| `GitHubRepositoryName`    | `"GenHub"`                                      | GitHub repository name                           |
| `DefaultTheme`            | `Theme.Dark`        | Default UI theme                                 |
| `DefaultThemeName`        | `"Dark"`            | Default theme name as string                     |
| `TokenFileName`           | `".ghtoken"`        | Default GitHub token file name                   |

---

## AppUpdateConstants Class

Constants related to application updates and Velopack.

| Constant                     | Value/Type                  | Description                                      |
| ---------------------------- | --------------------------- | ------------------------------------------------ |
| `PostUpdateExitDelay`        | `TimeSpan.FromSeconds(5)`   | Delay before exit after applying update          |
| `CacheDuration`              | `TimeSpan.FromHours(1)`     | Cache duration for update checks                 |
| `MaxHttpRetries`             | `3`                         | Maximum number of HTTP retries for failed requests |

---

## CasDefaults Class

Default values and limits for Content-Addressable Storage (CAS).

| Constant                  | Value                | Description                                     |
| ------------------------- | -------------------- | ----------------------------------------------- |
| `MaxCacheSizeBytes`       | `53687091200` (50GB) | Default maximum cache size                      |
| `DefaultMaxCacheSizeGB`   | `50`                 | Default maximum cache size in gigabytes         |
| `MaxConcurrentOperations` | `4`                  | Default maximum concurrent CAS operations       |
| `GcGracePeriodDays`       | `7`                  | Default garbage collection grace period in days |

---

## ConfigurationKeys Class

Configuration key constants for `appsettings.json` and environment variables.

### Workspace Configuration

- `WorkspaceDefaultPath`: `"GenHub:Workspace:DefaultPath"` - Default path for workspace creation
- `WorkspaceDefaultStrategy`: `"GenHub:Workspace:DefaultStrategy"` - Default workspace strategy (SymlinkOnly)

**Note**: The default workspace strategy is **SymlinkOnly** (enum value 0), which creates symbolic links to minimize disk usage. This requires administrator rights on Windows.

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

## WorkspaceConstants Class

Constants related to workspace management and configuration.

- `DefaultWorkspaceStrategy`: The default workspace strategy to use when none is specified (`WorkspaceStrategy.HardLink`)

---

## ConversionConstants Class

Constants for unit conversions used throughout the application.

- `BytesPerKilobyte`: 1024
- `BytesPerMegabyte`: 1048576
- `BytesPerGigabyte`: 1073741824

### Color Conversion Constants

- `LuminanceRedCoefficient`: Coefficient for red channel in luminance calculation (0.299)
- `LuminanceGreenCoefficient`: Coefficient for green channel in luminance calculation (0.587)
- `LuminanceBlueCoefficient`: Coefficient for blue channel in luminance calculation (0.114)
- `BrightnessThreshold`: Threshold value for determining if a color is light or dark (0.5)

### DirectoryNames

Directory names used for organizing content storage.

| Constant  | Value        | Description                   |
| --------- | ------------ | ----------------------------- |
| `Data`    | `"Data"`     | Directory for content data    |
| `Cache`   | `"Cache"`    | Directory for cache files     |
| `CasPool` | `"cas-pool"` | Directory for CAS pool        |
| `Temp`    | `"Temp"`     | Directory for temporary files |
| `Logs`    | `"Logs"`     | Directory for log files       |
| `Backups` | `"Backups"`  | Directory for backup files    |

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

| Constant                | Value               | Description                       |
| ----------------------- | ------------------- | --------------------------------- |
| `ManifestsDirectory`    | `"Manifests"`       | Directory for manifest files      |
| `ManifestFilePattern`   | `"*.manifest.json"` | File pattern for manifest files   |
| `ManifestFileExtension` | `".manifest.json"`  | File extension for manifest files |

### JSON Files

| Constant            | Value             | Description                   |
| ------------------- | ----------------- | ----------------------------- |
| `JsonFileExtension` | `".json"`         | File extension for JSON files |
| `JsonFilePattern`   | `"*.json"`        | File pattern for JSON files   |
| `SettingsFileName`  | `"settings.json"` | Default settings file name    |

---

## ManifestConstants Class

Constants related to manifest ID generation, validation, and file operations.

### Manifest ID Generation

| Constant                       | Value                | Description                                                                           |
| ------------------------------ | -------------------- | ------------------------------------------------------------------------------------- |
| `DefaultManifestFormatVersion` | `1`                  | Default manifest format version (integer)                                             |
| `DefaultManifestVersion`       | `"1.0"`              | Default manifest version as string                                                    |
| `PublisherContentIdPrefix`     | `"publisher"`        | Prefix for publisher content IDs                                                      |
| `BaseGameIdPrefix`             | `"gameinstallation"` | Prefix for game installation IDs                                                      |
| `SimpleIdPrefix`               | `"simple"`           | Prefix for simple test IDs                                                            |
| `GeneralsManifestVersion`      | `"1.08"`             | Version string for Generals game installation manifests (dots removed in IDs: "108")  |
| `ZeroHourManifestVersion`      | `"1.04"`             | Version string for Zero Hour game installation manifests (dots removed in IDs: "104") |

### Manifest Validation

| Constant              | Value | Description                               |
| --------------------- | ----- | ----------------------------------------- |
| `MaxManifestIdLength` | `256` | Maximum length for manifest IDs           |
| `MinManifestIdLength` | `3`   | Minimum length for manifest IDs           |
| `MaxManifestSegments` | `5`   | Maximum number of segments in manifest ID |

### Manifest Timeouts and Operations

| Constant                          | Value  | Description                                                  |
| --------------------------------- | ------ | ------------------------------------------------------------ |
| `ManifestIdGenerationTimeoutMs`   | `5000` | Timeout for manifest ID generation operations (milliseconds) |
| `ManifestValidationTimeoutMs`     | `1000` | Timeout for manifest validation operations (milliseconds)    |
| `MaxConcurrentManifestOperations` | `10`   | Maximum concurrent manifest operations                       |

### Manifest ID Regex Patterns

| Constant                         | Description                     |
| -------------------------------- | ------------------------------- |
| `PublisherContentRegexPattern` | Regex for validating 5-segment publisher content IDs (schemaVersion.userVersion.publisher.contentType.contentName) |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------ |

**Publisher Content Regex Pattern (5-segment format):**

```regex
^\d+\.\d+\.[a-z0-9]+\.(gameinstallation|gameclient|mod|patch|addon|mappack|languagepack|contentbundle|publisherreferral|contentreferral|mission|map|unknown)\.[a-z0-9-]+$
```

**Pattern Explanation:**

- **Publisher Content Pattern**: Validates the standard 5-segment format used for ALL content in GenHub
  - Segment 1: Schema version (digits only)
  - Segment 2: User version (digits only)
  - Segment 3: Publisher (lowercase alphanumeric)
  - Segment 4: Content type (enumerated values like gameinstallation, mod, etc.)
  - Segment 5: Content name (lowercase alphanumeric with dashes)

**Note**: The SimpleIdRegex pattern has been removed. All manifest IDs must now use the strict 5-segment format. The `MinManifestSegments` constant is now set to 5 (previously 1).

### Dependency Defaults

| Constant                     | Value                                | Description                                                                                                            |
| ---------------------------- | ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------- |
| `GeneralsManifestVersion`    | `"1.08"`                             | Version string for Generals game installation manifests. When used in manifest IDs, dots are removed (becomes "108").  |
| `ZeroHourManifestVersion`    | `"1.04"`                             | Version string for Zero Hour game installation manifests. When used in manifest IDs, dots are removed (becomes "104"). |
| `DefaultContentDependencyId` | `"1.0.genhub.mod.defaultdependency"` | Default ID string for content dependencies (fallback for model instantiation)                                          |

---

## GameClientHashRegistry Class

Extensible SHA-256 hash constants and registry for known game executables used for client detection across official and 3rd party distributions. Supports dynamic updates, external hash databases, and plugin extensibility.

### Core Hash Constants

| Constant          | Value                                                                | Description                                               |
| ----------------- | -------------------------------------------------------------------- | --------------------------------------------------------- |
| `Generals108Hash` | `"1c96366ff6a99f40863f6bbcfa8bf7622e8df1f80a474201e0e95e37c6416255"` | SHA-256 hash for Generals 1.08 executable (generals.exe)  |
| `ZeroHour104Hash` | `"f37a4929f8d697104e99c2bcf46f8d833122c943afcd87fd077df641d344495b"` | SHA-256 hash for Zero Hour 1.04 executable (generals.exe) |
| `ZeroHour105Hash` | `"420fba1dbdc4c14e2418c2b0d3010b9fac6f314eafa1f3a101805b8d98883ea1"` | SHA-256 hash for Zero Hour 1.05 executable (generals.exe) |

### Extensibility Configuration Constants

| Constant                            | Value                           | Description                                                    |
| ----------------------------------- | ------------------------------- | -------------------------------------------------------------- |
| `ExternalHashDatabaseFileName`      | `"game-executable-hashes.json"` | Default filename for external hash database JSON file          |
| `MaxExternalHashSources`            | `50`                            | Maximum number of external hash sources that can be registered |
| `ExternalSourceCacheTimeoutMinutes` | `30`                            | Cache timeout for external hash sources in minutes             |

### Collections and Properties

| Property                  | Type           | Description                                                                       |
| ------------------------- | -------------- | --------------------------------------------------------------------------------- |
| `PossibleExecutableNames` | `List<string>` | Executable file names that might contain game executables (extensible at runtime) |

### Basic Usage Example

```csharp
using GenHub.Core.Constants;

// Basic hash detection (backward compatible)
if (computedHash == GameClientHashRegistry.Generals108Hash)
{
    // Detected Generals 1.08
}
else if (computedHash == GameClientHashRegistry.ZeroHour104Hash)
{
    // Detected Zero Hour 1.04
}

var info = GameClientHashRegistry.GetGameExecutableInfo(computedHash);
if (info.HasValue)
{
    Console.WriteLine($"Detected: {info.Value.GameType} {info.Value.Version} ({info.Value.Publisher})");
}
```

### Extensibility Usage

```csharp
using GenHub.Core.Constants;

// Add runtime hash for 3rd party client
GameClientHashRegistry.AddKnownHash(
    "abc123...",
    GameType.Generals,
    "1.09",
    "CommunityPatch",
    "Community-enhanced Generals executable",
    false);

// Add custom executable name
GameClientHashRegistry.AddPossibleExecutableName("my-modded-generals.exe");
```

---

## PublisherTypeConstants Class

Well-known publisher type identifiers for content sources. Uses lowercase string identifiers for consistency with ManifestId system.

### Important Note

`PublisherTypeConstants` is **NOT an enum** - publishers are string-based for extensibility. Any string value is valid; these constants are just common values for convenience. This follows the same pattern as ManifestId (string-based with validation).

### Official Platform Publishers

| Constant         | Value              | Description                       |
| ---------------- | ------------------ | --------------------------------- |
| `EaApp`          | `"eaapp"`          | EA App (formerly Origin) platform |
| `Steam`          | `"steam"`          | Steam platform                    |
| `Retail`         | `"retail"`         | Retail/physical installation      |
| `TheFirstDecade` | `"thefirstdecade"` | The First Decade compilation      |
| `Wine`           | `"wine"`           | Wine/Proton compatibility layer   |
| `CdIso`          | `"cdiso"`          | CD-ROM/ISO installation           |

### Community Platforms

| Constant           | Value                | Description                        |
| ------------------ | -------------------- | ---------------------------------- |
| `GeneralsOnline`   | `"generalsonline"`   | Generals Online community platform |
| `CommunityOutpost` | `"communityoutpost"` | Community Outpost platform         |
| `ModDb`            | `"moddb"`            | ModDB hosting platform             |
| `CncLabs`          | `"cnclabs"`          | C&C Labs community site            |

### Web/Download Sources

| Constant      | Value      | Description          |
| ------------- | ---------- | -------------------- |
| `GitHub`      | `"github"` | GitHub repository    |
| `WebDownload` | `"web"`    | Generic web download |

### Local/System Sources

| Constant      | Value          | Description               |
| ------------- | -------------- | ------------------------- |
| `LocalImport` | `"local"`      | Local file import by user |
| `FileSystem`  | `"filesystem"` | Imported from file system |

### Generated Content

| Constant         | Value             | Description                                     |
| ---------------- | ----------------- | ----------------------------------------------- |
| `AutoGenerated`  | `"autogenerated"` | Auto-generated by ContentOrchestrator           |
| `GenHubInternal` | `"genhub"`        | GenHub internal system content                  |
| `CsvGenerated`   | `"csvgenerated"`  | Content generated from CSV authoritative source |

### Special/Unknown

| Constant  | Value       | Description                           |
| --------- | ----------- | ------------------------------------- |
| `Unknown` | `"unknown"` | Unknown or unspecified publisher type |
| `Custom`  | `"custom"`  | Custom user-defined publisher         |

#### Publisher Type Mapping Methods

**`FromInstallationType(GameInstallationType)`**: Maps GameInstallationType enum to publisher type string by delegating to the centralized extension method:

```csharp
public static string FromInstallationType(GameInstallationType installationType)
{
    return installationType.ToPublisherTypeString();
}
```

**Note**: This method now uses the centralized `ToPublisherTypeString()` extension method from `InstallationExtensions.cs` to eliminate code duplication and ensure consistent mapping behavior across the codebase.

### Publisher Type Usage Examples

```csharp
using GenHub.Core.Constants;

// Using platform-specific publisher types
var steamPublisher = PublisherTypeConstants.Steam; // "steam"
var eaAppPublisher = PublisherTypeConstants.EaApp; // "eaapp"

// Using community publisher types
var generalsOnlinePublisher = PublisherTypeConstants.GeneralsOnline; // "generalsonline"

// Mapping installation type to publisher
var installationType = GameInstallationType.Steam;
var publisherType = PublisherTypeConstants.FromInstallationType(installationType);
// Result: "steam"

// In manifest generation (GeneralsOnline example)
var manifest = new ContentManifest
{
    Publisher = new PublisherInfo
    {
        Name = PublisherTypeConstants.GeneralsOnline,
        Website = "https://www.playgenerals.online/",
    }
};

// Using publisher types for content filtering
if (manifest.Publisher?.Name == PublisherTypeConstants.GeneralsOnline)
{
    // Handle GeneralsOnline-specific content
}

// Custom publisher type (not a predefined constant)
var customPublisher = "my-custom-publisher";
// Custom publishers work just like predefined constants
```

### GeneralsOnline Publisher Type

The `GeneralsOnline` publisher type is used for the GeneralsOnline community launcher, which provides auto-updated clients for Command & Conquer Generals and Zero Hour.

**Usage in Game Client Detection**:

- When GeneralsOnline executables are detected (generalsonline_30hz.exe, generalsonline_60hz.exe, generalsonline.exe)
- Manifests are generated with PublisherType = "generalsonline"
- UI displays these clients with appropriate publisher attribution
- Users can select GeneralsOnline variants in game profiles

**Manifest ID Examples**:

- `1.0.generalsonline.gameclient.generalsonline_30hz` (GeneralsOnline 30Hz client)
- `1.0.generalsonline.gameclient.generalsonline_60hz` (GeneralsOnline 60Hz client)

See also: [Manifest ID System Documentation](manifest-id-system.md) for complete ID format details.

---

## ProviderEndpointConstants Class

Constants for provider endpoint names and keys used in JSON serialization and lookup.

| Constant           | Value                | Description                                      |
| ------------------ | -------------------- | ------------------------------------------------ |
| `CatalogUrl`       | `"catalogUrl"`       | Key/Property name for catalog URL                |
| `DownloadBaseUrl`  | `"downloadBaseUrl"`  | Key/Property name for download base URL          |
| `WebsiteUrl`       | `"websiteUrl"`       | Key/Property name for website URL                |
| `SupportUrl`       | `"supportUrl"`       | Key/Property name for support URL                |
| `LatestVersionUrl` | `"latestVersionUrl"` | Key/Property name for latest version URL         |
| `ManifestApiUrl`   | `"manifestApiUrl"`   | Key/Property name for manifest API URL           |
| `Catalog`          | `"catalog"`          | Short name/alias for catalog URL                 |
| `DownloadBase`     | `"downloadBase"`     | Short name/alias for download base URL           |
| `Website`          | `"website"`          | Short name/alias for website URL                 |
| `Support`          | `"support"`          | Short name/alias for support URL                 |
| `LatestVersion`    | `"latestVersion"`    | Short name/alias for latest version URL          |
| `ManifestApi`      | `"manifestApi"`      | Short name/alias for manifest API URL            |

---

## PublisherInfoConstants Class

Constants for publisher information including display names, websites, and support URLs.
These constants provide standardized publisher metadata for content attribution and user interface display.

### Steam Publisher Information

| Constant     | Value                              | Description                      |
| ------------ | ---------------------------------- | -------------------------------- |
| `Name`       | `"Steam"`                          | Display name for Steam publisher |
| `Website`    | `"https://store.steampowered.com"` | Website URL for Steam            |
| `SupportUrl` | `"https://help.steampowered.com"`  | Support URL for Steam            |

### EA App Publisher Information

| Constant     | Value                   | Description                       |
| ------------ | ----------------------- | --------------------------------- |
| `Name`       | `"EA App"`              | Display name for EA App publisher |
| `Website`    | `"https://www.ea.com"`  | Website URL for EA App            |
| `SupportUrl` | `"https://help.ea.com"` | Support URL for EA App            |

### The First Decade Publisher Information

| Constant     | Value                    | Description                                 |
| ------------ | ------------------------ | ------------------------------------------- |
| `Name`       | `"The First Decade"`     | Display name for The First Decade publisher |
| `Website`    | `"https://westwood.com"` | Website URL for The First Decade            |
| `SupportUrl` | `""`                     | Support URL for The First Decade (empty)    |

### Wine/Proton Publisher Information

| Constant     | Value           | Description                            |
| ------------ | --------------- | -------------------------------------- |
| `Name`       | `"Wine/Proton"` | Display name for Wine/Proton publisher |
| `Website`    | `""`            | Website URL for Wine/Proton (empty)    |
| `SupportUrl` | `""`            | Support URL for Wine/Proton (empty)    |

### CD-ROM Publisher Information

| Constant     | Value      | Description                       |
| ------------ | ---------- | --------------------------------- |
| `Name`       | `"CD-ROM"` | Display name for CD-ROM publisher |
| `Website`    | `""`       | Website URL for CD-ROM (empty)    |
| `SupportUrl` | `""`       | Support URL for CD-ROM (empty)    |

### Retail Publisher Information

| Constant     | Value                   | Description                       |
| ------------ | ----------------------- | --------------------------------- |
| `Name`       | `"Retail Installation"` | Display name for retail publisher |
| `Website`    | `""`                    | Website URL for retail (empty)    |
| `SupportUrl` | `""`                    | Support URL for retail (empty)    |

### Generals Online Publisher Information

| Constant     | Value                                       | Description                                |
| ------------ | ------------------------------------------- | ------------------------------------------ |
| `Name`       | `"Generals Online"`                         | Display name for Generals Online publisher |
| `Website`    | `"https://www.playgenerals.online/"`        | Website URL for Generals Online            |
| `SupportUrl` | `"https://www.playgenerals.online/support"` | Support URL for Generals Online            |

### Helper Methods

**`GetPublisherInfo(GameInstallationType)`**: Returns a tuple containing (Name, Website, SupportUrl) for the specified installation type.

```csharp
// Example usage
var (name, website, supportUrl) = PublisherInfoConstants.GetPublisherInfo(GameInstallationType.Steam);
// Result: ("Steam", "https://store.steampowered.com", "https://help.steampowered.com")
```

---

## GameClientConstants Class

Constants related to game client detection and management.

### Game Executables

| Constant             | Value            | Description                   |
| -------------------- | ---------------- | ----------------------------- |
| `GeneralsExecutable` | `"generals.exe"` | Generals executable filename  |
| `ZeroHourExecutable` | `"game.exe"`     | Zero Hour executable filename |

### SuperHackers Client Detection

| Constant                         | Value              | Description                                |
| -------------------------------- | ------------------ | ------------------------------------------ |
| `SuperHackersGeneralsExecutable` | `"generalsV.exe"`  | SuperHackers Generals executable filename  |
| `SuperHackersZeroHourExecutable` | `"generalsZH.exe"` | SuperHackers Zero Hour executable filename |

### Game Directory Names

| Constant                           | Value                                      | Description                                    |
| ---------------------------------- | ------------------------------------------ | ---------------------------------------------- |
| `GeneralsDirectoryName`                | `"Command and Conquer Generals"`            | Standard Generals installation directory name |
| `ZeroHourDirectoryName`                | `"Command and Conquer Generals Zero Hour"`  | Standard Zero Hour installation directory name |
| `ZeroHourDirectoryNameAmpersandHyphen` | `"Command & Conquer Generals - Zero Hour"`  | Zero Hour directory name with ampersand and hyphen (Steam standard) |
| `ZeroHourDirectoryNameColonVariant`    | `"Command & Conquer: Generals - Zero Hour"` | Zero Hour directory name with colon variant |
| `ZeroHourDirectoryNameAbbreviated`     | `"C&C Generals Zero Hour"`                  | Zero Hour directory name abbreviated form |

### GeneralsOnline Client Detection

| Constant                           | Value                       | Description                                      |
| ---------------------------------- | --------------------------- | ------------------------------------------------ |
| `GeneralsOnline30HzExecutable`     | `"generalsonlinezh_30.exe"` | GeneralsOnline 30Hz client executable name       |
| `GeneralsOnline60HzExecutable`     | `"generalsonlinezh_60.exe"` | GeneralsOnline 60Hz client executable name       |
| `GeneralsOnline30HzDisplayName`    | `"GeneralsOnline 30Hz"`     | Display name for GeneralsOnline 30Hz variant     |
| `GeneralsOnline60HzDisplayName`    | `"GeneralsOnline 60Hz"`     | Display name for GeneralsOnline 60Hz variant     |
| `GeneralsOnlineDefaultDisplayName` | `"GeneralsOnline 30Hz"`     | Default display name for GeneralsOnline variants |

### Dependency Names

| Constant                             | Value                                 | Description                                            |
| ------------------------------------ | ------------------------------------- | ------------------------------------------------------ |
| `ZeroHourInstallationDependencyName` | `"Zero Hour Installation (Required)"` | Name for Zero Hour installation dependency requirement |

### Version Strings

| Constant              | Value                   | Description                                            |
| --------------------- | ----------------------- | ------------------------------------------------------ |
| `AutoDetectedVersion` | `"Automatically added"` | Version string used for automatically detected clients |
| `UnknownVersion`      | `"Unknown"`             | Version string used for unknown/unrecognized clients   |

### Required DLLs

- `RequiredDlls`: Array of DLLs required for standard game installations
  - `"steam_api.dll"` - Steam integration
  - `"binkw32.dll"` - Bink video codec
  - `"mss32.dll"` - Miles Sound System
  - `"eauninstall.dll"` - EA App integration

### GeneralsOnline DLLs

- `GeneralsOnlineDlls`: Array of DLLs specific to GeneralsOnline installations
  - Core runtime DLLs: `"abseil_dll.dll"`, `"GameNetworkingSockets.dll"`, `"libcrypto-3.dll"`, `"libcurl.dll"`, `"libprotobuf.dll"`, `"libssl-3.dll"`, `"sentry.dll"`, `"zlib1.dll"`
  - Legacy game DLLs: `"steam_api.dll"`, `"binkw32.dll"`, `"mss32.dll"`, `"wsock32.dll"`

### Configuration Files

- `ConfigFiles`: Array of configuration files used by game installations
  - `"options.ini"` - Legacy game options
  - `"skirmish.ini"` - Skirmish settings
  - `"network.ini"` - Network configuration

### Executable Names List

- `GeneralsOnlineExecutableNames`: Read-only list of executable names for GeneralsOnline clients that should be detected
  - `GeneralsOnline30HzExecutable`
  - `GeneralsOnline60HzExecutable`
  - `GeneralsOnlineDefaultExecutable`

---

## GameClientName Enum

Enum for game client display names used in UI formatting and content display.

### Enum Values

| Value      | Description                          |
| ---------- | ------------------------------------ |
| `Generals` | Command & Conquer: Generals           |
| `ZeroHour` | Command & Conquer: Generals Zero Hour |

### Extension Methods

The `GameClientNameExtensions` class provides display name methods:

#### `GetShortName()`

Returns abbreviated display names for compact UI display.

- `GameClientName.Generals.GetShortName()` → `"Generals"`
- `GameClientName.ZeroHour.GetShortName()` → `"Zero Hour"`

#### `GetFullName()`

Returns full game titles for detailed display.

- `GameClientName.Generals.GetFullName()` → `"Command &amp; Conquer: Generals"`
- `GameClientName.ZeroHour.GetFullName()` → `"Command &amp; Conquer: Generals Zero Hour"`

### Usage in ContentDisplayFormatter

The enum is used in `ContentDisplayFormatter.GetGameTypeDisplayName()` to provide consistent game naming across the UI:

```csharp
public string GetGameTypeDisplayName(GameType gameType, bool useShortName = false)
{
    if (useShortName)
    {
        return gameType switch
        {
            GameType.ZeroHour => GameClientName.ZeroHour.GetShortName(),
            GameType.Generals => GameClientName.Generals.GetShortName(),
            _ => gameType.ToString(),
        };
    }

    return gameType switch
    {
        GameType.Generals => GameClientName.Generals.GetFullName(),
        GameType.ZeroHour => GameClientName.ZeroHour.GetFullName(),
        _ => gameType.ToString(),
    };
}
```

This ensures type-safe game name handling and prevents typos in display strings.

---

Installation source type identifiers for game installations. These constants represent WHERE the game was installed from (Steam, EA App, Retail, etc.).

**Content Provider Discovery**:

- Content providers register themselves with `ContentOrchestrator` via dependency injection
- Each provider implements `IContentProvider` with a unique `SourceName` property
- Providers can be: Official platforms (EA/Steam), Community sources (GitHub, ModDB, HTTP), Custom sources (any implementation)
- To add a new publisher: Create an `IContentProvider` implementation and register it in DI (see `ContentPipelineModule.cs`)

**Examples of IContentProvider Implementations**:

- `GitHubContentProvider` (SourceName: "GitHub")
- `ModDBContentProvider` (SourceName: "ModDB")
- `LocalFileSystemContentProvider` (SourceName: "Local Files")
- `CNCLabsContentProvider` (SourceName: "C&C Labs")

See [Content Pipeline Architecture](../architecture.md#content-pipeline) for details on dynamic provider registration.

### Installation Source Constants

These constants are used for `GameInstallationType` mapping only:

| Constant         | Value              | Description                                    |
| ---------------- | ------------------ | ---------------------------------------------- |
| `EaApp`          | `"eaapp"`          | EA App (formerly Origin) platform installation |
| `Steam`          | `"steam"`          | Steam platform installation                    |
| `Retail`         | `"retail"`         | Retail/physical installation                   |
| `TheFirstDecade` | `"thefirstdecade"` | The First Decade compilation installation      |
| `Wine`           | `"wine"`           | Wine/Proton compatibility layer installation   |
| `CdIso`          | `"cdiso"`          | CD-ROM/ISO installation                        |
| `Unknown`        | `"unknown"`        | Unknown or unspecified installation source     |

#### Installation Source Mapping Methods

**`FromInstallationType(GameInstallationType)`**: Maps GameInstallationType enum to installation source string by delegating to the centralized extension method:

```csharp
public static string FromInstallationType(GameInstallationType installationType)
{
    return installationType.ToInstallationSourceString();
}
```

**Note**: This method now uses the centralized `ToInstallationSourceString()` extension method from `InstallationExtensions.cs` to eliminate code duplication and ensure consistent mapping behavior across the codebase.

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

| Constant          | Value  | Description                                |
| ----------------- | ------ | ------------------------------------------ |
| `MaxRetries`      | `10`   | Maximum retry attempts for CAS operations  |
| `RetryDelayMs`    | `100`  | Delay between retry attempts (ms)          |
| `MaxRetryDelayMs` | `5000` | Maximum delay for exponential backoff (ms) |

### CAS Directory Structure

| Constant           | Value       | Description               |
| ------------------ | ----------- | ------------------------- |
| `ObjectsDirectory` | `"objects"` | Directory for CAS objects |
| `LocksDirectory`   | `"locks"`   | Directory for CAS locks   |

### CAS Maintenance

- `AutoGcIntervalDays`: 1

---

## TimeIntervals Class

- `UpdaterTimeout`: 10 minutes
- `DownloadTimeout`: 30 minutes
- `NotificationHideDelay`: 3000ms

---

### Status Colors

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

---

## Configuration and Usage Examples

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

// All manifest IDs now use 5-segment format: schemaVersion.userVersion.publisher.contentType.contentName

// Generate installation ID with version normalization (5 segments)
var installId = $"{ManifestConstants.DefaultManifestFormatVersion}.{ManifestConstants.GeneralsManifestVersion.Replace(".", "")}.steam.gameinstallation.generals";
// Result: "1.108.steam.gameinstallation.generals" (dots removed from "1.08" → "108")

// Generate client ID (5 segments)
var clientId = $"{ManifestConstants.DefaultManifestFormatVersion}.{ManifestConstants.ZeroHourManifestVersion.Replace(".", "")}.steam.gameclient.zerohour";
// Result: "1.104.steam.gameclient.zerohour" (dots removed from "1.04" → "104")

// Generate mod ID (5 segments)
var modId = $"{ManifestConstants.DefaultManifestFormatVersion}.1.{ManifestConstants.GenHubPublisher}.mod.rising-sun-mod";
// "1.1.genhub.mod.rising-sun-mod"

// Use manifest versions in manifest generation
var generalsVersion = ManifestConstants.GeneralsManifestVersion; // "1.08"
var zeroHourVersion = ManifestConstants.ZeroHourManifestVersion; // "1.04"

// Example: await manifestService.CreateGameInstallationManifestAsync(path, GameType.Generals, type, generalsVersion);
// The service will automatically normalize "1.08" → "108" and generate a 5-segment ID

// Validate manifest ID length
if (manifestId.Length < ManifestConstants.MinManifestIdLength ||
    manifestId.Length > ManifestConstants.MaxManifestIdLength)
{
    throw new ArgumentException("Manifest ID length is invalid");
}

// Use PublisherTypeConstants for publisher types
var publisherType = PublisherTypeConstants.Steam;
var unknownType = PublisherTypeConstants.Unknown;

// Map installation type to publisher type
var publisher = PublisherTypeConstants.FromInstallationType(GameInstallationType.Steam);
// Result: "steam"
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

## ContentSourceNames Class

Constants for content pipeline component identifiers used in dependency injection lookups.

### Discoverers

| Constant                   | Value                 | Description                                  |
| -------------------------- | --------------------- | -------------------------------------------- |
| `CNCLabsDiscoverer`        | `"CNC Labs Maps"`     | Source name for CNC Labs map discoverer      |
| `GitHubDiscoverer`         | `"GitHub"`            | Source name for GitHub content discoverer    |
| `GitHubReleasesDiscoverer` | `"GitHub Releases"`   | Source name for GitHub releases discoverer   |
| `FileSystemDiscoverer`     | `"Local File System"` | Source name for local file system discoverer |
| `ModDBDiscoverer`          | `"ModDB"`             | Source name for ModDB content discoverer     |

### Resolvers

| Constant            | Value             | Description                             |
| ------------------- | ----------------- | --------------------------------------- |
| `CNCLabsResolverId` | `"CNCLabsMap"`    | Resolver ID for CNC Labs map resolver   |
| `GitHubResolverId`  | `"GitHubRelease"` | Resolver ID for GitHub release resolver |
| `LocalResolverId`   | `"LocalManifest"` | Resolver ID for local manifest resolver |
| `ModDBResolverId`   | `"ModDB"`         | Resolver ID for ModDB resolver          |

### Deliverers

| Constant              | Value                           | Description                                 |
| --------------------- | ------------------------------- | ------------------------------------------- |
| `HttpDeliverer`       | `"HTTP Content Deliverer"`      | Source name for HTTP content deliverer      |
| `FileSystemDeliverer` | `"Local File System Deliverer"` | Source name for local file system deliverer |

---

## MaintenanceWhen adding new constants

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
- **PublisherInfoConstants**: Publisher display names, websites, and support URLs
- **PublisherTypeConstants**: Publisher type identifiers for content sources
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

| Constant         | Value | Description                   |
| ---------------- | ----- | ----------------------------- |
| `MaxQuality`     | `2`   | Maximum texture quality level |
| `MinQuality`     | `0`   | Minimum texture quality level |
| `DefaultQuality` | `1`   | Default texture quality level |

### Resolution Constants

| Constant    | Value  | Description               |
| ----------- | ------ | ------------------------- |
| `MinWidth`  | `640`  | Minimum resolution width  |
| `MinHeight` | `480`  | Minimum resolution height |
| `MaxWidth`  | `7680` | Maximum resolution width  |
| `MaxHeight` | `4320` | Maximum resolution height |

### Volume Constants

| Constant        | Value  | Description          |
| --------------- | ------ | -------------------- |
| `MinVolume`     | `0.0f` | Minimum volume level |
| `MaxVolume`     | `1.0f` | Maximum volume level |
| `DefaultVolume` | `0.5f` | Default volume level |

### Audio Constants

| Constant            | Value | Description         |
| ------------------- | ----- | ------------------- |
| `MinAudioLevel`     | `0`   | Minimum audio level |
| `MaxAudioLevel`     | `100` | Maximum audio level |
| `DefaultAudioLevel` | `50`  | Default audio level |

### FolderNames Constants

| Constant     | Value           | Description                  |
| ------------ | --------------- | ---------------------------- |
| `GameData`   | `"GameData"`    | Folder name for game data    |
| `MyGames`    | `"My Games"`    | Folder name for user's games |
| `SavedGames` | `"Saved Games"` | Folder name for saved games  |

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

## Content Provider Constants

Constants for various community content providers and manifest generation.

### CommunityOutpostCatalogConstants Class

Constants related to the Community Outpost (GenPatcher) catalog and metadata.

- `CatalogFilename`: Default filename for the GenPatcher catalog (`"GenPatcher.dat"`)
- `VersionKey`: Metadata key for version information (`"Version"`)
- `DescriptionKey`: Metadata key for description information (`"Description"`)
- `DownloadUrlKey`: Metadata key for download URLs (`"DownloadUrl"`)

### GeneralsOnlineConstants Class

Constants for Generals Online content discovery and manifest creation.

- `PublisherPrefix`: Publisher prefix string (`"generalsonline"`)
- `PublisherId`: Publisher identifier (`"generals-online"`)
- `PublisherDisplayName`: Display name for the publisher (`"Generals Online"`)
- `QfeMarkerPrefix`: Prefix used for QFE (Quick Fix Engineering) versions (`"qfe-"`)
- `MapPackTags`: Default tags for MapPack manifests (`["mappack", "generalsonline"]`)
- `UnknownVersion`: Default version string when unknown (`"unknown"`)
- `CoverSource`: Default path for cover images (`"/Assets/Covers/zerohour-cover.png"`)

### CNCLabsConstants Class

Constants for CNC Labs (CNC Maps) content discovery and manifest creation.

- `PublisherPrefix`: Publisher prefix string (`"cnclabs"`)
- `PublisherId`: Publisher identifier (`"cnc-labs"`)
- `PublisherName`: Display name for the publisher (`"CNC Labs"`)
- `PublisherWebsite`: Main website URL (`"https://www.cnclabs.com"`)
- `DefaultTags`: Default tags for CNC Labs manifests (`["cnclabs"]`)
- `DefaultDownloadFilename`: Default filename for downloads when parsing fails (`"download.zip"`)

### ModDBConstants Class

Constants for ModDB content discovery and manifest creation.

- `PublisherPrefix`: Publisher prefix string (`"moddb"`)
- `PublisherDisplayName`: Display name for the publisher (`"ModDB"`)
- `PublisherWebsite`: Main website URL (`"https://www.moddb.com"`)
- `ReleaseDateFormat`: Date format used in ModDB metadata (`"MMMM dd, yyyy"`)
- `PublisherNameFormat`: Format string for including the author with the publisher name (`"ModDB ({0})"`)
- `DefaultDownloadFilename`: Default filename for downloads when parsing fails (`"download.zip"`)

### SuperHackersConstants Class

Constants for The Super Hackers content discovery and manifest creation.

- `PublisherPrefix`: Publisher prefix string (`"thesuperhackers"`)
- `PublisherDisplayName`: Display name for the publisher (`"The Super Hackers"`)
- `VersionDelimiter`: Character used to separate components in version strings (`':'`)

---

## Related Documentation

- [Manifest ID System](manifest-id-system.md)
- [Complete System Architecture](../architecture.md)
