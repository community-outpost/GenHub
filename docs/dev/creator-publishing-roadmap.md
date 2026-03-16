# Creator Publishing Infrastructure Roadmap

**Version**: 1.0.0
**Last Updated**: 2026-01-04
**Status**: PLANNING - Awaiting Review

> [!IMPORTANT]
> This document is designed to be **shared across AI chat sessions**. It contains all architectural decisions, integration patterns, and implementation details needed to continue this work.

---

## Table of Contents

1. [Problem Statement](#problem-statement)
2. [Architecture Overview](#architecture-overview)
3. [Integration with Existing Pipeline](#integration-with-existing-pipeline)
4. [Component Design](#component-design)
5. [Mod Maker Interface](#mod-maker-interface)
6. [Implementation Phases](#implementation-phases)
7. [File Locations](#file-locations)

---

## Problem Statement

GenHub's current content pipeline requires **hardcoded implementations** for each publisher:

- 8 publishers currently hardcoded in `GetDiscovererForPublisher`
- Each needs: Discoverer, Resolver, ManifestFactory, FilterViewModel, Constants
- No way for creators to self-publish without code changes

**Goal**: Enable any creator to publish content by hosting a JSON catalog file, with GenHub subscribing to their feed.

---

## Architecture Overview

### Current Pipeline (Per-Publisher)

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐    ┌───────────────┐
│ XXXDiscoverer   │ →  │ XXXResolver      │ →  │ XXXManifestFac  │ →  │ CAS + Pool    │
│ (scrape/API)    │    │ (get details)    │    │ (build manifest)│    │ (store)       │
└─────────────────┘    └──────────────────┘    └─────────────────┘    └───────────────┘
     ↑                       ↑                       ↑
     │                       │                       │
   Unique per publisher - requires code changes for each new source
```

### New Pipeline (Generic + Subscription-Based)

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                           SUBSCRIPTION LAYER                                     │
│  ┌─────────────────────────┐  ┌─────────────────────────┐                       │
│  │ IPublisherSubscription  │  │ Subscription Manager    │                       │
│  │ Store                   │  │ (file/URI/QR import)    │                       │
│  └───────────┬─────────────┘  └────────────┬────────────┘                       │
└──────────────┼──────────────────────────────┼───────────────────────────────────┘
               │                              │
               ▼                              ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                        GENERIC PUBLISHER PIPELINE                                │
│                                                                                  │
│  ┌────────────────────┐   ┌────────────────────┐   ┌────────────────────────┐   │
│  │ GenericCatalog     │ → │ GenericCatalog     │ → │ ContentManifestBuilder │   │
│  │ Discoverer         │   │ Resolver           │   │ (EXISTING)             │   │
│  │ (fetch & parse)    │   │ (select version)   │   └────────────────────────┘   │
│  └────────────────────┘   └────────────────────┘                                │
│            ↑                     ↑                                              │
│            │                     │                                              │
│       Uses IPublisher        Uses IVersion                                      │
│       CatalogParser          Selector                                           │
└─────────────────────────────────────────────────────────────────────────────────┘
```

**Key Insight**: We create **ONE** generic discoverer/resolver pair that works for **ALL** subscribed publishers, rather than writing custom code for each.

---

## Integration with Existing Pipeline

### How Publishers Map to Discoverers (Current)

**File**: `DownloadsBrowserViewModel.cs:448-463`

```csharp
private IContentDiscoverer? GetDiscovererForPublisher(string publisherId)
{
    return publisherId switch
    {
        PublisherTypeConstants.GeneralsOnline => discoverers.OfType<GeneralsOnlineDiscoverer>().FirstOrDefault(),
        PublisherTypeConstants.TheSuperHackers => discoverers.OfType<GitHubReleasesDiscoverer>().FirstOrDefault(),
        ModDBConstants.PublisherType => discoverers.OfType<ModDBDiscoverer>().FirstOrDefault(),
        // ... 5 more publishers
        _ => null,  // <-- THIS IS WHERE SUBSCRIBED PUBLISHERS WILL GO
    };
}
```

### Integration Pattern for Subscribed Publishers

```csharp
private IContentDiscoverer? GetDiscovererForPublisher(string publisherId)
{
    return publisherId switch
    {
        // Existing hardcoded publishers...

        // NEW: Check if this is a subscribed publisher
        _ => GetSubscribedPublisherDiscoverer(publisherId),
    };
}

private IContentDiscoverer? GetSubscribedPublisherDiscoverer(string publisherId)
{
    // 1. Check if user is subscribed to this publisher
    var subscription = _subscriptionStore.GetSubscription(publisherId);
    if (subscription == null) return null;

    // 2. Return the generic catalog discoverer configured for this subscription
    var discoverer = _serviceProvider.GetRequiredService<GenericCatalogDiscoverer>();
    discoverer.Configure(subscription);
    return discoverer;
}
```

### Download Content Flow (Existing)

**File**: `DownloadsBrowserViewModel.cs:562-735`

```
User clicks Download
    │
    ▼
Get Resolver by ResolverId ← ContentSearchResult.ResolverId
    │
    ▼
resolver.ResolveAsync() → ContentManifest
    │
    ▼
Download RemoteFiles to temp dir
    │
    ▼
manifestPool.AddManifestAsync(manifest, tempDir)
    │
    └──► ContentStorageService stores in CAS
```

**Key Point**: The generic resolver just needs to produce a valid `ContentManifest`. The existing storage infrastructure handles everything else.

---

## Component Design

### 1. Catalog Schema (Creator-Authored JSON)

Creators host this on GitHub Releases, personal CDN, or any HTTP endpoint:

```json
{
  "$schema": "https://genhub.io/schemas/catalog/v1",
  "publisher": {
    "id": "my-mods",
    "name": "My Awesome Mods",
    "website": "https://github.com/myname",
    "avatarUrl": "https://github.com/myname.png"
  },
  "content": [
    {
      "id": "super-balance-mod",
      "name": "Super Balance Mod",
      "contentType": "Mod",
      "targetGame": "ZeroHour",
      "releases": [
        {
          "version": "1.0.0",
          "isLatest": true,
          "artifacts": [
            {
              "downloadUrl": "https://github.com/.../SuperBalanceMod-1.0.0.zip",
              "sha256": "abc123..."
            }
          ]
        }
      ]
    }
  ]
}
```

### 2. GenericCatalogDiscoverer

**Purpose**: Fetches and parses any catalog that follows the GenHub schema.

```csharp
public class GenericCatalogDiscoverer : IContentDiscoverer
{
    private PublisherSubscription? _subscription;

    public string SourceName => _subscription?.PublisherId ?? "generic";
    public string ResolverId => "generic-catalog";  // Maps to GenericCatalogResolver

    public void Configure(PublisherSubscription subscription)
    {
        _subscription = subscription;
    }

    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query, CancellationToken ct)
    {
        // 1. Fetch catalog from _subscription.CatalogUrl
        // 2. Parse using IPublisherCatalogParser
        // 3. Convert to ContentSearchResult[]
        // 4. Apply IVersionSelector (latest only by default)
    }
}
```

### 3. GenericCatalogResolver

**Purpose**: Converts a `ContentSearchResult` into a `ContentManifest` with downloadable files.

```csharp
public class GenericCatalogResolver : IContentResolver
{
    public string ResolverId => "generic-catalog";

    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult searchResult, CancellationToken ct)
    {
        // 1. Extract release info from searchResult.ResolverMetadata
        // 2. Build manifest using IContentManifestBuilder
        // 3. Add artifact as RemoteDownload file
        // 4. Return manifest (ContentManifestBuilder.AddDownloadedFileAsync
        //    handles archive extraction + CAS storage)
    }
}
```

### 4. Manifest Creation for Archives

**Existing Behavior** (no changes needed):

`ContentManifestBuilder.AddDownloadedFileAsync()` automatically:

1. Downloads file to temp directory
2. Detects if it's an archive (ZIP, RAR, 7z) using SharpCompress
3. If archive: extracts all files, stores each in CAS, adds to manifest
4. If not archive: stores single file in CAS, adds to manifest
5. Cleans up temp files

**File**: `ContentManifestBuilder.cs:488-716`

```csharp
// Archive detection and extraction (simplified)
using (var archive = ArchiveFactory.Open(stream))
{
    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
    {
        entry.WriteToDirectory(extractionTempDir, extractOptions);
        await _casService.StoreContentAsync(extractedFilePath, fileHash, ct);
        _manifest.Files.Add(new ManifestFile { ... });
    }
}
```

---

## Mod Maker Interface

### Option 1: Manual JSON Authoring

Creators write `catalog.json` by hand following the schema.

**Pros**: Simple, no tooling required
**Cons**: Error-prone, tedious for version updates

### Option 2: CLI Tool (Future)

```bash
# Initialize a new catalog
genhub-cli init --publisher "My Name"

# Add a release (scans files, computes hashes)
genhub-cli add-release \
  --content-id "my-mod" \
  --version "1.0.0" \
  --file "./releases/MyMod-1.0.0.zip"

# Publish (uploads catalog to configured destination)
genhub-cli publish --to github-release
```

### Option 3: Web Wizard (Future)

A web-based form that:

1. Collects publisher info
2. Accepts file uploads or URLs
3. Generates `catalog.json` for download

---

## Implementation Phases

### Phase 0: Preparation

- [ ] Create this roadmap document
- [ ] Create shared design document in `/docs/dev/`

### Phase 1: Core Models & Interfaces

**Goal**: Define data contracts without implementation

| File | Description |
|------|-------------|
| `Core/Models/Providers/PublisherCatalog.cs` | Root catalog model |
| `Core/Models/Providers/CatalogContentItem.cs` | Content entry |
| `Core/Models/Providers/ContentRelease.cs` | Version-specific release |
| `Core/Models/Providers/ReleaseArtifact.cs` | Downloadable file |
| `Core/Models/Providers/PublisherSubscription.cs` | Local subscription record |
| `Core/Interfaces/Providers/IPublisherSubscriptionStore.cs` | Subscription CRUD |
| `Core/Interfaces/Providers/IPublisherCatalogParser.cs` | Catalog parsing |
| `Core/Interfaces/Providers/IVersionSelector.cs` | Version filtering |

### Phase 2: Subscription System

**Goal**: Enable adding/removing publisher subscriptions

- Implement `PublisherSubscriptionStore` (file-based storage)
- Add subscribed publishers to `DownloadsBrowserViewModel.InitializePublishers()`
- Handle `genhub://subscribe?url=...` URI scheme

### Phase 3: Generic Pipeline

**Goal**: Discover and download content from subscribed catalogs

- Implement `GenericCatalogDiscoverer`
- Implement `GenericCatalogResolver`
- Integrate with existing `ContentManifestBuilder.AddDownloadedFileAsync()`
- Add caching for fetched catalogs

### Phase 4: Version Filtering

**Goal**: Show only latest versions by default

- Implement `IVersionSelector` with policies:
  - `LatestStableOnly` (default)
  - `AllVersions` (opt-in)
  - `IncludePrereleases`
- Add "Show Older Versions" toggle to Downloads UI

### Phase 5: UI Polish

**Goal**: Premium UX for subscribed publishers

- Rich content detail view (banner, screenshots, video)
- Publisher subscription confirmation dialog
- Subscription management in Settings

### Phase 6: Creator Tooling (Future)

**Goal**: Make it easy for creators to publish

- CLI tool for catalog generation
- Web wizard for non-technical creators
- GitHub Action for automated catalog updates

---

## File Locations

### New Files to Create

```
GenHub.Core/
├── Interfaces/Providers/
│   ├── IPublisherSubscriptionStore.cs
│   ├── IPublisherCatalogParser.cs
│   └── IVersionSelector.cs
├── Models/Providers/
│   ├── PublisherCatalog.cs
│   ├── CatalogContentItem.cs
│   ├── ContentRelease.cs
│   ├── ReleaseArtifact.cs
│   ├── CatalogDependency.cs
│   ├── PublisherReferral.cs
│   ├── PublisherSubscription.cs
│   └── PublisherSubscriptionCollection.cs
└── Models/Enums/
    └── TrustLevel.cs

GenHub/Features/Content/Services/Catalog/
├── GenericCatalogDiscoverer.cs
├── GenericCatalogResolver.cs
├── JsonPublisherCatalogParser.cs
├── VersionSelector.cs
└── PublisherSubscriptionStore.cs

GenHub/Features/Downloads/ViewModels/Filters/
└── SubscribedPublisherFilterViewModel.cs
```

### Files to Modify

| File | Changes |
|------|---------|
| `DownloadsBrowserViewModel.cs` | Add subscribed publishers to sidebar, modify `GetDiscovererForPublisher` |
| `ContentPipelineModule.cs` | Register new services |
| `ManifestConstants.cs` | Add `CatalogSchemaVersion` |
| `DownloadsBrowserView.axaml` | "Show Older Versions" toggle |

---

## Questions for User Review

1. **Trust Model**: Should we implement a "verified publisher" badge system for known/trusted publishers?

2. **URI Scheme**: Confirm `genhub://subscribe?url=<catalog_url>` as the subscription protocol.

3. **Versioning Default**: Confirm "Latest Stable Only" as default with opt-in for older versions.

4. **CLI Tooling**: Should CLI be bundled with GenHub or a separate download?

---

## Next Steps

1. User reviews this document
2. Proceed with Phase 1 implementation
3. Create unit tests alongside models
4. Request user testing of subscription flow
