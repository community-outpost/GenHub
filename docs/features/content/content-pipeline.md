---
title: Content Pipeline Architecture
description: Detailed documentation of the GenHub three-tier content pipeline for discovering, resolving, and acquiring content
---

# Content Pipeline Architecture

The GenHub content system uses a **three-tier pipeline architecture** that transforms external content sources into installable content with full manifest and CAS (Content-Addressable Storage) integration.

## Pipeline Overview

```mermaid
flowchart TB
    subgraph "Tier 1: Orchestration"
        CO["ContentOrchestrator"]
    end

    subgraph "Tier 2: Providers"
        BP["BaseContentProvider"]
        MDP["ModDBContentProvider"]
        CLP["CNCLabsContentProvider"]
        GOP["GeneralsOnlineProvider"]
    end

    subgraph "Tier 3: Components"
        D["Discoverers"]
        P["Parsers"]
        R["Resolvers"]
        DEL["Deliverers"]
        MF["ManifestFactories"]
    end

    subgraph "Storage"
        CAS["CAS Storage"]
        MP["Manifest Pool"]
    end

    CO --> BP
    BP --> D
    D --> P
    P --> R
    R --> DEL
    DEL --> MF
    MF --> CAS
    MF --> MP
```

## Tier 1: ContentOrchestrator

**Location**: `GenHub.Core/Interfaces/Content/IContentOrchestrator.cs`

The orchestrator is the system-wide coordinator for all content operations.

### Responsibilities

| Operation | Method | Description |
|-----------|--------|-------------|
| **Search** | `SearchAsync()` | Broadcasts query to all providers, aggregates results |
| **Acquire** | `AcquireContentAsync()` | Downloads, extracts, stores, and registers content |
| **Cache** | `IDynamicContentCache` | System-wide caching for performance |

### Search Flow

```csharp
// User initiates search in DownloadsBrowserView
var results = await _orchestrator.SearchAsync(new ContentSearchQuery
{
    SearchTerm = "Rise of the Reds",
    ContentType = ContentType.Mod,
    TargetGame = GameType.ZeroHour
});
```

---

## Tier 2: Content Providers

**Base**: `GenHub/Features/Content/Services/ContentProviders/BaseContentProvider.cs`

Providers are source-specific facades that orchestrate the internal pipeline.

### Provider Pattern

```csharp
public abstract class BaseContentProvider : IContentProvider
{
    protected abstract IContentDiscoverer Discoverer { get; }
    protected abstract IContentResolver Resolver { get; }
    protected abstract IContentDeliverer Deliverer { get; }

    // Common pipeline orchestration
    public virtual async Task<OperationResult<IEnumerable<ContentSearchResult>>> SearchAsync(
        ContentSearchQuery query, CancellationToken cancellationToken = default)
    {
        var providerDefinition = GetProviderDefinition();
        return await Discoverer.DiscoverAsync(providerDefinition, query, cancellationToken);
    }
}
```

### Registered Providers

| Provider | Discoverer | Parser | Notes |
|----------|------------|--------|-------|
| **ModDB** | `ModDBDiscoverer` | `ModDBPageParser` (AngleSharp) | Uses Playwright for WAF bypass |
| **CNC Labs** | `CNCLabsMapDiscoverer` | AngleSharp HTML | Direct HTTP scraping |
| **AOD Maps** | `AODMapsDiscoverer` | `AODMapsPageParser` | Pagination support |
| **Community Outpost** | `CommunityOutpostDiscoverer` | `GenPatcherDatCatalogParser` | `.dat` catalog format |
| **GitHub** | `GitHubDiscoverer` | GitHub API JSON | Release assets |
| **Generals Online** | `GeneralsOnlineDiscoverer` | GitHub API | Multi-variant releases |
| **File System** | `FileSystemDiscoverer` | Direct scan | Local manifests |

---

## Tier 3: Pipeline Components

### Discoverers (`IContentDiscoverer`)

**Location**: `GenHub.Core/Interfaces/Content/IContentDiscoverer.cs`

Discoverers fetch catalog data from external sources and delegate to parsers.

```csharp
public interface IContentDiscoverer : IContentSource
{
    Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default);

    // Overload with provider definition for data-driven configuration
    Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ProviderDefinition? provider,
        ContentSearchQuery query,
        CancellationToken cancellationToken = default);
}
```

**Key principle**: Discoverers handle network concerns (timeouts, retries, WAF bypass) but do NOT parse data themselves—that's the parser's job.

### Parsers (`ICatalogParser`, `IWebPageParser`)

**Locations**:

- `GenHub.Core/Interfaces/Providers/ICatalogParser.cs`
- `GenHub.Core/Interfaces/Parsers/IWebPageParser.cs`

Parsers transform raw data (HTML, JSON, `.dat` files) into `ContentSearchResult` objects.

| Parser | Format | Source |
|--------|--------|--------|
| `GenPatcherDatCatalogParser` | `.dat` pipe-delimited | Community Outpost |
| `ModDBPageParser` | HTML | ModDB |
| `AODMapsPageParser` | HTML | AOD Maps |
| AngleSharp | Generic HTML | CNC Labs |

### Resolvers (`IContentResolver`)

**Location**: `GenHub.Core/Interfaces/Content/IContentResolver.cs`

Resolvers transform lightweight search results into complete `ContentManifest` blueprints.

```csharp
public interface IContentResolver : IContentSource
{
    Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default);
}
```

**Resolution tasks**:

1. Fetch detail page for full metadata (description, screenshots)
2. Extract download URL
3. Determine target game and content type
4. Build manifest structure

### Deliverers (`IContentDeliverer`)

**Location**: `GenHub.Core/Interfaces/Content/IContentDeliverer.cs`

Deliverers download content files and prepare them for storage.

```csharp
public interface IContentDeliverer : IContentSource
{
    bool CanDeliver(ContentManifest manifest);

    Task<OperationResult<DeliveryResult>> DeliverContentAsync(
        ContentManifest manifest,
        string targetDirectory,
        CancellationToken cancellationToken = default);
}
```

### Manifest Factories (`IContentManifestFactory`)

**Location**: `GenHub.Core/Interfaces/Manifest/IContentManifestFactory.cs`

Factories create proper `ContentManifest` objects after downloading, handling publisher-specific logic.

| Factory | Publisher | Features |
|---------|-----------|----------|
| `ModDBManifestFactory` | ModDB | ID format: `1.YYYYMMDD.moddb-{author}.{type}.{name}` |
| `CNCLabsManifestFactory` | CNC Labs | Map-specific metadata |
| `AODMapsManifestFactory` | AOD Maps | Referer header handling |
| `GitHubManifestFactory` | GitHub | Release asset handling |
| `SuperHackersManifestFactory` | The Super Hackers | Multi-game releases (Generals + ZH) |

---

## Archive Handling

The `ContentManifestBuilder.AddDownloadedFileAsync()` method automatically handles archives:

```mermaid
flowchart TD
    DL["Download to Temp"] --> CHECK{"Is Archive?<br/>(by file signature)"}
    CHECK -->|"Yes (ZIP/RAR/7z)"| EXTRACT["Extract All Files"]
    EXTRACT --> HASH["Hash Each File"]
    HASH --> CAS["Store in CAS"]
    CAS --> MANIFEST["Add to Manifest<br/>as ContentAddressable"]

    CHECK -->|"No"| HASHSINGLE["Hash Single File"]
    HASHSINGLE --> CASSINGLE["Store in CAS"]
    CASSINGLE --> MANIFESTSINGLE["Add Single Entry"]
```

**Supported formats**: ZIP, RAR, 7z, TAR, GZ (via SharpCompress library)

**Detection**: By file signature (magic bytes), NOT file extension

---

## ContentManifest Builder

**Location**: `GenHub/Features/Manifest/ContentManifestBuilder.cs`

The fluent builder API for manifest creation:

```csharp
var manifest = manifestBuilder
    .WithBasicInfo(publisherId, contentName, manifestVersion)
    .WithContentType(ContentType.Mod, GameType.ZeroHour)
    .WithPublisher(
        name: "ModDB - Author Name",
        website: "https://moddb.com",
        publisherType: "moddb-author")
    .WithMetadata(
        description: details.Description,
        tags: ["mod", "zerohour"],
        iconUrl: details.PreviewImage)
    .Build();

// Add downloaded file (handles archive extraction automatically)
await manifest.AddDownloadedFileAsync(
    relativePath: "content.zip",
    downloadUrl: "https://example.com/download",
    refererUrl: detailPageUrl,  // For sites requiring referer
    userAgent: customUserAgent); // Triggers Playwright if set
```

### Key Methods

| Method | Purpose |
|--------|---------|
| `AddDownloadedFileAsync()` | Downloads, extracts archives, stores in CAS |
| `AddFilesFromDirectoryAsync()` | Scans directory, hashes files, adds to manifest |
| `AddLocalFileAsync()` | Adds existing local file |
| `AddContentAddressableFileAsync()` | Adds CAS reference by hash |
| `AddDependency()` | Adds content dependency |

---

## Manifest ID System

**Documentation**: [manifest-id-system.md](../dev/manifest-id-system.md)

IDs follow a deterministic format:

```
{version}.{userVersion}.{publisherId}.{contentType}.{contentName}
```

**Examples**:

- `1.20190826.moddb-hanpatch.mod.hanpatchv32` - ModDB mod
- `1.0.zerohour.gameinstallation` - Base game

---

## Downloads View Integration

### User Flow

```mermaid
sequenceDiagram
    actor User
    participant SB as PublisherSidebar
    participant VM as DownloadsBrowserViewModel
    participant D as Discoverer
    participant UI as ContentGrid

    User->>SB: Select "ModDB"
    SB->>VM: SetPublisher(ModDB)
    VM->>D: DiscoverAsync(query)
    D-->>VM: ContentDiscoveryResult
    VM->>UI: Update ContentItems
    User->>UI: Click content card
    UI->>VM: OpenDetail(item)
```

### Acquisition Flow

```mermaid
sequenceDiagram
    actor User
    participant Detail as ContentDetailView
    participant CO as ContentOrchestrator
    participant Resolver as ContentResolver
    participant Factory as ManifestFactory
    participant CAS as CAS Service
    participant Pool as ManifestPool

    User->>Detail: Click "Download"
    Detail->>CO: AcquireContentAsync(item)
    CO->>Resolver: ResolveAsync(searchResult)
    Resolver-->>CO: Full details + download URL
    CO->>Factory: CreateManifestAsync(details)
    Factory->>Factory: AddDownloadedFileAsync()
    Factory->>CAS: StoreContentAsync(files)
    Factory-->>CO: ContentManifest
    CO->>Pool: AddManifest(manifest)
    CO-->>Detail: Success
```

---

## Per-Publisher Implementation Checklist

To add support for a new publisher:

### 1. Create Constants

```csharp
// GenHub.Core/Constants/MyPublisherConstants.cs
public static class MyPublisherConstants
{
    public const string PublisherPrefix = "mypub";
    public const string PublisherName = "My Publisher";
    public const string PublisherWebsite = "https://mypub.example.com";
}
```

### 2. Create Discoverer

```csharp
public class MyPublisherDiscoverer : IContentDiscoverer
{
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query, CancellationToken ct)
    {
        // 1. Fetch catalog from source
        // 2. Parse into ContentSearchResult objects
        // 3. Apply query filters
        return OperationResult<ContentDiscoveryResult>.CreateSuccess(
            new ContentDiscoveryResult { Items = results });
    }
}
```

### 3. Create Resolver (if needed)

```csharp
public class MyPublisherResolver : IContentResolver
{
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult item, CancellationToken ct)
    {
        // Fetch detail page, build full manifest
    }
}
```

### 4. Create Manifest Factory

```csharp
public class MyPublisherManifestFactory : IContentManifestFactory
{
    public bool CanHandle(ContentManifest manifest) =>
        manifest.Publisher.Name.Contains("My Publisher");

    public async Task<ContentManifest> CreateManifestAsync(...)
    {
        // Build manifest with AddDownloadedFileAsync()
    }
}
```

### 5. Register in DI

```csharp
// ContentPipelineModule.cs
services.AddTransient<IContentDiscoverer, MyPublisherDiscoverer>();
services.AddTransient<IContentManifestFactory, MyPublisherManifestFactory>();
```

---

## Related Documentation

- [Provider Configuration](./provider-configuration.md) - Data-driven provider settings
- [Discovery Flow](../FlowCharts/Discovery-Flow.md) - Visual discovery workflow
- [Manifest ID System](../dev/manifest-id-system.md) - ID generation rules
- [Manifest Factories](./publisher-manifest-factories.md) - Factory pattern details
