# Creator Publishing & Discovery Infrastructure

**Version**: 1.0.0
**Status**: Phase 2-3 Complete
**Last Updated**: 2026-01-04

## Overview

The Creator Publishing & Discovery Infrastructure enables **any creator** to publish mods, maps, and addons by hosting a simple JSON catalog file. GenHub users can subscribe to these catalogs via `genhub://subscribe?url=...` URIs, eliminating the need for code changes to add new publishers.

This system addresses key user feedback:

- ✅ **Version clutter**: Shows only latest stable version by default
- ✅ **Seamless updates**: Creators push updates, users get them automatically
- ✅ **Decentralized**: No central authority required
- ✅ **Non-technical**: Simple JSON schema, optional CLI tooling

---

## Architecture

### Generic Publisher Pattern

Instead of creating unique `Discoverer`/`Resolver`/`ManifestFactory` for each publisher, we use:

```
┌─────────────────────────────────────────────────────────────┐
│                    SUBSCRIPTION LAYER                        │
│  User subscribes → subscriptions.json → Sidebar display     │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                  GENERIC CATALOG PIPELINE                    │
│                                                              │
│  GenericCatalogDiscoverer → GenericCatalogResolver          │
│  (ONE instance per subscription, configured dynamically)     │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                 EXISTING INFRASTRUCTURE                      │
│  ContentManifestBuilder → CAS Storage → Profile Integration │
└─────────────────────────────────────────────────────────────┘
```

**Key Insight**: Zero code changes needed to add new publishers.

---

## For Mod Makers: Publishing Your Content

### Step 1: Create Your Catalog

Create a `catalog.json` file following this schema:

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
      "description": "Rebalances all factions for competitive play",
      "contentType": "Mod",
      "targetGame": "ZeroHour",
      "tags": ["balance", "multiplayer", "competitive"],
      "releases": [
        {
          "version": "1.0.0",
          "releaseDate": "2026-01-04T00:00:00Z",
          "isLatest": true,
          "isPrerelease": false,
          "changelog": "Initial release with faction rebalancing",
          "artifacts": [
            {
              "filename": "SuperBalanceMod-1.0.0.zip",
              "downloadUrl": "https://github.com/myname/my-mod/releases/download/v1.0.0/SuperBalanceMod-1.0.0.zip",
              "size": 15728640,
              "sha256": "abc123def456...",
              "isPrimary": true
            }
          ]
        }
      ]
    }
  ]
}
```

### Step 2: Host Your Catalog

Host `catalog.json` on any HTTP endpoint:

- **GitHub Releases**: `https://github.com/user/repo/releases/latest/download/catalog.json`
- **GitHub Pages**: `https://user.github.io/repo/catalog.json`
- **Personal CDN**: `https://mycdn.com/genhub/catalog.json`
- **Google Drive**: Public share link (must be direct download)

### Step 3: Share Subscription Link

Create a subscription URI:

```
genhub://subscribe?url=https://github.com/myname/my-mod/releases/latest/download/catalog.json
```

Users click this link → GenHub adds your catalog → Your content appears in Downloads tab.

---

## For GenHub Users: Subscribing to Publishers

### Method 1: Click Subscription Link

Mod maker shares: `genhub://subscribe?url=...`
You click → GenHub shows confirmation dialog → Subscribe → Publisher appears in sidebar

### Method 2: Manual Subscription (Future)

Settings → Subscriptions → Add Publisher → Paste catalog URL

---

## Technical Details

### Components

| Component | Purpose |
|-----------|---------|
| [PublisherSubscriptionStore](file:///z:/GenHub/GenHub/GenHub/Features/Content/Services/Catalog/PublisherSubscriptionStore.cs) | File-based subscription persistence (`subscriptions.json`) |
| [JsonPublisherCatalogParser](file:///z:/GenHub/GenHub/GenHub/Features/Content/Services/Catalog/JsonPublisherCatalogParser.cs) | Parses & validates catalog JSON (15+ validation rules) |
| [VersionSelector](file:///z:/GenHub/GenHub/GenHub/Features/Content/Services/Catalog/VersionSelector.cs) | Filters versions (Latest Stable Only by default) |
| [GenericCatalogDiscoverer](file:///z:/GenHub/GenHub/GenHub/Features/Content/Services/Catalog/GenericCatalogDiscoverer.cs) | Fetches catalog, applies filters, returns search results |
| [GenericCatalogResolver](file:///z:/GenHub/GenHub/GenHub/Features/Content/Services/Catalog/GenericCatalogResolver.cs) | Converts catalog entry → ContentManifest |

### Integration Points

#### DownloadsBrowserViewModel

**InitializePublishersAsync**:

1. Loads hardcoded publishers (Generals Online, ModDB, etc.)
2. Calls `IPublisherSubscriptionStore.GetSubscriptionsAsync()`
3. Adds subscribed publishers to sidebar

**GetDiscovererForPublisher**:

1. Checks hardcoded publishers first
2. Falls back to subscription store
3. Creates `GenericCatalogDiscoverer` configured for subscription

#### ContentManifestBuilder Integration

`GenericCatalogResolver` calls:

```csharp
await builder.AddDownloadedFileAsync(
    relativePath: artifact.Filename,
    downloadUrl: artifact.DownloadUrl,
    ...
);
```

This automatically:

- Downloads file to temp directory
- Detects if it's an archive (ZIP/RAR/7z)
- Extracts all files
- Stores each file in CAS
- Adds `ManifestFile` entries with hashes

**No custom extraction logic needed per publisher.**

---

## Catalog Schema Reference

### Root: PublisherCatalog

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `$schemaVersion` | int | Yes | Schema version (currently `1`) |
| `publisher` | PublisherProfile | Yes | Publisher identity |
| `content` | CatalogContentItem[] | Yes | Content items |
| `lastUpdated` | DateTime | Yes | Catalog last modified date |
| `signature` | string | No | SHA256 signature (future) |
| `referrals` | PublisherReferral[] | No | Links to other publishers |

### PublisherProfile

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique publisher ID (lowercase, alphanumeric) |
| `name` | string | Yes | Display name |
| `website` | string | No | Publisher website |
| `avatarUrl` | string | No | Logo/avatar URL |
| `supportUrl` | string | No | Support/Discord URL |
| `contactEmail` | string | No | Contact email |

### CatalogContentItem

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Content ID (unique within publisher) |
| `name` | string | Yes | Display name |
| `description` | string | Yes | Description |
| `contentType` | ContentType | Yes | `Mod`, `Map`, `Addon`, etc. |
| `targetGame` | GameType | Yes | `Generals` or `ZeroHour` |
| `releases` | ContentRelease[] | Yes | Version releases |
| `metadata` | ContentRichMetadata | No | Banners, screenshots, videos |
| `tags` | string[] | No | Search tags |

### ContentRelease

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `version` | string | Yes | Semantic version (e.g., `1.0.0`) |
| `releaseDate` | DateTime | Yes | Release timestamp |
| `isLatest` | bool | Yes | Mark as latest stable |
| `isPrerelease` | bool | No | Beta/alpha flag |
| `changelog` | string | No | Release notes (markdown) |
| `artifacts` | ReleaseArtifact[] | Yes | Downloadable files |
| `dependencies` | CatalogDependency[] | No | Required content |

### ReleaseArtifact

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `filename` | string | Yes | File name |
| `downloadUrl` | string | Yes | Direct download URL |
| `size` | long | Yes | File size in bytes |
| `sha256` | string | Yes | SHA256 hash for verification |
| `isPrimary` | bool | No | Primary artifact (default: true) |

---

## Version Filtering

**Default Policy**: `LatestStableOnly`

- Shows only the latest release where `isLatest: true` and `isPrerelease: false`
- Reduces version clutter (addresses user feedback)

**User Override**: "Show Older Versions" checkbox (Phase 4)

- Switches to `AllVersions` policy
- Shows all releases for power users

**Implementation**:

```csharp
var policy = query.IncludeOlderVersions
    ? VersionPolicy.AllVersions
    : VersionPolicy.LatestStableOnly;

var selectedReleases = versionSelector.SelectReleases(
    contentItem.Releases,
    policy
);
```

---

## Security & Trust

### Catalog Validation

15+ validation rules enforced by `JsonPublisherCatalogParser`:

- Schema version compatibility
- Required fields present
- Publisher ID format
- At least one content item
- Each content has releases
- Each release has artifacts
- Each artifact has download URL + SHA256

### File Integrity

**SHA256 Verification**:

- Creators compute SHA256 of artifacts
- GenHub verifies hash after download
- Mismatches rejected

**Trust Levels** (Phase 5):

- `Untrusted`: Default for new subscriptions
- `Trusted`: User explicitly trusts publisher
- `Verified`: GenHub maintainers verify (future)

---

## Future Enhancements

### Phase 4: Version Filtering UI

- "Show Older Versions" toggle in Downloads tab
- Per-publisher version policy preferences

### Phase 5: UI Polish

- Rich content detail view (banners, screenshots, video embeds)
- Subscription confirmation dialog with catalog preview
- Settings → Subscriptions management page

### Phase 6: Creator Tooling

**CLI Tool**:

```bash
genhub-cli init --publisher "My Name"
genhub-cli add-release --content-id "my-mod" --version "1.0.0" --file "./MyMod.zip"
genhub-cli publish --to github-release
```

**Web Wizard**:

- Upload files → Auto-compute SHA256
- Fill form → Generate catalog.json
- Download or publish directly

---

## Troubleshooting

### Catalog Not Loading

**Check**:

1. Catalog URL is publicly accessible (test in browser)
2. JSON is valid (use JSONLint)
3. Schema version is `1`
4. All required fields present

**Logs**:

```
GenHub → Settings → Logs → Filter: "PublisherCatalog"
```

### Content Not Appearing

**Check**:

1. `targetGame` matches user's filter (Generals vs Zero Hour)
2. `isLatest: true` is set on latest release
3. Artifact `downloadUrl` is direct download (not HTML page)
4. SHA256 hash is correct (64 hex characters)

---

## Examples

### Minimal Catalog

```json
{
  "$schemaVersion": 1,
  "publisher": {
    "id": "simple-mods",
    "name": "Simple Mods"
  },
  "content": [
    {
      "id": "test-mod",
      "name": "Test Mod",
      "description": "A test mod",
      "contentType": "Mod",
      "targetGame": "ZeroHour",
      "releases": [
        {
          "version": "1.0.0",
          "releaseDate": "2026-01-04T00:00:00Z",
          "isLatest": true,
          "artifacts": [
            {
              "filename": "test.zip",
              "downloadUrl": "https://example.com/test.zip",
              "size": 1024,
              "sha256": "abc123..."
            }
          ]
        }
      ]
    }
  ],
  "lastUpdated": "2026-01-04T00:00:00Z"
}
```

### Full-Featured Catalog

See [creator-publishing-roadmap.md](file:///z:/GenHub/docs/dev/creator-publishing-roadmap.md) for complete examples with metadata, dependencies, and multiple releases.

---

## Related Documentation

- [Roadmap](file:///z:/GenHub/docs/dev/creator-publishing-roadmap.md) - Comprehensive architectural design
- [Manifest ID System](file:///z:/GenHub/docs/dev/manifest-id-system.md) - How content IDs are generated
- [Provider Infrastructure](file:///z:/GenHub/docs/features/content/provider-infrastructure.md) - Existing provider system
- [Downloads Flow](file:///z:/GenHub/docs/FlowCharts/Downloads-Flow.md) - Content discovery pipeline
