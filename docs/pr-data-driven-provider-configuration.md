# Pull Request: Data-Driven Provider Configuration System

## PR Title

`feat: Implement data-driven provider configuration with externalized JSON settings`

---

## Summary

This PR introduces a comprehensive **data-driven provider configuration system** that externalizes content source settings into JSON files. This enables runtime configuration of endpoints, timeouts, catalog parsing, and provider behavior **without code changes**.

## Key Changes

### 🏗️ Architecture

#### New Core Infrastructure

- **`IProviderDefinitionLoader`** - Service interface for loading/managing provider definitions
- **`ProviderDefinitionLoader`** - Implementation with auto-loading, caching, and hot-reload support
- **`ICatalogParser`** - Interface for pluggable catalog format parsers
- **`ICatalogParserFactory`** - Factory for obtaining parsers by format identifier
- **`IContentPipelineFactory`** - Factory for obtaining pipeline components by provider ID

#### Provider Definition Files (JSON)

- `communityoutpost.provider.json` - Community Outpost (GenPatcher) configuration
- `generalsonline.provider.json` - Generals Online CDN configuration
- `thesuperhackers.provider.json` - TheSuperHackers GitHub configuration

### 📁 New Files

| File | Purpose |
|------|---------|
| `GenHub.Core/Interfaces/Providers/ICatalogParser.cs` | Catalog parser interface |
| `GenHub.Core/Interfaces/Providers/ICatalogParserFactory.cs` | Parser factory interface |
| `GenHub.Core/Services/Providers/CatalogParserFactory.cs` | Parser factory implementation |
| `GenHub/Features/Content/Services/CommunityOutpost/GenPatcherDatCatalogParser.cs` | GenPatcher dl.dat catalog parser |
| `GenHub/Features/Content/Services/GeneralsOnline/GeneralsOnlineJsonCatalogParser.cs` | Generals Online JSON API parser |
| `GenHub/Features/Content/Services/ContentPipelineFactory.cs` | Pipeline component factory |
| `GenHub/Providers/*.provider.json` | Provider configuration files |
| `docs/features/content/provider-configuration.md` | Comprehensive documentation |
| `docs/features/content/provider-infrastructure.md` | Architecture documentation |

### 🔧 Modified Files

#### Content Providers

All content providers now:

1. Inject `IProviderDefinitionLoader`
2. Override `GetProviderDefinition()` to provide cached configuration
3. Pass provider definition to discoverers and resolvers

| Provider | Changes |
|----------|---------|
| `CommunityOutpostProvider` | Added provider definition loading and caching |
| `CommunityOutpostDiscoverer` | Uses provider endpoints instead of constants |
| `CommunityOutpostResolver` | Uses provider endpoints for manifest URLs |
| `GeneralsOnlineProvider` | Added provider definition support |
| `GeneralsOnlineDiscoverer` | Uses catalog parser factory |
| `GeneralsOnlineManifestFactory` | Uses provider endpoints for manifest URLs |
| `SuperHackersProvider` | Added provider definition support |
| `SuperHackersUpdateService` | Uses provider config for GitHub repo info |

#### Base Infrastructure

- **`BaseContentProvider`** - Added `GetProviderDefinition()` virtual method
- **`IContentDiscoverer`** - Added provider-aware `DiscoverAsync()` overload
- **`IContentResolver`** - Added provider-aware `ResolveAsync()` overload

### 📝 Documentation Updates

- `docs/architecture.md` - Added section on data-driven provider configuration
- `docs/features/content/index.md` - Added link to provider configuration docs

---

## Provider Definition Schema

```json
{
  "providerId": "community-outpost",
  "publisherType": "communityoutpost",
  "displayName": "Community Outpost",
  "description": "Official patches, tools, and addons from GenPatcher",
  "iconColor": "#2196F3",
  "providerType": "Static",
  "catalogFormat": "genpatcher-dat",
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
  },
  "enabled": true
}
```

---

## Usage Example

### Provider Implementation

```csharp
public class MyContentProvider : BaseContentProvider
{
    private readonly IProviderDefinitionLoader _loader;
    private ProviderDefinition? _cachedDefinition;

    protected override ProviderDefinition? GetProviderDefinition()
    {
        _cachedDefinition ??= _loader.GetProvider("my-provider-id");
        return _cachedDefinition;
    }
}
```

### Discoverer Implementation

```csharp
public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
    ProviderDefinition? provider,
    ContentSearchQuery query,
    CancellationToken cancellationToken)
{
    // Use provider-defined endpoints with fallback to constants
    var catalogUrl = provider?.Endpoints.CatalogUrl ?? MyConstants.CatalogUrl;
    var timeout = provider?.Timeouts.CatalogTimeoutSeconds ?? 30;
    
    // Use custom endpoints
    var customEndpoint = provider?.Endpoints.GetEndpoint("customKey") ?? "default";
    
    // ... discovery logic
}
```

---

## Breaking Changes

None. All changes are backward-compatible:

- Providers without definitions fall back to hardcoded constants
- Existing manifest IDs remain unchanged
- All public interfaces maintain backward compatibility

---

## Testing

- Unit tests for `ProviderDefinitionLoader` - file loading, caching, hot-reload
- Unit tests for `CatalogParserFactory` - parser registration and lookup
- Unit tests for `ContentPipelineFactory` - component matching
- All existing tests continue to pass

---

## Deployment Notes

Provider JSON files are automatically copied to the output directory via MSBuild:

```xml
<ItemGroup>
  <Content Include="Providers\*.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

---

## File Locations

**Bundled Providers**: `{AppDir}/Providers/*.provider.json`

**User Providers** (override bundled):

- Windows: `%APPDATA%\GenHub\Providers\`
- Linux: `~/.config/GenHub/Providers/`
- macOS: `~/Library/Application Support/GenHub/Providers/`

---

## Related Issues

- Enables future support for user-defined content sources
- Foundation for ModDB and CNCLabs provider implementations
- Supports mirror failover for improved download reliability
