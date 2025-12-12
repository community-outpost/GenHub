---
title: Provider Configuration
description: Data-driven provider configuration for flexible content pipeline customization
---

# Provider Configuration

GenHub uses **data-driven provider configuration** to externalize content source settings into JSON files. This enables runtime configuration of endpoints, timeouts, catalog parsing, and provider behavior without code changes.

## File Locations

Provider definition files are loaded from two locations:

| Location | Path | Purpose |
|----------|------|---------|
| **Bundled** | `{AppDir}/Providers/*.provider.json` | Official providers shipped with the app |
| **User** | `{AppData}/GenHub/Providers/*.provider.json` | User-customized or additional providers |

**Loading Priority**: User providers with matching `providerId` override bundled providers, allowing customization without modifying app files.

**Platform Paths**:

- Windows: `C:\Users\{User}\AppData\Roaming\GenHub\Providers\`
- Linux: `~/.config/GenHub/Providers/`
- macOS: `~/Library/Application Support/GenHub/Providers/`

## Provider Definition Schema

Each provider is defined in a `*.provider.json` file:

```json
{
  "providerId": "community-outpost",
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
| `providerId` | string | Unique identifier used by `IProviderDefinitionLoader.GetProvider()` to retrieve the provider |
| `publisherType` | string | Used in manifest ID generation (e.g., "communityoutpost" → `communityoutpost:gentool`) |
| `displayName` | string | Shown in UI provider listings and content source headers |
| `description` | string | Shown in provider detail views and tooltips |
| `iconColor` | string | Used to color provider icons in the content browser |
| `providerType` | enum | `Static` (fixed publisher) or `Dynamic` (authors as publishers) |
| `catalogFormat` | string | Used by `ICatalogParserFactory.GetParser()` to resolve the correct catalog parser |
| `enabled` | boolean | Controls whether provider is returned by `GetAllProviders()` |
| `endpoints` | object | URL configuration used by discoverers, resolvers, and deliverers |
| `mirrorPreference` | string[] | Used by catalog parsers to order download URLs by mirror name |
| `targetGame` | enum? | Used to filter content by game in discovery and manifest building |
| `defaultTags` | string[] | Applied to all content from this provider in `ContentSearchResult` |
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
var catalogUrl = provider.Endpoints.CatalogUrl;
var website = provider.Endpoints.WebsiteUrl;

// Custom endpoints (case-insensitive key lookup)
var patchPage = provider.Endpoints.GetEndpoint("patchPageUrl");
var customApi = provider.Endpoints.GetEndpoint("customApiUrl");
```

## Catalog Parser System

The `catalogFormat` field drives a pluggable catalog parsing system. Each format has a dedicated parser that transforms raw catalog data into `ContentSearchResult` objects.

### How It Works

1. **Discovery** - `CommunityOutpostDiscoverer` fetches catalog from `provider.Endpoints.CatalogUrl`
2. **Parser Resolution** - `ICatalogParserFactory.GetParser(provider.CatalogFormat)` returns the correct parser
3. **Parsing** - Parser transforms catalog content, using static registry classes for metadata lookup

```csharp
// In CommunityOutpostDiscoverer.DiscoverAsync():
var parser = _catalogParserFactory.GetParser(provider.CatalogFormat);
var results = await parser.ParseAsync(catalogContent, provider, cancellationToken);
```

### ICatalogParser Interface

```csharp
public interface ICatalogParser
{
    /// <summary>
    /// Format identifier matching provider.CatalogFormat (e.g., "genpatcher-dat").
    /// </summary>
    string CatalogFormat { get; }

    /// <summary>
    /// Parses catalog content into ContentSearchResults using provider config.
    /// Metadata is sourced from static registry classes (e.g., GenPatcherContentRegistry).
    /// </summary>
    Task<OperationResult<IEnumerable<ContentSearchResult>>> ParseAsync(
        string catalogContent,
        ProviderDefinition provider,
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

3. **Create Provider JSON** - Reference your format in `catalogFormat`

Example parser skeleton:

```csharp
public class MyNewCatalogParser : ICatalogParser
{
    public string CatalogFormat => "my-format";

    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> ParseAsync(
        string catalogContent,
        ProviderDefinition provider,
        CancellationToken cancellationToken = default)
    {
        // Parse catalogContent using provider.Endpoints for URLs
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
│              ProviderDefinitionLoader.GetProvider()             │
│         (Auto-loads on first access if not initialized)         │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
┌──────────────────────────┐    ┌──────────────────────────┐
│   Load Bundled Providers │    │    Load User Providers   │
│   {AppDir}/Providers/    │    │  {AppData}/GenHub/Prov.  │
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
│                     In-Memory Provider Cache                    │
└─────────────────────────────────────────────────────────────────┘
```

### Content Pipeline Integration

The provider definition flows through the content pipeline:

```
┌─────────────────────┐
│  ContentProvider    │──── GetProviderDefinition() ────┐
└─────────────────────┘                                 │
         │                                              ▼
         │                              ┌───────────────────────────┐
         ▼                              │  ProviderDefinitionLoader │
┌─────────────────────┐                 │  GetProvider(providerId)  │
│    Discoverer       │◄────────────────└───────────────────────────┘
│ DiscoverAsync(prov) │
└─────────────────────┘
         │
         ▼
┌─────────────────────┐
│     Resolver        │
│ ResolveAsync(prov)  │
└─────────────────────┘
         │
         ▼
┌─────────────────────┐
│     Deliverer       │
│  (uses manifest)    │
└─────────────────────┘
```

## Implementation Example: Community Outpost

### Provider Class

The provider class injects `IProviderDefinitionLoader` and caches the definition:

```csharp
public class CommunityOutpostProvider : BaseContentProvider
{
    private readonly IProviderDefinitionLoader _definitionLoader;
    private ProviderDefinition? _cachedProviderDefinition;

    public CommunityOutpostProvider(
        IProviderDefinitionLoader definitionLoader,
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

    protected override ProviderDefinition? GetProviderDefinition()
    {
        // Cache the provider definition for performance
        _cachedProviderDefinition ??= _definitionLoader.GetProvider(PublisherId);
        return _cachedProviderDefinition;
    }
}
```

### Discoverer Usage

Discoverers receive the provider definition and use it for endpoint configuration:

```csharp
public class CommunityOutpostDiscoverer : IContentDiscoverer
{
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ProviderDefinition? provider,
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // Get configuration from provider definition with fallback to constants
        var catalogUrl = provider?.Endpoints.CatalogUrl 
            ?? CommunityOutpostConstants.CatalogUrl;
        
        var patchPageUrl = provider?.Endpoints.GetEndpoint("patchPageUrl") 
            ?? CommunityOutpostConstants.PatchPageUrl;
        
        var timeout = TimeSpan.FromSeconds(
            provider?.Timeouts.CatalogTimeoutSeconds ?? 30);

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

Resolvers use provider configuration for manifest creation:

```csharp
public class CommunityOutpostResolver : IContentResolver
{
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ProviderDefinition? provider,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        // Get endpoints from provider definition
        var websiteUrl = provider?.Endpoints.WebsiteUrl 
            ?? CommunityOutpostConstants.PublisherWebsite;
        
        var patchPageUrl = provider?.Endpoints.GetEndpoint("patchPageUrl") 
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

## IProviderDefinitionLoader Interface

```csharp
public interface IProviderDefinitionLoader
{
    /// <summary>
    /// Gets a specific provider definition by ID. Auto-loads on first access.
    /// </summary>
    ProviderDefinition? GetProvider(string providerId);

    /// <summary>
    /// Gets all enabled provider definitions.
    /// </summary>
    IEnumerable<ProviderDefinition> GetAllProviders();

    /// <summary>
    /// Gets providers filtered by type (Static or Dynamic).
    /// </summary>
    IEnumerable<ProviderDefinition> GetProvidersByType(ProviderType providerType);

    /// <summary>
    /// Loads all provider definitions asynchronously.
    /// </summary>
    Task<OperationResult<IEnumerable<ProviderDefinition>>> LoadProvidersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads all providers (for hot-reload scenarios).
    /// </summary>
    Task<OperationResult<bool>> ReloadProvidersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a runtime-defined provider (not from file).
    /// </summary>
    OperationResult<bool> AddCustomProvider(ProviderDefinition provider);

    /// <summary>
    /// Removes a runtime-added provider.
    /// </summary>
    OperationResult<bool> RemoveCustomProvider(string providerId);
}
```

## Provider Types

### Static Providers

Static providers have a fixed publisher identity. All content discovered from the source is attributed to a single known publisher.

**Examples**: Community Outpost, Generals Online, TheSuperHackers

```json
{
  "providerType": "Static",
  "publisherType": "communityoutpost"
}
```

### Dynamic Providers

Dynamic providers support multiple publishers where content authors become individual publishers. Each discovered author gets their own publisher identity.

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

## Benefits

| Feature | Description |
|---------|-------------|
| **Runtime Changes** | Modify endpoints without recompilation |
| **User Customization** | Users can override bundled providers in AppData |
| **Mirror Support** | Built-in failover across multiple download mirrors |
| **Hot Reload** | `ReloadProvidersAsync()` for runtime updates |
| **Extensibility** | Add new providers by dropping in JSON files |
| **Environment Config** | Different URLs for dev/staging/production |

## Testing

### Unit Testing with Mock Providers

```csharp
[Fact]
public async Task Discoverer_UsesProviderEndpoints()
{
    // Arrange
    var provider = new ProviderDefinition
    {
        ProviderId = "test-provider",
        DisplayName = "Test Provider",
        Endpoints = new ProviderEndpoints
        {
            CatalogUrl = "https://test.example.com/catalog"
        },
        Timeouts = new ProviderTimeouts
        {
            CatalogTimeoutSeconds = 10
        }
    };

    var mockHttp = new Mock<IHttpClientFactory>();
    var discoverer = new CommunityOutpostDiscoverer(mockHttp.Object, _logger);

    // Act
    await discoverer.DiscoverAsync(provider, query, CancellationToken.None);

    // Assert
    mockHttp.Verify(x => x.CreateClient(), Times.Once);
    // Verify the configured URL was used...
}
```

### Integration Testing with Test Provider Files

```csharp
[Fact]
public async Task Loader_LoadsFromBothDirectories()
{
    // Arrange
    var bundledDir = Path.Combine(_tempDir, "bundled");
    var userDir = Path.Combine(_tempDir, "user");
    
    Directory.CreateDirectory(bundledDir);
    Directory.CreateDirectory(userDir);
    
    // Create bundled provider
    File.WriteAllText(
        Path.Combine(bundledDir, "test.provider.json"),
        """{"providerId": "test", "displayName": "Bundled"}""");
    
    // Create user override
    File.WriteAllText(
        Path.Combine(userDir, "test.provider.json"),
        """{"providerId": "test", "displayName": "User Override"}""");

    var loader = new ProviderDefinitionLoader(_logger, bundledDir, userDir);

    // Act
    var provider = loader.GetProvider("test");

    // Assert - User override wins
    Assert.Equal("User Override", provider?.DisplayName);
}
```

## File Reference

| Component | Path |
|-----------|------|
| **Core Interfaces** | |
| IProviderDefinitionLoader | `GenHub.Core/Interfaces/Providers/IProviderDefinitionLoader.cs` |
| ICatalogParser | `GenHub.Core/Interfaces/Providers/ICatalogParser.cs` |
| ICatalogParserFactory | `GenHub.Core/Interfaces/Providers/ICatalogParserFactory.cs` |
| **Core Services** | |
| ProviderDefinitionLoader | `GenHub.Core/Services/Providers/ProviderDefinitionLoader.cs` |
| CatalogParserFactory | `GenHub.Core/Services/Providers/CatalogParserFactory.cs` |
| **Models** | |
| ProviderDefinition | `GenHub.Core/Models/Providers/ProviderDefinition.cs` |
| GenPatcherContentRegistry | `GenHub/Features/Content/Models/GenPatcherContentRegistry.cs` |
| **Provider Configurations** | |
| Community Outpost Provider | `GenHub/Providers/communityoutpost.provider.json` |
| **Community Outpost Implementation** | |
| CommunityOutpostDiscoverer | `GenHub/Features/Content/Services/CommunityOutpost/CommunityOutpostDiscoverer.cs` |
| CommunityOutpostResolver | `GenHub/Features/Content/Services/CommunityOutpost/CommunityOutpostResolver.cs` |
| CommunityOutpostProvider | `GenHub/Features/Content/Services/CommunityOutpost/CommunityOutpostProvider.cs` |
| GenPatcherDatCatalogParser | `GenHub/Features/Content/Services/CommunityOutpost/GenPatcherDatCatalogParser.cs` |
