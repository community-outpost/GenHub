---
title: Publisher Manifest Factories
description: Extensible architecture for publisher-specific manifest generation
---

# Publisher Manifest Factories

## Overview

The Publisher Manifest Factory pattern enables extensible, publisher-agnostic content delivery by separating publisher-specific manifest generation logic from the core content delivery pipeline. This architecture allows GenHub to support any GitHub publisher (game developers, mod creators, UI addon developers) and any content type (GameClient, Mod, Patch, Addon, MapPack, etc.) without modifying core delivery code.

## Architecture

### Design Principles

1. **Open/Closed Principle**: Content delivery is closed for modification, open for extension via factories
2. **Strategy Pattern**: Factory selection via `CanHandle()` self-identification
3. **Separation of Concerns**: Publisher logic in factories, delivery logic in orchestrator
4. **Dependency Injection**: All factories registered in DI container, resolved automatically

### Components

#### IPublisherManifestFactory Interface

Located: `GenHub.Core/Interfaces/Content/IPublisherManifestFactory.cs`

```csharp
public interface IPublisherManifestFactory
{
    /// <summary>
    /// Publisher identifier (e.g., "thesuperhackers", "generalsonline", "generic")
    /// </summary>
    string PublisherId { get; }

    /// <summary>
    /// Determines if this factory can handle the given manifest
    /// </summary>
    bool CanHandle(ContentManifest manifest);

    /// <summary>
    /// Creates one or more manifests from extracted content
    /// Returns multiple manifests for multi-variant releases
    /// </summary>
    Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the subdirectory for manifest file storage
    /// </summary>
    string GetManifestDirectory(ContentManifest manifest, string extractedDirectory);
}
```

#### SuperHackersManifestFactory

Located: `GenHub/Features/Content/Services/Publishers/SuperHackersManifestFactory.cs`

**Purpose**: Handles TheSuperHackers multi-game releases containing both Generals and Zero Hour executables.

**Key Features**:

- Detects `generalsv.exe` (Generals) and `generalszh.exe` (Zero Hour) executables
- Creates **two separate manifests** from single release
- Generates proper ManifestIds: `1.{version}.thesuperhackers.gameclient.generals` and `.zerohour`
- Assigns correct `TargetGame` (Generals vs ZeroHour)
- Sets `IsExecutable` flag only on actual game executables

**Selection Logic**:

```csharp
public bool CanHandle(ContentManifest manifest)
{
    return manifest.Publisher?.PublisherType == "thesuperhackers" &&
           manifest.ContentType == ContentType.GameClient;
}
```

#### PublisherManifestFactoryResolver

Located: `GenHub/Features/Content/Services/Publishers/PublisherManifestFactoryResolver.cs`

**Purpose**: Selects appropriate factory for each manifest using DI and `CanHandle()` pattern.

**Resolution Algorithm**:

1. Receives `IEnumerable<IPublisherManifestFactory>` via DI
2. Iterates factories calling `CanHandle(manifest)`
3. Returns first match OR null if none match
4. Logs which factory was selected

```csharp
public IPublisherManifestFactory? ResolveFactory(ContentManifest manifest)
{
    // Try to find a specialized factory that can handle this manifest
    var factory = factories.FirstOrDefault(f => f.CanHandle(manifest));

    if (factory != null)
    {
        logger.LogInformation(
            "Resolved {FactoryType} for manifest {ManifestId} (Publisher: {Publisher})",
            factory.GetType().Name,
            manifest.Id,
            manifest.Publisher?.PublisherType ?? "Unknown");
        return factory;
    }

    logger.LogWarning(
        "No factory found for manifest {ManifestId} (Publisher: {Publisher}, ContentType: {ContentType})",
        manifest.Id,
        manifest.Publisher?.PublisherType ?? "Unknown",
        manifest.ContentType);

    return null;
}
```

### Content Delivery Flow

#### GitHubContentDeliverer (Publisher-Agnostic)

The refactored `GitHubContentDeliverer` is now completely publisher-agnostic:

```csharp
public async Task<OperationResult<ContentManifest>> DeliverContentAsync(
    ContentManifest packageManifest,
    string targetDirectory,
    IProgress<ContentAcquisitionProgress>? progress = null,
    CancellationToken cancellationToken = default)
{
    // 1. Download files from GitHub
    // 2. Extract ZIPs to target directory
    // 3. Delegate to factory for manifest generation
    return await HandleExtractedContentAsync(packageManifest, targetDirectory, cancellationToken);
}
```

#### HandleExtractedContentAsync (Factory Orchestration)

```csharp
private async Task<OperationResult<ContentManifest>> HandleExtractedContentAsync(
    ContentManifest originalManifest,
    string extractedDirectory,
    CancellationToken cancellationToken)
{
    // 1. Resolve appropriate factory
    var factory = _factoryResolver.ResolveFactory(originalManifest);

    // 2. Create manifests using factory
    var manifests = await factory.CreateManifestsFromExtractedContentAsync(
        originalManifest, extractedDirectory, cancellationToken);

    // 3. Return PRIMARY manifest (first one)
    var primaryManifest = manifests[0];

    // 4. Store SECONDARY manifests to pool
    for (int i = 1; i < manifests.Count; i++)
    {
        await _manifestPool.AddManifestAsync(manifests[i], ...);
    }

    return OperationResult<ContentManifest>.CreateSuccess(primaryManifest);
}
```

## File Storage in Content-Addressable Storage (CAS)

### ContentStorageService Integration

After manifests are created by factories, files must be physically stored in GenHub's CAS system for later workspace preparation and game launching.

#### Storage Decision Logic

Located: `GenHub/Features/Content/Services/ContentStorageService.cs`

The storage service determines whether to store files physically or metadata-only based on the manifest's `SourceType` flags:

```csharp
private bool RequiresPhysicalStorage(ContentManifest manifest)
{
    // GameInstallation content always references external installations
    if (manifest.ContentType == ContentType.GameInstallation)
        return false;

    // Check if any file requires CAS storage based on source type
    bool hasStorableContent = manifest.Files.Any(f =>
        f.SourceType == ContentSourceType.ContentAddressable ||
        f.SourceType == ContentSourceType.ExtractedPackage ||
        f.SourceType == ContentSourceType.LocalFile ||
        f.SourceType == ContentSourceType.Unknown);

    return hasStorableContent;
}
```

#### Source Type Meanings

- **ExtractedPackage**: Files extracted from GitHub ZIP archives (factory-generated manifests)
- **ContentAddressable**: Files already in CAS from previous operations
- **LocalFile**: Files from local filesystem that should be copied to CAS
- **GameInstallation**: References to game installation files (NOT copied to CAS)
- **RemoteDownload**: Files that need downloading (not yet in CAS)

#### Storage Flow

1. **GitHubContentDeliverer** extracts ZIPs to staging directory
2. **Factory** creates manifests with `SourceType = ExtractedPackage`
3. **ContentManifestPool.AddManifestAsync** delegates to storage service
4. **ContentStorageService.StoreContentAsync** checks `RequiresPhysicalStorage()`
5. If `true`, calls `StoreContentFilesAsync()` to copy files from staging to CAS
6. If `false`, calls `StoreManifestOnlyAsync()` to save only metadata

#### CAS Directory Structure

```
C:\Users\{User}\AppData\Roaming\GenHub\Content\
├── Manifests\
│   ├── 1.20251107.thesuperhackers.gameclient.generals.manifest.json
│   └── 1.20251107.thesuperhackers.gameclient.zerohour.manifest.json
└── Content\
    ├── 1.20251107.thesuperhackers.gameclient.generals\
    │   ├── generalsv.exe
    │   ├── game.dat
    │   └── ...
    └── 1.20251107.thesuperhackers.gameclient.zerohour\
        ├── generalszh.exe
        ├── game.dat
        └── ...
```

### Important: GameClient Content from GitHub

**Prior to this fix**, the storage service incorrectly skipped physical file storage for ALL `ContentType.GameClient` manifests, assuming they were references to existing game installations.

**After the fix**, the storage service correctly:

- ✅ Stores files for GameClient manifests with `SourceType = ExtractedPackage` (downloaded from GitHub)
- ✅ Skips storage for GameClient manifests with `SourceType = GameInstallation` (detected installations)
- ✅ Enables proper workspace preparation and game launching for GitHub-delivered content

## Multi-Variant Content Support

### Use Case: TheSuperHackers Weekly Releases

TheSuperHackers publishes weekly GitHub releases containing **both** Generals and Zero Hour executables:

```
weekly-release.zip
├── generalsv.exe      (Generals)
├── generalszh.exe     (Zero Hour)
├── game.dat
├── maps/
└── ...
```

### Factory Behavior

1. **SuperHackersManifestFactory** detects both executables
2. Creates **TWO manifests**:
   - `1.20251107.thesuperhackers.gameclient.generals` (PRIMARY)
   - `1.20251107.thesuperhackers.gameclient.zerohour` (SECONDARY)
3. Primary manifest returned to caller (added to pool via normal flow)
4. Secondary manifest stored directly to pool (available immediately)

### Benefits

- ✅ Users can install either Generals OR Zero Hour from single release
- ✅ Both versions available in content browser
- ✅ Each manifest has correct `TargetGame`, executable paths, files
- ✅ No duplicate file extraction or storage

## Adding Support for New Publishers

### Scenario: Supporting Custom Mod Publisher

To add support for a new publisher with specialized content handling:

#### 1. Create Factory Class

```csharp
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;

namespace GenHub.Features.Content.Services.Publishers;

public class CustomModPublisherManifestFactory : IPublisherManifestFactory
{
    public string PublisherId => "custommodpublisher";

    public bool CanHandle(ContentManifest manifest)
    {
        return manifest.Publisher?.PublisherType == "custommodpublisher" &&
               manifest.ContentType == ContentType.Mod;
    }

    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken)
    {
        // Custom logic: detect mod variants, split by language packs, etc.
        var manifests = new List<ContentManifest>();

        // Example: Create manifest per language pack
        var languageDirs = Directory.GetDirectories(extractedDirectory, "lang_*");
        foreach (var langDir in languageDirs)
        {
            var manifest = await BuildManifestForLanguageAsync(
                originalManifest, langDir, cancellationToken);
            manifests.Add(manifest);
        }

        return manifests;
    }

    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        // Return subdirectory for this manifest variant
        return Path.Combine(extractedDirectory, manifest.Metadata["Language"]);
    }
}
```

#### 2. Register in DI Container

File: `GenHub/Infrastructure/DependencyInjection/ContentPipelineModule.cs`

```csharp
services.AddTransient<IPublisherManifestFactory, CustomModPublisherManifestFactory>();
```

#### 3. Done

Zero changes to `GitHubContentDeliverer` or any other core code. The resolver automatically finds and uses the new factory.

## Supported Content Types

The factory pattern supports **all** ContentType enum values:

### Current

- **GameClient**: Complete game executables
- **Mod**: Game modifications
- **Patch**: Bug fixes and updates
- **Addon**: Additional content packs
- **MapPack**: Map collections
- **LanguagePack**: Translation files
- **Mission**: Campaign missions
- **Map**: Individual maps
- **ContentBundle**: Meta-packages
- **PublisherReferral**: Links to publisher content
- **ContentReferral**: Links to other content

### Factory Selection by Content Type

```csharp
// GameClient from TheSuperHackers → SuperHackersManifestFactory
ContentType.GameClient + publisher="thesuperhackers"

// Mod from any publisher → No factory found (fails)
// Patch from any publisher → No factory found (fails)
// Custom content → CustomPublisherManifestFactory (if implemented)

// Custom content → CustomPublisherManifestFactory (if implemented)
```

## Testing Strategy

### Unit Tests

Test each factory independently:

```csharp
[Fact]
public async Task SuperHackersFactory_DetectsBothExecutables()
{
    // Arrange
    var factory = new SuperHackersManifestFactory(...);
    var manifest = CreateTestManifest("thesuperhackers", ContentType.GameClient);
    var extractedDir = CreateMockDirectory(["generalsv.exe", "generalszh.exe"]);

    // Act
    var result = await factory.CreateManifestsFromExtractedContentAsync(
        manifest, extractedDir, CancellationToken.None);

    // Assert
    Assert.Equal(2, result.Count);
    Assert.Contains(result, m => m.TargetGame == GameType.Generals);
    Assert.Contains(result, m => m.TargetGame == GameType.ZeroHour);
}
```

### Integration Tests

Test resolver + factories:

```csharp
[Fact]
public void Resolver_SelectsCorrectFactory()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddTransient<IPublisherManifestFactory, SuperHackersManifestFactory>();
    services.AddSingleton<PublisherManifestFactoryResolver>();
    var resolver = services.BuildServiceProvider().GetService<PublisherManifestFactoryResolver>();

    // Act
    var factory = resolver.ResolveFactory(CreateSuperHackersManifest());

    // Assert
    Assert.IsType<SuperHackersManifestFactory>(factory);
}
```

### End-to-End Tests

Test complete content delivery pipeline:

```csharp
[Fact]
public async Task GitHubDeliverer_HandlesMultiGameRelease()
{
    // Arrange
    var deliverer = CreateDeliverer(); // with resolver + factories
    var manifest = CreateSuperHackersReleaseManifest();

    // Act
    var result = await deliverer.DeliverContentAsync(manifest, targetDir);

    // Assert
    Assert.True(result.Success);
    Assert.NotNull(result.Value); // Primary manifest
    // Verify secondary manifest in pool
    var secondaryManifest = await _manifestPool.GetManifestAsync("...zerohour");
    Assert.NotNull(secondaryManifest);
}
```

## Logging and Diagnostics

### Factory Resolution Logging

```
[INFO] Resolved factory SuperHackersManifestFactory for manifest 1.20251107.thesuperhackers.gameclient.generals
[INFO] Factory generated 2 manifest(s) from extracted content
[INFO] Storing secondary manifest 1.20251107.thesuperhackers.gameclient.zerohour directly to content pool
[INFO] Successfully stored secondary manifest to pool
```

### Factory Processing Logging

```
[INFO] Detected Generals executable: Z:\content\generalsv.exe
[INFO] Detected Zero Hour executable: Z:\content\generalszh.exe
[INFO] Created Generals manifest with 1234 files
[INFO] Created ZeroHour manifest with 1456 files
```

## Future Extensions

### Potential Factory Implementations

1. **GeneralsOnlineManifestFactory**: Creates 30Hz and 60Hz variants from single release
2. **ModDBManifestFactory**: Handles ModDB-specific content structure
3. **SteamWorkshopManifestFactory**: Processes Steam Workshop items
4. **LocalModFolderManifestFactory**: Scans local mod directories
5. **MultiLanguageModFactory**: Splits mods by language pack

### Factory Feature Enhancements

- **Version Detection**: Automatic version extraction from file metadata
- **Dependency Inference**: Auto-detect dependencies from file structure
- **Compatibility Checking**: Validate content compatibility with game versions
- **Asset Optimization**: Compress textures, optimize models during manifest generation
- **Checksum Validation**: Compute and verify file hashes during processing

## Benefits Summary

✅ **Extensibility**: New publishers supported by adding single factory class  
✅ **Maintainability**: Publisher changes isolated to specific factory  
✅ **Testability**: Each factory independently testable  
✅ **Flexibility**: Supports any content type, any publisher, any variant strategy (with appropriate factory)  
✅ **Separation of Concerns**: Core delivery code never changes  
✅ **Open/Closed Principle**: Closed for modification, open for extension  
✅ **Dependency Injection**: Automatic factory registration and resolution  
✅ **Multi-Variant Support**: Handle complex releases with multiple game versions  

## Migration Notes

### Before Refactoring

```csharp
// GitHubContentDeliverer had hardcoded SuperHackers logic:
private Dictionary<GameType, string> DetectGameExecutables(string directory)
{
    // Hardcoded generalsv.exe and generalszh.exe detection
}

private async Task<ContentManifest> BuildManifestForGameTypeAsync(...)
{
    // Hardcoded manifest generation per game type
}
```

**Problems**:

- ❌ Cannot support other publishers without modifying deliverer
- ❌ Cannot support other content types with special handling
- ❌ Violates Open/Closed Principle
- ❌ Testing requires mocking entire deliverer

### After Refactoring

```csharp
// GitHubContentDeliverer is publisher-agnostic:
private async Task<OperationResult<ContentManifest>> HandleExtractedContentAsync(...)
{
    var factory = _factoryResolver.ResolveFactory(originalManifest);
    var manifests = await factory.CreateManifestsFromExtractedContentAsync(...);
    return primaryManifest;
}
```

**Benefits**:

- ✅ Supports any publisher via factory pattern
- ✅ Supports any content type, any publisher, any variant strategy (with appropriate factory)
- ✅ Follows Open/Closed Principle
- ✅ Easy to test with mock factories

## References

- Interface: `GenHub.Core/Interfaces/Content/IPublisherManifestFactory.cs`
- SuperHackers Factory: `GenHub/Features/Content/Services/Publishers/SuperHackersManifestFactory.cs`
- Resolver: `GenHub/Features/Content/Services/Publishers/PublisherManifestFactoryResolver.cs`
- Deliverer: `GenHub/Features/Content/Services/ContentDeliverers/GitHubContentDeliverer.cs`
- DI Registration: `GenHub/Infrastructure/DependencyInjection/ContentPipelineModule.cs`
