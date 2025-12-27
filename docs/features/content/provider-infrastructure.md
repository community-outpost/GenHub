---
title: Provider Infrastructure Architecture
description: Clean architecture for implementing content providers (CommunityOutpost, GeneralsOnline, GitHub, ModDB, etc.)
---

# Provider Infrastructure Architecture

This document describes the clean, data-driven architecture for implementing content providers in GenHub.

## Architecture Overview

```
┌───────────────────────────────────────────────────────────────────────────┐
│                           PROVIDER ARCHITECTURE                           │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐   │
│  │  Provider.json   │     │   ICatalogParser │     │  Domain Registry │   │
│  │  (Configuration) │     │    (Interface)   │     │   (Metadata)     │   │
│  └────────┬─────────┘     └────────┬─────────┘     └────────┬─────────┘   │
│           │                        │                        │             │
│           ▼                        ▼                        ▼             │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │                        CONTENT DISCOVERER                           │  │
│  │  - Fetches catalog/API/HTML from endpoint                           │  │
│  │  - Uses ICatalogParser to parse response                            │  │
│  │  - Returns ContentSearchResult[] with ResolverMetadata              │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│           │                                                               │
│           ▼                                                               │
│  ┌────────────────────────────────────────────────────────────────────┐   │
│  │                        CONTENT RESOLVER                            │   │
│  │  - Takes ContentSearchResult with ResolverMetadata                 │   │
│  │  - Uses Domain Registry for additional metadata                    │   │
│  │  - Builds ContentManifest via IContentManifestBuilder              │   │
│  └────────────────────────────────────────────────────────────────────┘   │
│           │                                                               │
│           ▼                                                               │
│  ┌────────────────────────────────────────────────────────────────────┐   │
│  │                        CONTENT DELIVERER                           │   │
│  │  - Downloads files from SourceUrl                                  │   │
│  │  - Extracts archives (zip, 7z)                                     │   │
│  │  - Uses IPublisherManifestFactory for final manifest               │   │
│  └────────────────────────────────────────────────────────────────────┘   │
│                                                                           │
└───────────────────────────────────────────────────────────────────────────┘
```

## Key Principles

### 1. Provider.json is for Configuration ONLY

- Endpoints (catalog URLs, API URLs, download base URLs)
- Timeouts
- Mirrors and priority
- UI display (name, color, icon)
- **NOT for content metadata**

### 2. Metadata Comes from the Source

- **Option A**: Domain-specific registry (e.g., `GenPatcherContentRegistry`)
  - Static class with hardcoded mappings
  - Used when content codes need human-curated display names
  - Example: GenPatcher codes like "gent" → "GenTool"

- **Option B**: Parsed from the source itself
  - GitHub releases API → name, description, version from release
  - JSON API → metadata fields in response
  - HTML scraping → metadata from page content

### 3. Parser Interface is Simple

```csharp
public interface ICatalogParser
{
    string CatalogFormat { get; }
    
    Task<OperationResult<IEnumerable<ContentSearchResult>>> ParseAsync(
        string catalogContent,
        ProviderDefinition provider,
        CancellationToken cancellationToken = default);
}
```

- Parser gets raw content + provider config
- Parser returns ContentSearchResult with ResolverMetadata
- Parser sources its own metadata (from registry or parsing)

---

## Provider Types

### Static Providers

Publishers with fixed identity (e.g., CommunityOutpost, GeneralsOnline, TheSuperHackers)

| Provider | Catalog Format | Metadata Source |
|----------|---------------|-----------------|
| CommunityOutpost | `genpatcher-dat` | `GenPatcherContentRegistry` |
| GeneralsOnline | `json-api` | Parsed from JSON response |
| TheSuperHackers | `github-releases` | Parsed from GitHub API |

### Dynamic Providers

Publishers discovered from a source (e.g., GitHub Topics, ModDB authors)

| Provider | Discovery Method | Metadata Source |
|----------|-----------------|-----------------|
| GitHub Topics | Topic search API | Release metadata |
| ModDB | Search API | Mod page metadata |
| CNCLabs | Website scraping | Page content |

---

## Implementing a New Provider

### Step 1: Create Provider.json

```json
{
  "providerId": "generalsonline",
  "publisherType": "generalsonline",
  "displayName": "Generals Online",
  "description": "Official Generals Online game client releases",
  "iconColor": "#4CAF50",
  "providerType": "Static",
  "catalogFormat": "json-api",
  "endpoints": {
    "catalogUrl": "https://api.generalsonline.com/releases",
    "websiteUrl": "https://generalsonline.com",
    "supportUrl": "https://discord.gg/generalsonline"
  },
  "defaultTags": ["generalsonline", "official"],
  "targetGame": "ZeroHour",
  "timeouts": {
    "catalogTimeoutSeconds": 30,
    "contentTimeoutSeconds": 600
  },
  "enabled": true
}
```

### Step 2: Create ICatalogParser Implementation

```csharp
public class JsonApiCatalogParser : ICatalogParser
{
    public string CatalogFormat => "json-api";

    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> ParseAsync(
        string catalogContent,
        ProviderDefinition provider,
        CancellationToken cancellationToken = default)
    {
        // Parse JSON API response
        var response = JsonSerializer.Deserialize<ApiResponse>(catalogContent);
        
        var results = response.Releases.Select(release => new ContentSearchResult
        {
            Id = $"{provider.ProviderId}.{release.Id}",
            Name = release.Name,
            Description = release.Description,
            Version = release.Version,
            ContentType = ContentType.GameClient,
            TargetGame = provider.TargetGame ?? GameType.ZeroHour,
            SourceUrl = release.DownloadUrl,
            // Store metadata for resolver
            ResolverMetadata = new Dictionary<string, string>
            {
                ["releaseId"] = release.Id,
                ["checksum"] = release.Checksum,
            }
        });

        return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
    }
}
```

### Step 3: Register Parser in DI

```csharp
// In ContentPipelineModule.cs or ServiceRegistration
services.AddSingleton<ICatalogParser, JsonApiCatalogParser>();
```

### Step 4: Create Provider-Specific Discoverer (if needed)

For most cases, a generic discoverer can be created that:

1. Loads `ProviderDefinition` by ID
2. Fetches catalog from `Endpoints.CatalogUrl`
3. Gets parser from `ICatalogParserFactory` by `CatalogFormat`
4. Calls `parser.ParseAsync(content, provider)`

```csharp
public class GenericStaticProviderDiscoverer : IContentDiscoverer
{
    private readonly IProviderDefinitionLoader _providerLoader;
    private readonly ICatalogParserFactory _parserFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ProviderDefinition provider,
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        var catalogContent = await FetchCatalogAsync(provider, cancellationToken);
        
        var parser = _parserFactory.GetParser(provider.CatalogFormat);
        if (parser == null)
            return OperationResult.Failure($"No parser for format: {provider.CatalogFormat}");

        return await parser.ParseAsync(catalogContent, provider, cancellationToken);
    }
}
```

---

## Catalog Format Examples

### genpatcher-dat

```
2.13                ;;
gent  123456789 legi.cc   f/gent.dat
cbbs  987654321 legi.cc   f/cbbs.dat
```

### github-releases

```json
{
  "releases": [
    {
      "tag_name": "v1.0.0",
      "name": "Release 1.0.0",
      "body": "Changelog...",
      "assets": [
        { "name": "game-1.0.0.zip", "browser_download_url": "..." }
      ]
    }
  ]
}
```

### json-api

```json
{
  "releases": [
    {
      "id": "release-123",
      "name": "Game Client v2.0",
      "version": "2.0.0",
      "downloadUrl": "https://...",
      "checksum": "sha256:..."
    }
  ]
}
```

---

## Domain-Specific Registries

For providers with content codes that need human-readable mappings:

```csharp
public static class GenPatcherContentRegistry
{
    private static readonly Dictionary<string, GenPatcherContentMetadata> KnownContent = new()
    {
        ["gent"] = new GenPatcherContentMetadata
        {
            ContentCode = "gent",
            DisplayName = "GenTool",
            Description = "GenTool utility for Generals/Zero Hour",
            ContentType = ContentType.Addon,
            Category = GenPatcherContentCategory.Tools,
        },
        // ... more content codes
    };

    public static GenPatcherContentMetadata GetMetadata(string contentCode)
    {
        if (KnownContent.TryGetValue(contentCode.ToLowerInvariant(), out var metadata))
            return metadata;

        // Try dynamic parsing (e.g., patch codes like "108e")
        return TryParsePatchCode(contentCode) ?? CreateUnknownMetadata(contentCode);
    }
}
```

---

## Summary

| Component | Responsibility |
|-----------|---------------|
| `provider.json` | Configuration: endpoints, timeouts, UI |
| `ICatalogParser` | Parse raw catalog into ContentSearchResult |
| Domain Registry | Map codes to metadata (optional) |
| Discoverer | Orchestrate fetch → parse → filter |
| Resolver | Build ContentManifest from SearchResult |
| Deliverer | Download, extract, finalize |

This architecture allows adding new providers with minimal code:

1. Create `provider.json` for configuration
2. Create or reuse `ICatalogParser` for the catalog format
3. Optionally create domain registry for metadata
4. Register in DI

**No changes needed to:**

- Core interfaces
- Existing providers
- Manifest building
- Content delivery
