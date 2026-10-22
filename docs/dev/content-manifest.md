# ContentManifest API Reference

**Version**: 1.0
**Last Updated**: 2026-03-15

## Overview

The `ContentManifest` is the core data structure in GenHub's content pipeline, serving as a complete installation blueprint for mods, maps, addons, and other game content. It bridges the gap between content discovery (lightweight metadata) and installation (complete file and dependency information).

### Purpose

- **Installation Blueprint**: Contains all information needed to install content (files, dependencies, metadata)
- **Content-Addressable Storage**: Files referenced by SHA256 hash for deduplication and integrity
- **Dependency Management**: Declares runtime dependencies with version constraints and install behaviors
- **Provider Abstraction**: Unified format for content from any source (catalogs, ModDB, CNCLabs, GitHub)
- **Workspace Integration**: Supports multiple installation strategies (symlink, copy, hardlink)

### Lifecycle

```
Discovery Phase (ContentSearchResult)
  ↓
Resolution Phase (ContentManifest created)
  ↓
Installation Phase (Files downloaded, stored in CAS)
  ↓
Manifest Pool (Available for game profiles)
  ↓
Profile Launch (Files mapped to game directory)
```

### Key Concepts

- **Manifest ID**: Unique identifier format: `{version}.{publisherId}.{contentType}.{contentId}`
- **Content-Addressable Storage (CAS)**: Files stored by SHA256 hash, enabling deduplication
- **Source Types**: Archive (zip/rar), Direct (individual files), CAS (already in storage)
- **Install Targets**: Data folder, game root, or custom paths
- **Dependency Types**: Required, Optional, Recommended, Conflicting

---

## ContentManifest Class

**Namespace**: `GenHub.Core.Models.Manifest`
**File**: `GenHub.Core/Models/Manifest/ContentManifest.cs`

### Properties

#### Identity & Versioning

```csharp
public string ManifestId { get; set; }
```

Unique identifier for this manifest. Format: `{version}.{publisherId}.{contentType}.{contentId}`
Example: `1.0.shockwave.mod.shockwave-chaos-edition`

```csharp
public string Name { get; set; }
```

Human-readable name of the content.
Example: `"Shockwave Chaos Edition"`

```csharp
public string Version { get; set; }
```

Semantic version string.
Example: `"1.2.3"`, `"2.0.0-beta.1"`

```csharp
public string Description { get; set; }
```

Detailed description of the content (supports markdown).

#### Content Classification

```csharp
public ContentType ContentType { get; set; }
```

Type of content. See [ContentType Enum](#contenttype-enum).

```csharp
public string TargetGame { get; set; }
```

Game identifier this content targets.
Values: `"generals"`, `"zerohour"`, `"generals-online"`

```csharp
public string BaseContentId { get; set; }
```

Optional. If this is an addon, the ID of the base content it extends.
Example: `"shockwave"` for a Shockwave addon

#### Publisher Information

```csharp
public PublisherInfo Publisher { get; set; }
```

Information about the content publisher. See [PublisherInfo Class](#publisherinfo-class).

#### Files & Installation

```csharp
public List<ManifestFile> Files { get; set; }
```

List of files to install. See [ManifestFile Class](#manifestfile-class).

```csharp
public InstallationInstructions InstallationInstructions { get; set; }
```

Optional. Custom installation steps and workspace strategy. See [InstallationInstructions Class](#installationinstructions-class).

```csharp
public long TotalSize { get; set; }
```

Total size of all files in bytes (calculated from Files collection).

#### Dependencies

```csharp
public List<ContentDependency> Dependencies { get; set; }
```

List of runtime dependencies. See [ContentDependency Class](#contentdependency-class).

#### Metadata

```csharp
public ContentMetadata Metadata { get; set; }
```

Additional metadata (tags, screenshots, etc.). See [ContentMetadata Class](#contentmetadata-class).

```csharp
public DateTime CreatedAt { get; set; }
```

Timestamp when manifest was created.

```csharp
public DateTime? UpdatedAt { get; set; }
```

Timestamp when manifest was last updated.

#### Source Tracking

```csharp
public string SourceProvider { get; set; }
```

Identifier of the provider that created this manifest.
Example: `"generic-catalog"`, `"moddb"`, `"cnclabs"`, `"github"`

```csharp
public string SourceUrl { get; set; }
```

Original URL where content was discovered.

```csharp
public Dictionary<string, object> ProviderMetadata { get; set; }
```

Provider-specific metadata (e.g., ModDB page ID, GitHub repo info).

---

## ManifestFile Class

**Namespace**: `GenHub.Core.Models.Manifest`
**File**: `GenHub.Core/Models/Manifest/ManifestFile.cs`

Represents a single file in the manifest.

### Properties

```csharp
public string RelativePath { get; set; }
```

Path relative to content root where file should be installed.
Example: `"Data/INI/Object/AmericaVehicle.ini"`

```csharp
public string Hash { get; set; }
```

SHA256 hash of the file (lowercase hex string).
Example: `"a3f5e8c9d2b1..."`

```csharp
public long Size { get; set; }
```

File size in bytes.

```csharp
public ContentSourceType SourceType { get; set; }
```

How this file is sourced. See [ContentSourceType Enum](#contentsourcetype-enum).

```csharp
public ContentInstallTarget InstallTarget { get; set; }
```

Where this file should be installed. See [ContentInstallTarget Enum](#contentinstalltarget-enum).

```csharp
public string DownloadUrl { get; set; }
```

Optional. Direct download URL for this file (used with SourceType.Direct).

```csharp
public string ArchivePath { get; set; }
```

Optional. Path within archive if SourceType is Archive.
Example: `"ShockwaveChaos/Data/INI/Object/AmericaVehicle.ini"`

```csharp
public string CasReference { get; set; }
```

Optional. CAS hash reference if SourceType is CAS (file already in storage).

```csharp
public FilePermissions Permissions { get; set; }
```

Optional. Unix-style file permissions (for executable files).
Example: `0755` for executables

```csharp
public Dictionary<string, string> Attributes { get; set; }
```

Optional. Additional file attributes (e.g., `{ "executable": "true" }`).

---

## ContentDependency Class

**Namespace**: `GenHub.Core.Models.Manifest`
**File**: `GenHub.Core/Models/Manifest/ContentDependency.cs`

Represents a runtime dependency on other content.

### Properties

```csharp
public string Id { get; set; }
```

Manifest ID of the dependency.
Example: `"1.0.shockwave.mod.shockwave"`

```csharp
public string Name { get; set; }
```

Human-readable name of the dependency.
Example: `"Shockwave Mod"`

```csharp
public DependencyType DependencyType { get; set; }
```

Type of dependency. See [DependencyType Enum](#dependencytype-enum).

```csharp
public InstallBehavior InstallBehavior { get; set; }
```

How to handle installation. See [InstallBehavior Enum](#installbehavior-enum).

```csharp
public string MinVersion { get; set; }
```

Optional. Minimum required version (semantic versioning).
Example: `"1.2.0"`

```csharp
public string MaxVersion { get; set; }
```

Optional. Maximum compatible version (exclusive).
Example: `"2.0.0"`

```csharp
public string ExactVersion { get; set; }
```

Optional. Exact version required (overrides min/max).
Example: `"1.2.3"`

```csharp
public string PublisherId { get; set; }
```

Optional. Publisher ID for cross-publisher dependencies.
Example: `"shockwave"`

```csharp
public bool StrictPublisher { get; set; }
```

If true, dependency must come from specified publisher (prevents substitution).

```csharp
public string CatalogId { get; set; }
```

Optional. Specific catalog ID where dependency can be found.

```csharp
public string Reason { get; set; }
```

Optional. Human-readable explanation of why this dependency is needed.
Example: `"Required for custom unit models"`

---

## PublisherInfo Class

**Namespace**: `GenHub.Core.Models.Manifest`
**File**: `GenHub.Core/Models/Manifest/PublisherInfo.cs`

Contains information about the content publisher.

### Properties

```csharp
public string Name { get; set; }
```

Publisher display name.
Example: `"Shockwave Team"`

```csharp
public string PublisherId { get; set; }
```

Unique publisher identifier (lowercase, alphanumeric + hyphens).
Example: `"shockwave"`

```csharp
public string Website { get; set; }
```

Optional. Publisher's website URL.
Example: `"https://shockwave.example.com"`

```csharp
public string SupportUrl { get; set; }
```

Optional. Support/contact URL (forum, Discord, email).
Example: `"https://discord.gg/shockwave"`

```csharp
public string UpdateApiEndpoint { get; set; }
```

Optional. API endpoint for checking updates.
Example: `"https://api.example.com/updates"`

```csharp
public string AvatarUrl { get; set; }
```

Optional. Publisher avatar/logo URL.

```csharp
public PublisherType PublisherType { get; set; }
```

Type of publisher: `GenericCatalog`, `ModDB`, `CNCLabs`, `GitHub`, `Manual`

```csharp
public Dictionary<string, string> ContactInfo { get; set; }
```

Optional. Additional contact methods (e.g., `{ "discord": "username#1234", "email": "..." }`).

---

## ContentMetadata Class

**Namespace**: `GenHub.Core.Models.Manifest`
**File**: `GenHub.Core/Models/Manifest/ContentMetadata.cs`

Additional metadata for content presentation and discovery.

### Properties

```csharp
public string ShortDescription { get; set; }
```

Brief one-line description (max 200 chars).

```csharp
public string LongDescription { get; set; }
```

Detailed description (supports markdown).

```csharp
public List<string> Tags { get; set; }
```

Searchable tags.
Example: `["balance", "new-units", "graphics"]`

```csharp
public string IconUrl { get; set; }
```

Optional. Icon/thumbnail URL (recommended: 256x256px).

```csharp
public string BannerUrl { get; set; }
```

Optional. Banner image URL (recommended: 1920x400px).

```csharp
public List<string> ScreenshotUrls { get; set; }
```

Optional. Screenshot URLs for gallery.

```csharp
public string VideoUrl { get; set; }
```

Optional. Trailer/showcase video URL (YouTube, etc.).

```csharp
public string Author { get; set; }
```

Optional. Original author name (may differ from publisher).

```csharp
public List<string> Contributors { get; set; }
```

Optional. List of contributor names.

```csharp
public string License { get; set; }
```

Optional. License identifier (e.g., `"MIT"`, `"GPL-3.0"`, `"Proprietary"`).

```csharp
public DateTime? ReleaseDate { get; set; }
```

Optional. Original release date.

```csharp
public string Changelog { get; set; }
```

Optional. Version-specific changelog (markdown).

```csharp
public Dictionary<string, ContentVariant> Variants { get; set; }
```

Optional. Content variants (e.g., different resolutions, feature sets).
Example: `{ "classic": {...}, "modern": {...} }`

```csharp
public Dictionary<string, object> CustomFields { get; set; }
```

Optional. Provider-specific custom fields.

---

## InstallationInstructions Class

**Namespace**: `GenHub.Core.Models.Manifest`
**File**: `GenHub.Core/Models/Manifest/InstallationInstructions.cs`

Custom installation steps and workspace configuration.

### Properties

```csharp
public List<InstallStep> PreInstallSteps { get; set; }
```

Optional. Steps to execute before file installation.
Example: Backup files, check prerequisites, prompt user

```csharp
public List<InstallStep> PostInstallSteps { get; set; }
```

Optional. Steps to execute after file installation.
Example: Run patcher, generate config files, show readme

```csharp
public WorkspaceStrategy WorkspaceStrategy { get; set; }
```

Preferred workspace strategy: `Symlink`, `Copy`, `Hardlink`
Default: `Symlink`

```csharp
public bool RequiresRestart { get; set; }
```

If true, game must be restarted after installation.

```csharp
public string InstallNotes { get; set; }
```

Optional. Additional installation notes for users (markdown).

### InstallStep Structure

```csharp
public class InstallStep
{
    public string Type { get; set; }        // "command", "prompt", "backup", "patch"
    public string Description { get; set; } // Human-readable description
    public Dictionary<string, object> Parameters { get; set; } // Step-specific params
}
```

---

## Enumerations

### ContentType Enum

```csharp
public enum ContentType
{
    Mod,           // Total conversion or major gameplay mod
    Map,           // Custom map or map pack
    Addon,         // Addon to existing mod (requires base mod)
    Patch,         // Bug fix or compatibility patch
    Tool,          // External tool or utility
    Asset,         // Shared assets (models, textures, sounds)
    Config,        // Configuration files or presets
    SaveGame,      // Save game or replay file
    Other          // Uncategorized content
}
```

### ContentSourceType Enum

```csharp
public enum ContentSourceType
{
    Archive,       // Files are in a zip/rar/7z archive
    Direct,        // Individual files with direct download URLs
    CAS,           // Files already in Content-Addressable Storage
    Git            // Files from Git repository
}
```

### ContentInstallTarget Enum

```csharp
public enum ContentInstallTarget
{
    Data,          // Install to Data/ folder (most mods)
    Root,          // Install to game root directory
    Custom,        // Custom path specified in RelativePath
    Documents,     // User documents folder (saves, replays)
    AppData        // Application data folder (configs)
}
```

### DependencyType Enum

```csharp
public enum DependencyType
{
    Required,      // Must be installed, installation fails without it
    Optional,      // Enhances functionality but not required
    Recommended,   // Strongly suggested but not required
    Conflicting    // Cannot be installed together (mutual exclusion)
}
```

### InstallBehavior Enum

```csharp
public enum InstallBehavior
{
    AutoInstall,   // Automatically install if missing
    Prompt,        // Ask user before installing
    Manual,        // User must manually install
    Skip           // Don't install (for optional dependencies)
}
```

### PublisherType Enum

```csharp
public enum PublisherType
{
    GenericCatalog,  // Publisher using GenHub catalog format
    ModDB,           // Content from ModDB
    CNCLabs,         // Content from CNCLabs
    GitHub,          // Content from GitHub releases
    Manual           // Manually created manifest
}
```

---

## Usage Examples

### Creating a Basic Manifest

```csharp
using GenHub.Core.Models.Manifest;

var manifest = new ContentManifest
{
    ManifestId = "1.0.mymod.mod.awesome-mod",
    Name = "Awesome Mod",
    Version = "1.0.0",
    Description = "An awesome mod that adds new units and balance changes",
    ContentType = ContentType.Mod,
    TargetGame = "zerohour",

    Publisher = new PublisherInfo
    {
        Name = "Awesome Modder",
        PublisherId = "mymod",
        Website = "https://example.com",
        PublisherType = PublisherType.GenericCatalog
    },

    Files = new List<ManifestFile>
    {
        new ManifestFile
        {
            RelativePath = "Data/INI/Object/AmericaVehicle.ini",
            Hash = "a3f5e8c9d2b1...",
            Size = 12345,
            SourceType = ContentSourceType.Archive,
            InstallTarget = ContentInstallTarget.Data,
            ArchivePath = "AwesomeMod/Data/INI/Object/AmericaVehicle.ini"
        }
    },

    Metadata = new ContentMetadata
    {
        ShortDescription = "New units and balance changes",
        Tags = new List<string> { "balance", "new-units" },
        Author = "Awesome Modder"
    },

    CreatedAt = DateTime.UtcNow
};
```

### Adding Dependencies

```csharp
manifest.Dependencies = new List<ContentDependency>
{
    new ContentDependency
    {
        Id = "1.0.shockwave.mod.shockwave",
        Name = "Shockwave Mod",
        DependencyType = DependencyType.Required,
        InstallBehavior = InstallBehavior.AutoInstall,
        MinVersion = "1.2.0",
        PublisherId = "shockwave",
        StrictPublisher = true,
        Reason = "Required for custom unit models"
    },

    new ContentDependency
    {
        Id = "1.0.controlbar.asset.modern-ui",
        Name = "Modern UI Pack",
        DependencyType = DependencyType.Optional,
        InstallBehavior = InstallBehavior.Prompt,
        Reason = "Enhances visual experience"
    }
};
```

### Adding Installation Instructions

```csharp
manifest.InstallationInstructions = new InstallationInstructions
{
    WorkspaceStrategy = WorkspaceStrategy.Symlink,
    RequiresRestart = true,

    PreInstallSteps = new List<InstallStep>
    {
        new InstallStep
        {
            Type = "backup",
            Description = "Backup existing INI files",
            Parameters = new Dictionary<string, object>
            {
                { "paths", new[] { "Data/INI/Object/*.ini" } }
            }
        }
    },

    PostInstallSteps = new List<InstallStep>
    {
        new InstallStep
        {
            Type = "command",
            Description = "Run GenPatcher to apply compatibility fixes",
            Parameters = new Dictionary<string, object>
            {
                { "executable", "Tools/GenPatcher.exe" },
                { "arguments", "--apply-fixes" }
            }
        }
    },

    InstallNotes = "**Important**: This mod requires Zero Hour 1.04 or later."
};
```

### Loading from JSON

```csharp
using System.Text.Json;

string json = File.ReadAllText("manifest.json");
var manifest = JsonSerializer.Deserialize<ContentManifest>(json);
```

### Saving to JSON

```csharp
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

string json = JsonSerializer.Serialize(manifest, options);
File.WriteAllText("manifest.json", json);
```

### Validating a Manifest

```csharp
public static class ManifestValidator
{
    public static List<string> Validate(ContentManifest manifest)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(manifest.ManifestId))
            errors.Add("ManifestId is required");

        if (string.IsNullOrEmpty(manifest.Name))
            errors.Add("Name is required");

        if (string.IsNullOrEmpty(manifest.Version))
            errors.Add("Version is required");

        if (manifest.Files == null || manifest.Files.Count == 0)
            errors.Add("At least one file is required");

        foreach (var file in manifest.Files ?? new List<ManifestFile>())
        {
            if (string.IsNullOrEmpty(file.RelativePath))
                errors.Add($"File missing RelativePath");

            if (string.IsNullOrEmpty(file.Hash))
                errors.Add($"File {file.RelativePath} missing Hash");

            if (file.Size <= 0)
                errors.Add($"File {file.RelativePath} has invalid Size");
        }

        return errors;
    }
}
```

### Calculating Total Size

```csharp
manifest.TotalSize = manifest.Files?.Sum(f => f.Size) ?? 0;
```

### Checking Dependency Compatibility

```csharp
public static bool IsVersionCompatible(ContentDependency dep, string installedVersion)
{
    if (!string.IsNullOrEmpty(dep.ExactVersion))
        return installedVersion == dep.ExactVersion;

    bool minOk = string.IsNullOrEmpty(dep.MinVersion) ||
                 Version.Parse(installedVersion) >= Version.Parse(dep.MinVersion);

    bool maxOk = string.IsNullOrEmpty(dep.MaxVersion) ||
                 Version.Parse(installedVersion) < Version.Parse(dep.MaxVersion);

    return minOk && maxOk;
}
```

---

## JSON Schema Example

Complete example of a ContentManifest in JSON format:

```json
{
  "manifestId": "1.0.shockwave.mod.shockwave-chaos",
  "name": "Shockwave Chaos Edition",
  "version": "1.2.3",
  "description": "Enhanced version of Shockwave with new units and balance changes",
  "contentType": "Mod",
  "targetGame": "zerohour",
  "baseContentId": "shockwave",

  "publisher": {
    "name": "Shockwave Team",
    "publisherId": "shockwave",
    "website": "https://shockwave.example.com",
    "supportUrl": "https://discord.gg/shockwave",
    "avatarUrl": "https://example.com/avatar.png",
    "publisherType": "GenericCatalog"
  },

  "files": [
    {
      "relativePath": "Data/INI/Object/AmericaVehicle.ini",
      "hash": "a3f5e8c9d2b1f4e7c8a5b3d6e9f2c1a4b7d0e3f6c9a2b5d8e1f4c7a0b3d6e9f2",
      "size": 45678,
      "sourceType": "Archive",
      "installTarget": "Data",
      "archivePath": "ShockwaveChaos/Data/INI/Object/AmericaVehicle.ini"
    },
    {
      "relativePath": "Data/Art/Textures/TXTankCrusader.dds",
      "hash": "b4e7c8a5b3d6e9f2c1a4b7d0e3f6c9a2b5d8e1f4c7a0b3d6e9f2c1a4b7d0e3f6",
      "size": 524288,
      "sourceType": "Archive",
      "installTarget": "Data",
      "archivePath": "ShockwaveChaos/Data/Art/Textures/TXTankCrusader.dds"
    }
  ],

  "dependencies": [
    {
      "id": "1.0.shockwave.mod.shockwave",
      "name": "Shockwave Mod",
      "dependencyType": "Required",
      "installBehavior": "AutoInstall",
      "minVersion": "1.2.0",
      "maxVersion": "2.0.0",
      "publisherId": "shockwave",
      "strictPublisher": true,
      "reason": "Base mod required for Chaos Edition features"
    },
    {
      "id": "1.0.controlbar.asset.modern-ui",
      "name": "Modern UI Pack",
      "dependencyType": "Optional",
      "installBehavior": "Prompt",
      "reason": "Enhances visual experience with modern interface"
    }
  ],

  "metadata": {
    "shortDescription": "Enhanced Shockwave with new units and balance",
    "longDescription": "# Shockwave Chaos Edition\n\nA comprehensive enhancement...",
    "tags": ["balance", "new-units", "graphics", "shockwave-addon"],
    "iconUrl": "https://example.com/icon.png",
    "bannerUrl": "https://example.com/banner.jpg",
    "screenshotUrls": [
      "https://example.com/screenshot1.jpg",
      "https://example.com/screenshot2.jpg"
    ],
    "videoUrl": "https://youtube.com/watch?v=...",
    "author": "Shockwave Team",
    "contributors": ["Developer1", "Developer2", "Artist1"],
    "license": "Proprietary",
    "releaseDate": "2026-01-15T00:00:00Z",
    "changelog": "## Version 1.2.3\n- Added new units\n- Balance changes\n- Bug fixes"
  },

  "installationInstructions": {
    "workspaceStrategy": "Symlink",
    "requiresRestart": true,
    "preInstallSteps": [
      {
        "type": "backup",
        "description": "Backup existing INI files",
        "parameters": {
          "paths": ["Data/INI/Object/*.ini"]
        }
      }
    ],
    "postInstallSteps": [
      {
        "type": "command",
        "description": "Run GenPatcher for compatibility",
        "parameters": {
          "executable": "Tools/GenPatcher.exe",
          "arguments": "--apply-fixes"
        }
      }
    ],
    "installNotes": "**Important**: Requires Zero Hour 1.04 or later"
  },

  "totalSize": 15728640,
  "createdAt": "2026-01-15T10:30:00Z",
  "updatedAt": "2026-02-20T14:45:00Z",

  "sourceProvider": "generic-catalog",
  "sourceUrl": "https://example.com/catalog.json",
  "providerMetadata": {
    "catalogId": "shockwave-main",
    "releaseId": "chaos-1.2.3"
  }
}
```

---

## Best Practices

### Manifest ID Format

Always use the format: `{version}.{publisherId}.{contentType}.{contentId}`

- Version: Schema version (currently `1.0`)
- PublisherId: Lowercase, alphanumeric + hyphens
- ContentType: Lowercase enum value (`mod`, `map`, `addon`, etc.)
- ContentId: Unique identifier within publisher's catalog

Example: `1.0.shockwave.mod.shockwave-chaos-edition`

### File Hashing

- Always use SHA256 for file hashes
- Store hashes as lowercase hex strings (64 characters)
- Calculate hashes before compression (on actual file content)
- Use hashes for integrity verification during installation

### Version Constraints

Use semantic versioning for all version fields:

- `MinVersion`: Inclusive minimum (e.g., `"1.2.0"` means >= 1.2.0)
- `MaxVersion`: Exclusive maximum (e.g., `"2.0.0"` means < 2.0.0)
- `ExactVersion`: Exact match required (overrides min/max)

### Dependency Management

- Use `Required` for dependencies that break functionality without them
- Use `Recommended` for dependencies that enhance but aren't critical
- Use `Optional` for nice-to-have features
- Use `Conflicting` to prevent incompatible content from being installed together
- Always provide a `Reason` to help users understand why dependency is needed

### File Organization

- Use forward slashes in `RelativePath` (cross-platform compatibility)
- Keep paths relative to content root (no absolute paths)
- Use `InstallTarget` to specify installation location
- Group related files logically (INI files together, textures together, etc.)

### Metadata Quality

- Provide clear, concise descriptions
- Use meaningful tags for discoverability
- Include screenshots and videos when possible
- Keep icon/banner URLs stable (don't use temporary hosting)
- Write changelogs in markdown for better formatting

### Installation Instructions

- Only use custom install steps when necessary
- Prefer `Symlink` workspace strategy for efficiency
- Document any special requirements in `InstallNotes`
- Test installation steps thoroughly before publishing

---

## Related Documentation

- **Publisher Catalog Schema**: `docs/features/content/provider-configuration.md`
- **Content Pipeline**: `CONTENT_PIPELINE_REPORT.md`
- **Dependency System**: `docs/features/content/content-dependencies.md`
- **Publisher Studio Guide**: `docs/features/tools/publisher-studio.md`
- **Architecture Overview**: `COMPREHENSIVE_ARCHITECTURE_SUMMARY.md`

---

## File Locations

- **ContentManifest.cs**: `GenHub.Core/Models/Manifest/ContentManifest.cs`
- **ManifestFile.cs**: `GenHub.Core/Models/Manifest/ManifestFile.cs`
- **ContentDependency.cs**: `GenHub.Core/Models/Manifest/ContentDependency.cs`
- **PublisherInfo.cs**: `GenHub.Core/Models/Manifest/PublisherInfo.cs`
- **ContentMetadata.cs**: `GenHub.Core/Models/Manifest/ContentMetadata.cs`
- **InstallationInstructions.cs**: `GenHub.Core/Models/Manifest/InstallationInstructions.cs`

---

**End of API Reference**
