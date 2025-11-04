# Generals Online Content Provider for GenHub

Complete implementation of a content provider for **Generals Online** - demonstrating GenHub's content provider architecture with a real-world game client.

## Overview

This content provider enables GenHub to:
- Discover Generals Online releases
- Download and extract portable ZIP packages
- Manage dependencies (C&C Generals Zero Hour base game)
- Store files in Content-Addressable Storage (CAS)
- Create workspaces with proper file linking
- Handle version updates automatically

## Architecture

```
User Search "Generals Online"
    ↓
GeneralsOnlineDiscoverer
    ↓ (scrapes/queries API)
ContentSearchResult{version: "101525_QFE5"}
    ↓
GeneralsOnlineResolver
    ↓ (creates manifest)
ContentManifest{dependencies, files, metadata}
    ↓
User clicks "Install"
    ↓
GeneralsOnlineProvider.PrepareContentAsync()
    ↓ (downloads ZIP)
HTTP Deliverer
    ↓ (extracts files)
ZIP Extraction
    ↓ (hashes files)
SHA-256 Hashing
    ↓ (stores in CAS)
Content-Addressable Storage
    ↓ (creates workspace)
Workspace with Links
    ↓
Ready to Play!
```

## Key Features

### 1. Manifest Generation

Automatically generates complete ContentManifest from Generals Online releases:

- **Version tracking**: `YYMMDD_QFE#` format
- **File hashing**: SHA-256 for all files
- **Dependency declaration**: Requires C&C Generals Zero Hour
- **Metadata**: Description, changelog, URLs

### 2. Dependency Management

Detects and validates base game installation:

- **Steam detection**: Checks Steam library
- **EA Origin detection**: Checks Origin library
- **Registry fallback**: Custom installations
- **Version validation**: Ensures v1.04+

### 3. Update Handling

Automatic update detection and management:

- **Periodic checks**: Every 24 hours
- **Version comparison**: Semantic versioning
- **User notification**: Update available UI
- **Delta storage**: Only changed files stored in CAS

### 4. CAS Integration

Efficient storage through deduplication:

- **Hash-based**: Files stored by SHA-256
- **Deduplication**: Shared files stored once
- **Hard linking**: Multiple profiles share files
- **Rollback support**: Old versions retained

## File Structure

```
GenHub/Features/Content/Services/GeneralsOnline/
├── GeneralsOnlineProvider.cs              # Main provider implementation
├── GeneralsOnlineDiscoverer.cs            # Discovery and API client
├── GeneralsOnlineResolver.cs              # Manifest resolution
├── GeneralsOnlineManifestFactory.cs       # Manifest generation
├── GeneralsOnlineDeliverer.cs             # Content delivery
├── GeneralsOnlineUpdateService.cs         # Update checking
└── README.md                              # This file
```

## Implementation Details

### GeneralsOnlineProvider.cs

Main content provider implementing `IContentProvider`:

- Orchestrates discovery → resolution → delivery pipeline
- Handles ZIP download and extraction
- Integrates with CAS for file storage
- Manages workspace preparation

### GeneralsOnlineDiscoverer.cs

Discovers available releases:

- Scrapes playgenerals.online website (current)
- Will query manifest API (future)
- Parses version information from URLs
- Returns `ContentSearchResult` objects

### GeneralsOnlineResolver.cs

Resolves search results to manifests:

- Converts `ContentSearchResult` → `ContentManifest`
- Populates file lists
- Declares dependencies
- Adds metadata

### GeneralsOnlineManifestFactory.cs

Factory for manifest creation:

- Creates base manifest structure
- Updates with extracted files
- Computes SHA-256 hashes
- Determines install paths

## Dependencies

### Required

- **C&C Generals Zero Hour**: Base game (Steam or EA Origin)
- **.NET 8.0**: Runtime for GenHub
- **GenHub.Core**: GenHub core library

### Included in Portable ZIP

- **.NET 9.0 Desktop Runtime**: Bundled with installer
- **Visual C++ Redistributable**: Bundled with installer
- **Game networking DLLs**: Bundled in portable

## API Integration

GenHub has **full API infrastructure in place** and ready for the Generals Online CDN:

### API Endpoints Supported

1. **manifest.json** (preferred): Full release details with file sizes and hashes
   - Endpoint: `https://cdn.playgenerals.online/manifest.json`
   - Model: `GeneralsOnlineApiResponse` with version, download_url, size, sha256, release_notes
   - Provides: Complete release metadata for optimal downloads

2. **latest.txt** (fallback): Simple version polling
   - Endpoint: `https://cdn.playgenerals.online/latest.txt`
   - Returns: Plain text version string (e.g., "101525_QFE5")
   - GenHub constructs URLs by pattern

3. **Mock data** (development fallback): Hardcoded "101525_QFE5" release
   - Used only when both API endpoints are unavailable
   - Allows development and testing without live CDN

### Current Behavior

`GeneralsOnlineDiscoverer` tries endpoints in priority order:
1. Try manifest.json → Full API response
2. Try latest.txt → Version-only response  
3. Fall back to mock → Development data

All manifest generation, CAS storage, and workspace preparation is **fully implemented and tested**. Just waiting on Generals Online team to deploy the CDN infrastructure.

## Workspace Structure

Example workspace for "Generals Online Multiplayer" profile:

```
GenHub/workspaces/Generals Online Multiplayer/
├── generals.exe                           # Symlink → Steam/EA installation
├── Data/                                  # Symlink → base game Data/
├── GeneralsOnlineZH.exe                   # Hard link → CAS
├── GeneralsOnlineZH_30.exe                # Hard link → CAS
├── GeneralsOnlineZH_60.exe                # Hard link → CAS
├── abseil_dll.dll                         # Hard link → CAS
├── GameNetworkingSockets.dll              # Hard link → CAS
├── libcrypto-3.dll                        # Hard link → CAS
├── (... 10+ more DLLs ...)                # Hard links → CAS
├── GeneralsOnlineGameData/                # Hard links → CAS
│   ├── GOSplash.bmp
│   └── MapCacheGO.ini
└── Maps/                                  # Hard links → CAS
    ├── [GO][RANK] Arctic Lagoon ZH v2/
    ├── [GO][RANK] Barren Badlands Balanced ZH v2/
    └── (... 20+ maps ...)
```

### Linking Strategy

| Source | Link Type | Reason |
|--------|-----------|--------|
| Base game files | Symlink | Read-only, don't modify original |
| GO executables | Hard link | From CAS, deduplicated |
| GO DLLs | Hard link | From CAS, deduplicated |
| GO maps | Hard link | From CAS, shared across profiles |

## Storage Efficiency

### Example: 3 Profiles with Generals Online

**Without CAS**:
```
Profile 1: 38 MB
Profile 2: 38 MB
Profile 3: 38 MB
Total:     114 MB
```

**With CAS**:
```
CAS Storage: 38 MB (stored once)
Profile 1:    0 MB (hard links)
Profile 2:    0 MB (hard links)
Profile 3:    0 MB (hard links)
Total:       38 MB
Savings:     76 MB (67%)
```

## Version Management

### Update Scenario

User has `101525_QFE5`, new version `101625_QFE6` released:

1. **Discovery**: Plugin detects new version via API/scrape
2. **Notification**: User informed of update
3. **Download**: New ZIP downloaded to staging
4. **Extraction**: Files extracted and hashed
5. **CAS Deduplication**:
   - `GeneralsOnlineZH.exe`: NEW (binary changed)
   - `GOSplash.bmp`: REUSE (hash unchanged)
   - `Maps/Arctic Lagoon/`: REUSE (unchanged)
   - `GameNetworkingSockets.dll`: NEW (updated)
6. **Workspace Update**: Links updated to new files
7. **Cleanup**: Staging directory removed
8. **Old Version**: Remains in CAS (rollback possible)

### Rollback Support

To rollback to previous version:

```csharp
// Switch profile to use older manifest
profile.RequiredManifests = new List<ManifestId>
{
    new("GeneralsOnline", "101525_QFE5", ContentType.Mod, GameType.CNCGeneralsZeroHour)
};

// Workspace preparation recreates links to old files
await WorkspaceManager.PrepareWorkspaceAsync(profile);

// Old files still in CAS, instant rollback
```

## Multi-Profile Support

### Scenario: Different Versions

```
Profile: "Competitive - Latest"
  └─ GeneralsOnline 101625_QFE6

Profile: "Testing - Previous"
  └─ GeneralsOnline 101525_QFE5

Profile: "Modded - Custom Maps"
  └─ GeneralsOnline 101625_QFE6
  └─ Community Map Pack 1.0
```

**CAS Storage**:
- GO QFE6 files: 1 copy
- GO QFE5 files: 1 copy
- Common maps: 1 copy (shared)
- Custom maps: 1 copy (shared)

**Total Storage**: ~80 MB (vs ~180 MB without CAS)

## Error Handling

### Common Issues

1. **Base Game Not Found**:
   - Error: "C&C Generals Zero Hour not detected"
   - Solution: Prompt user to install or specify path

2. **Download Failed**:
   - Error: "Failed to download Generals Online package"
   - Solution: Retry with exponential backoff

3. **Extraction Failed**:
   - Error: "Failed to extract ZIP archive"
   - Solution: Check disk space, permissions

4. **Hash Mismatch**:
   - Error: "File integrity check failed"
   - Solution: Re-download corrupted file

### Logging

All errors logged with context:

```csharp
_logger.LogError(ex, 
    "Failed to prepare Generals Online content for version {Version}", 
    release.Version);
```

## Testing

### Unit Tests

```csharp
[Fact]
public async Task Discoverer_FindsLatestRelease()
{
    var discoverer = new GeneralsOnlineDiscoverer(_logger);
    var result = await discoverer.DiscoverAsync(new ContentSearchQuery());
    
    Assert.True(result.Success);
    Assert.Single(result.Data);
    Assert.Equal("Generals Online", result.Data.First().Name);
}

[Fact]
public async Task Resolver_CreatesValidManifest()
{
    var resolver = new GeneralsOnlineResolver(_logger);
    var searchResult = CreateMockSearchResult();
    
    var result = await resolver.ResolveAsync(searchResult);
    
    Assert.True(result.Success);
    Assert.NotNull(result.Data);
    Assert.Equal(ContentType.Mod, result.Data.ContentType);
    Assert.NotEmpty(result.Data.Dependencies);
}

[Fact]
public async Task ManifestFactory_GeneratesCorrectStructure()
{
    var release = CreateMockRelease();
    var manifest = GeneralsOnlineManifestFactory.CreateManifest(release);
    
    Assert.Equal("GeneralsOnline", manifest.Name);
    Assert.Equal(GameType.CNCGeneralsZeroHour, manifest.TargetGame);
    Assert.Contains(manifest.Dependencies, 
        d => d.DependencyType == DependencyType.BaseGame);
}
```

### Integration Tests

1. **Full Installation Flow**:
   - Search → Install → Workspace Creation → Launch

2. **Update Flow**:
   - Initial install → New version → Update → Verify

3. **Multi-Profile**:
   - Create 3 profiles → Verify CAS deduplication

4. **Rollback**:
   - Install new → Rollback to old → Verify

## Production Deployment

### For Generals Online Team

To enable GenHub integration:

1. **Create manifest API** at `cdn.playgenerals.online/manifest.json`
2. **Enable CORS** for GenHub requests
3. **Include file hashes** for CAS optimization
4. **Provide changelog** for each release

### For GenHub Users

1. **Download GenHub** from the official repository
2. **Install C&C Generals Zero Hour** (Steam or EA Origin)
3. **Launch GenHub**
4. **Search "Generals Online"**
5. **Click Install**
6. **Play!**

## Future Enhancements

### Short Term

- [ ] Implement manifest API integration
- [ ] Add progress reporting during download
- [ ] Improve error messages
- [ ] Add retry logic for failed downloads

### Medium Term

- [ ] Delta updates (download only changed files)
- [ ] Parallel file hashing
- [ ] Bandwidth throttling
- [ ] Resume interrupted downloads

### Long Term

- [ ] Multiple release channels (stable, beta, experimental)
- [ ] Automatic update on launch
- [ ] Plugin marketplace integration
- [ ] Community map repository integration

## Contributing

This plugin serves as a reference implementation for GenHub content providers. Contributions welcome:

1. Fork the repository
2. Create feature branch
3. Implement changes
4. Add tests
5. Submit pull request

## License

This plugin follows GenHub's license. Generals Online is a separate project with its own license.

## Credits

- **Generals Online Team**: For creating the multiplayer service
- **GenHub Team**: For the extensible plugin architecture
- **Community**: For testing and feedback

## Support

- **GenHub Issues**: https://github.com/undead2146/GenHub/issues
- **Generals Online Discord**: https://discord.playgenerals.online/
- **Documentation**: https://docs.genhub.dev/content-providers/generals-online

---

**Version**: 1.0.0  
**Last Updated**: October 30, 2025  
**Status**: Built-in Content Provider  

This content provider demonstrates the complete GenHub content provider architecture with a real-world use case.
