---
title: Publisher Configuration
description: Data-driven publisher configuration for flexible content pipeline customization
---

# Publisher Configuration

GenHub uses **data-driven publisher configuration** to externalize content source settings into JSON files. This enables runtime configuration of endpoints, timeouts, catalog parsing, and publisher behavior without code changes.

## File Locations

Publisher definition files are loaded from two locations:

| Location | Path | Purpose |
|----------|------|---------|
| **Bundled** | `{AppDir}/Publishers/*.publisher.json` | Official publishers shipped with the app |
| **User** | `{AppData}/GenHub/Publishers/*.publisher.json` | User-customized or additional publishers |

**Loading Priority**: User publishers with matching `publisherId` override bundled publishers, allowing customization without modifying app files.

**Platform Paths**:

- Windows: `C:\Users\{User}\AppData\Roaming\GenHub\Publishers\`
- Linux: `~/.config/GenHub/Publishers/`
- macOS: `~/Library/Application Support/GenHub/Publishers/`

## Publisher Definition Schema

Each publisher is defined in a `*.publisher.json` file:

```json
{
  "publisherId": "community-outpost",
  "publisherType": "communityoutpost",
  "displayName": "Community Outpost",
  "description": "Official patches, tools, and addons from GenPatcher",
  "iconColor": "#2196F3",
  "providerType": "Static",
  "catalogFormat": "genpatcher-dat",
  "enabled": true,
  "endpoints": {
    "catalogUrl": "https://legi.cc/gp2/dl.dat",
    "websiteUrl": "https://legi.cc",
    "supportUrl": "https://legi.cc/patch",
    "custom": {
      "patchPageUrl": "https://legi.cc/patch",
      "gentoolWebsite": "https://gentool.net"
    }
  },
  "mirrorPreference": ["legi.cc", "gentool.net"],
  "targetGame": "ZeroHour",
  "defaultTags": ["community", "genpatcher"],
  "timeouts": {
    "catalogTimeoutSeconds": 30,
    "contentTimeoutSeconds": 300
  }
}
```

### Field Reference

| Field | Type | Usage |
|-------|------|-------|
| `publisherId` | string | Unique identifier used by `IPublisherDefinitionLoader.GetPublisher()` to retrieve the publisher |
| `publisherType` | string | Used in manifest ID generation (e.g., "communityoutpost" → `communityoutpost:gentool`) |
| `displayName` | string | Shown in UI publisher listings and content source headers |
| `description` | string | Shown in publisher detail views and tooltips |
| `iconColor` | string | Used to color publisher icons in the content browser |
| `providerType` | enum | `Static` (fixed publisher) or `Dynamic` (authors as publishers) |
| `catalogFormat` | string | Used by `ICatalogParserFactory.GetParser()` to resolve the correct catalog parser |
| `enabled` | boolean | Controls whether publisher is returned by `GetAllPublishers()` |
| `endpoints` | object | URL configuration used by discoverers, resolvers, and deliverers |
| `mirrorPreference` | string[] | Used by catalog parsers to order download URLs by mirror name |
| `targetGame` | enum? | Used to filter content by game in discovery and manifest building |
| `defaultTags` | string[] | Applied to all content from this publisher in `ContentSearchResult` |
| `timeouts` | object | Used to configure HTTP client timeouts in discoverers |

### Endpoints Object

```json
{
  "catalogUrl": "https://example.com/catalog.json",
  "websiteUrl": "https://example.com",
  "supportUrl": "https://example.com/help",
  "custom": {
    "anyCustomEndpoint": "https://example.com/custom"
  }
}
```

**Accessing Endpoints in Code**:

```csharp
// Standard endpoints
var catalogUrl = publisher.Endpoints.CatalogUrl;
var website = publisher.Endpoints.WebsiteUrl;

// Custom endpoints (case-insensitive key lookup)
var patchPage = publisher.Endpoints.GetEndpoint("patchPageUrl");
var customApi = publisher.Endpoints.GetEndpoint("customApiUrl");
```

## Catalog Parser System

The `catalogFormat` field drives a pluggable catalog parsing system. Each format has a dedicated parser that transforms raw catalog data into `ContentSearchResult` objects.

### How It Works

1. **Discovery** - `CommunityOutpostDiscoverer` fetches catalog from `publisher.Endpoints.CatalogUrl`
2. **Parser Resolution** - `ICatalogParserFactory.GetParser(publisher.CatalogFormat)` returns the correct parser
3. **Parsing** - Parser transforms catalog content, using static registry classes for metadata lookup

```csharp
// In CommunityOutpostDiscoverer.DiscoverAsync():
var parser = _catalogParserFactory.GetParser(publisher.CatalogFormat);
var results = await parser.ParseAsync(catalogContent, publisher, cancellationToken);
```

### ICatalogParser Interface

```csharp
public interface ICatalogParser
{
    /// <summary>
    /// Format identifier matching publisher.CatalogFormat (e.g., "genpatcher-dat").
    /// </summary>
    string CatalogFormat { get; }

    /// <summary>
    /// Parses catalog content into ContentSearchResults using publisher config.
    /// Metadata is sourced from static registry classes (e.g., GenPatcherContentRegistry).
    /// </summary>
    Task<OperationResult<IEnumerable<ContentSearchResult>>> ParseAsync(
        string catalogContent,
        PublisherDefinition publisher,
        CancellationToken cancellationToken = default);
}
```

### Built-in Catalog Formats

| Format ID | Parser | Description |
|-----------|--------|-------------|
| `genpatcher-dat` | `GenPatcherDatCatalogParser` | Parses GenPatcher's `dl.dat` format with pipe-delimited fields |

### Content Metadata

Content metadata (display names, descriptions, categories) is provided by domain-specific registry classes
such as `GenPatcherContentRegistry`. These are static classes that provide metadata lookup by content code:

```json
{
  "items": [
    {
      "code": "gtol",
      "displayName": "GenTool",
      "description": "GenTool is a helper application for Generals and Zero Hour",
      "category": "Tool",
      "targetGame": "ZeroHour",
      "version": "7.7",
      "tags": ["tool", "gentool", "utility"]
    }
  ],
  "patchCodePatterns": [
    {
      "pattern": "^1(\\d{2})([a-z])$",
      "displayNameTemplate": "Patch 1.{0} ({1})",
      "descriptionTemplate": "Official patch version 1.{0} for {2}",
      "targetGame": "dynamic"
    }
  ],
  "languageMappings": {
    "e": { "code": "en", "displayName": "English" },
    "d": { "code": "de", "displayName": "German" },
    "b": { "code": "pt-BR", "displayName": "Portuguese (Brazil)" }
  }
}
```

### Adding a New Catalog Format

1. **Create Parser** - Implement `ICatalogParser` with your format logic
2. **Register in DI** - Add to `ContentPipelineModule.cs`:

   ```csharp
   services.AddTransient<ICatalogParser, MyNewCatalogParser>();
   ```

3. **Create Publisher JSON** - Reference your format in `catalogFormat`

Example parser skeleton:

```csharp
public class MyNewCatalogParser : ICatalogParser
{
    public string CatalogFormat => "my-format";

    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> ParseAsync(
        string catalogContent,
        PublisherDefinition publisher,
        CancellationToken cancellationToken = default)
    {
        // Parse catalogContent using publisher.Endpoints for URLs
        // Look up metadata from a static registry class
        // Return ContentSearchResult collection
    }
}
```

## Architecture

### Loading Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    Application Startup                          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│              PublisherDefinitionLoader.GetPublisher()           │
│         (Auto-loads on first access if not initialized)         │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
┌──────────────────────────┐    ┌──────────────────────────┐
│  Load Bundled Publishers │    │   Load User Publishers   │
│   {AppDir}/Publishers/   │    │  {AppData}/GenHub/Pub.   │
└──────────────────────────┘    └──────────────────────────┘
              │                               │
              └───────────────┬───────────────┘
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                  Merge (User overrides Bundled)                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     In-Memory Publisher Cache                   │
└─────────────────────────────────────────────────────────────────┘
```

### Content Pipeline Integration

The publisher definition flows through the content pipeline:

```
┌─────────────────────┐
│  ContentProvider    │──── GetPublisherDefinition() ────┐
└─────────────────────┘                                  │
         │                                               ▼
         │                              ┌────────────────────────────┐
         ▼                              │ PublisherDefinitionLoader  │
┌─────────────────────┐                 │ GetPublisher(publisherId)  │
│    Discoverer       │◄────────────────└────────────────────────────┘
│ DiscoverAsync(pub)  │
└─────────────────────┘
         │
         ▼
┌─────────────────────┐
│     Resolver        │
│ ResolveAsync(pub)   │
└─────────────────────┘
         │
         ▼
┌─────────────────────┐
│     Deliverer       │
│  (uses manifest)    │
└─────────────────────┘
```

## Implementation Example: Community Outpost

### Publisher Class

The publisher class injects `IPublisherDefinitionLoader` and caches the definition:

```csharp
public class CommunityOutpostProvider : BaseContentProvider
{
    private readonly IPublisherDefinitionLoader _definitionLoader;
    private PublisherDefinition? _cachedPublisherDefinition;

    public CommunityOutpostProvider(
        IPublisherDefinitionLoader definitionLoader,
        IContentDiscoverer discoverer,
        IContentResolver resolver,
        IContentDeliverer deliverer,
        IContentValidator validator,
        ILogger<CommunityOutpostProvider> logger)
        : base(validator, logger)
    {
        _definitionLoader = definitionLoader;
        // ... store other dependencies
    }

    protected override PublisherDefinition? GetPublisherDefinition()
    {
        // Cache the publisher definition for performance
        _cachedPublisherDefinition ??= _definitionLoader.GetPublisher(PublisherId);
        return _cachedPublisherDefinition;
    }
}
```

### Discoverer Usage

Discoverers receive the publisher definition and use it for endpoint configuration:

```csharp
public class CommunityOutpostDiscoverer : IContentDiscoverer
{
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        PublisherDefinition? publisher,
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // Get configuration from publisher definition with fallback to constants
        var catalogUrl = publisher?.Endpoints.CatalogUrl
            ?? CommunityOutpostConstants.CatalogUrl;

        var patchPageUrl = publisher?.Endpoints.GetEndpoint("patchPageUrl")
            ?? CommunityOutpostConstants.PatchPageUrl;

        var timeout = TimeSpan.FromSeconds(
            publisher?.Timeouts.CatalogTimeoutSeconds ?? 30);

        _logger.LogDebug(
            "Using endpoints - CatalogUrl: {CatalogUrl}, Timeout: {Timeout}s",
            catalogUrl,
            timeout.TotalSeconds);

        // Fetch catalog and discover content...
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = timeout;

        var catalogContent = await client.GetStringAsync(catalogUrl, cancellationToken);
        // Parse and return results...
    }
}
```

### Resolver Usage

Resolvers use publisher configuration for manifest creation:

```csharp
public class CommunityOutpostResolver : IContentResolver
{
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        PublisherDefinition? publisher,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        // Get endpoints from publisher definition
        var websiteUrl = publisher?.Endpoints.WebsiteUrl
            ?? CommunityOutpostConstants.PublisherWebsite;

        var patchPageUrl = publisher?.Endpoints.GetEndpoint("patchPageUrl")
            ?? CommunityOutpostConstants.PatchPageUrl;

        // Build manifest using configured endpoints
        var manifest = _manifestBuilder
            .WithPublisher(
                name: CommunityOutpostConstants.PublisherName,
                website: websiteUrl,
                supportUrl: patchPageUrl,
                publisherType: CommunityOutpostConstants.PublisherType)
            .WithMetadata(
                description: contentMetadata.Description,
                changelogUrl: patchPageUrl)
            // ... continue building manifest
            .Build();

        return OperationResult<ContentManifest>.CreateSuccess(manifest);
    }
}
```

## IPublisherDefinitionLoader Interface

```csharp
public interface IPublisherDefinitionLoader
{
    /// <summary>
    /// Gets a specific publisher definition by ID. Auto-loads on first access.
    /// </summary>
    PublisherDefinition? GetPublisher(string publisherId);

    /// <summary>
    /// Gets all enabled publisher definitions.
    /// </summary>
    IEnumerable<PublisherDefinition> GetAllPublishers();

    /// <summary>
    /// Gets publishers filtered by type (Static or Dynamic).
    /// </summary>
    IEnumerable<PublisherDefinition> GetPublishersByType(ProviderType providerType);

    /// <summary>
    /// Loads all publisher definitions asynchronously.
    /// </summary>
    Task<OperationResult<IEnumerable<PublisherDefinition>>> LoadPublishersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads all publishers (for hot-reload scenarios).
    /// </summary>
    Task<OperationResult<bool>> ReloadPublishersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a runtime-defined publisher (not from file).
    /// </summary>
    OperationResult<bool> AddCustomPublisher(PublisherDefinition publisher);

    /// <summary>
    /// Removes a runtime-added publisher.
    /// </summary>
    OperationResult<bool> RemoveCustomPublisher(string publisherId);
}
```

## Publisher Types

### Static Publishers

Static publishers have a fixed publisher identity. All content discovered from the source is attributed to a single known publisher.

**Examples**: Community Outpost, AODMaps, Generals Online, TheSuperHackers

```json
{
  "providerType": "Static",
  "publisherType": "communityoutpost"
}
```

### Dynamic Publishers

Dynamic publishers support multiple publishers where content authors become individual publishers. Each discovered author gets their own publisher identity.

**Examples**: GitHub (repo owners), ModDB (mod authors), CNCLabs (map authors)

```json
{
  "providerType": "Dynamic",
  "discovery": {
    "method": "github-topic",
    "topics": ["cnc-generals", "zero-hour-mod"],
    "authorsAsPublishers": true
  }
}
```

#### AODMaps Configuration

AODMaps uses a static publisher configuration to map its custom catalog format:

```json
{
  "publisherId": "aodmaps",
  "publisherType": "aodmaps",
  "displayName": "Age of Defense Maps",
  "providerType": "Static",
  "catalogFormat": "html-scraping",
  "endpoints": {
    "catalogUrl": "https://aodmaps.com",
    "websiteUrl": "https://aodmaps.com"
  }
}
```

## Benefits

| Feature | Description |
|---------|-------------|
| **Runtime Changes** | Modify endpoints without recompilation |
| **User Customization** | Users can override bundled publishers in AppData |
| **Mirror Support** | Built-in failover across multiple download mirrors |
| **Hot Reload** | `ReloadPublishersAsync()` for runtime updates |
| **Extensibility** | Add new publishers by dropping in JSON files |
| **Environment Config** | Different URLs for dev/staging/production |

## Testing

### Unit Testing with Mock Publishers

```csharp
[Fact]
public async Task Discoverer_UsesPublisherEndpoints()
{
    // Arrange
    var publisher = new PublisherDefinition
    {
        PublisherId = "test-publisher",
        DisplayName = "Test Publisher",
        Endpoints = new PublisherEndpoints
        {
            CatalogUrl = "https://test.example.com/catalog"
        },
        Timeouts = new PublisherTimeouts
        {
            CatalogTimeoutSeconds = 10
        }
    };

    var mockHttp = new Mock<IHttpClientFactory>();
    var discoverer = new CommunityOutpostDiscoverer(mockHttp.Object, _logger);

    // Act
    await discoverer.DiscoverAsync(publisher, query, CancellationToken.None);

    // Assert
    mockHttp.Verify(x => x.CreateClient(), Times.Once);
    // Verify the configured URL was used...
}
```

### Integration Testing with Test Publisher Files

```csharp
[Fact]
public async Task Loader_LoadsFromBothDirectories()
{
    // Arrange
    var bundledDir = Path.Combine(_tempDir, "bundled");
    var userDir = Path.Combine(_tempDir, "user");

    Directory.CreateDirectory(bundledDir);
    Directory.CreateDirectory(userDir);

    // Create bundled publisher
    File.WriteAllText(
        Path.Combine(bundledDir, "test.publisher.json"),
        """{"publisherId": "test", "displayName": "Bundled"}""");

    // Create user override
    File.WriteAllText(
        Path.Combine(userDir, "test.publisher.json"),
        """{"publisherId": "test", "displayName": "User Override"}""");

    var loader = new PublisherDefinitionLoader(_logger, bundledDir, userDir);

    // Act
    var publisher = loader.GetPublisher("test");

    // Assert - User override wins
    Assert.Equal("User Override", publisher?.DisplayName);
}
```

## File Reference

| Component | Path |
|-----------|------|
| **Core Interfaces** | |
| IPublisherDefinitionLoader | `GenHub.Core/Interfaces/Publishers/IPublisherDefinitionLoader.cs` |
| ICatalogParser | `GenHub.Core/Interfaces/Publishers/ICatalogParser.cs` |
| ICatalogParserFactory | `GenHub.Core/Interfaces/Publishers/ICatalogParserFactory.cs` |
| **Core Services** | |
| PublisherDefinitionLoader | `GenHub.Core/Services/Publishers/PublisherDefinitionLoader.cs` |
| CatalogParserFactory | `GenHub.Core/Services/Publishers/CatalogParserFactory.cs` |
| **Models** | |
| PublisherDefinition | `GenHub.Core/Models/Publishers/PublisherDefinition.cs` |
| GenPatcherContentRegistry | `GenHub/Features/Content/Models/GenPatcherContentRegistry.cs` |
| **Publisher Configurations** | |
| Community Outpost Publisher | `GenHub/Publishers/communityoutpost.publisher.json` |
| **Community Outpost Implementation** | |
| CommunityOutpostDiscoverer | `GenHub/Features/Content/Services/CommunityOutpost/CommunityOutpostDiscoverer.cs` |
| CommunityOutpostResolver | `GenHub/Features/Content/Services/CommunityOutpost/CommunityOutpostResolver.cs` |
| CommunityOutpostProvider | `GenHub/Features/Content/Services/CommunityOutpost/CommunityOutpostProvider.cs` |
| GenPatcherDatCatalogParser | `GenHub/Features/Content/Services/CommunityOutpost/GenPatcherDatCatalogParser.cs` |
