# GeneralsOnline API Schema Proposal for GenHub Integration

**Document Version:** 1.0
**Date:** March 20, 2026
**Target Audience:** GeneralsOnline Development Team
**Purpose:** Propose a comprehensive, extensible API schema for GeneralsOnline content catalog that aligns with GenHub's Publisher Studio architecture

---

## Executive Summary

This document proposes a unified API schema for the GeneralsOnline CDN catalog endpoint that will:

1. **Replace current dual-endpoint system** (manifest.json + latest.txt) with a single, comprehensive catalog API
2. **Enable future Publisher Studio integration** for community content distribution
3. **Support multiple content types** (game clients, map packs, mods, tools)
4. **Provide extensibility** for future features (variants, dependencies, metadata)
5. **Maintain backward compatibility** during migration

---

## Current State Analysis

### Existing Implementation

GenHub currently integrates with GeneralsOnline using:

**Provider Configuration** (`generalsonline.provider.json`):
```json
{
  "providerId": "generalsonline",
  "publisherType": "generalsonline",
  "catalogFormat": "generalsonline-json-api",
  "endpoints": {
    "catalogUrl": "https://cdn.playgenerals.online/manifest.json",
    "custom": {
      "cdnBaseUrl": "https://cdn.playgenerals.online",
      "latestVersionUrl": "https://cdn.playgenerals.online/latest.txt",
      "releasesUrl": "https://cdn.playgenerals.online/releases"
    }
  }
}
```

**Current API Endpoints:**

1. **manifest.json** (Primary) - Full release metadata:
   ```json
   {
     "version": "111825_QFE2",
     "download_url": "https://cdn.playgenerals.online/releases/GeneralsOnline_portable_111825_QFE2.zip",
     "size": 1234567890,
     "release_notes": "Bug fixes and improvements",
     "sha256": "abc123..."
   }
   ```

2. **latest.txt** (Fallback) - Simple version string:
   ```
   111825_QFE2
   ```

### Current Workflow in GenHub

1. **Discovery Phase**
   - `GeneralsOnlineDiscoverer` fetches manifest.json (or falls back to latest.txt)
   - Wraps response in source-tagged format for parser

2. **Parsing Phase**
   - `GeneralsOnlineJsonCatalogParser` parses API response
   - Creates `GeneralsOnlineRelease` model
   - Generates `ContentSearchResult` for UI display

3. **Manifest Factory Phase**
   - `GeneralsOnlineManifestFactory` creates TWO manifests from single release:
     - **60Hz Game Client** - Main executable and shared files
     - **QuickMatch MapPack** - Required multiplayer maps
   - Post-extraction: Computes SHA-256 hashes for all files
   - Integrates with Content-Addressable Storage (CAS) system

4. **Reconciliation Phase**
   - `GeneralsOnlineProfileReconciler` checks for updates
   - `GeneralsOnlineUpdateService` polls CDN every 24 hours
   - Handles update strategies (replace/side-by-side)
   - Manages version skipping and auto-update preferences

### Pain Points

1. **Dual Endpoint Complexity**: Fallback logic between manifest.json and latest.txt
2. **Limited Metadata**: No support for multiple variants, dependencies, or rich metadata
3. **Manual Variant Creation**: GenHub must manually split content into 60Hz + MapPack
4. **No Extensibility**: Cannot add new content types (mods, tools, skins) without code changes
5. **Missing Features**: No changelog URLs, cover images, or detailed release notes
6. **No Dependency Management**: Cannot express requirements (e.g., "requires Zero Hour 1.04")

---

## Proposed API Schema

### Unified Catalog Endpoint

**Endpoint:** `https://cdn.playgenerals.online/catalog.json`

**Purpose:** Single source of truth for all GeneralsOnline content releases

### Schema Structure

```json
{
  "schema_version": "1.0",
  "publisher": {
    "id": "generalsonline",
    "name": "Generals Online",
    "website": "https://www.playgenerals.online/",
    "support_url": "https://discord.playgenerals.online/",
    "logo_url": "https://www.playgenerals.online/logo.png",
    "cover_url": "https://www.playgenerals.online/cover.jpg",
    "theme_color": "#4CAF50",
    "description": "Community-driven multiplayer service for C&C Generals Zero Hour. Features 60Hz tick rate, automatic updates, and improved stability."
  },
  "releases": [
    {
      "id": "generalsonline-gameclient-60hz",
      "content_type": "gameclient",
      "variant": "60hz",
      "name": "Generals Online 60Hz",
      "version": "111825_QFE2",
      "version_date": "2025-11-18T00:00:00Z",
      "release_date": "2025-11-18T14:30:00Z",
      "target_game": "zerohour",
      "description": "High-performance 60Hz game client with improved netcode and stability",
      "changelog_url": "https://www.playgenerals.online/changelog/111825_QFE2",
      "changelog_text": "## Version 111825_QFE2\n\n- Fixed desync issues in 4v4 matches\n- Improved connection stability\n- Reduced memory usage by 15%",
      "tags": [
        "multiplayer",
        "online",
        "community",
        "enhancement",
        "60hz"
      ],
      "downloads": [
        {
          "format": "portable_zip",
          "url": "https://cdn.playgenerals.online/releases/GeneralsOnline_portable_111825_QFE2.zip",
          "size": 1234567890,
          "sha256": "abc123def456...",
          "md5": "legacy_hash_optional"
        }
      ],
      "dependencies": [
        {
          "type": "game",
          "id": "zerohour",
          "name": "Command & Conquer: Generals Zero Hour",
          "version_min": "1.04",
          "required": true
        },
        {
          "type": "content",
          "id": "generalsonline-mappack-quickmatch",
          "name": "QuickMatch MapPack",
          "version": "111825_QFE2",
          "required": true,
          "description": "Required maps for multiplayer matchmaking"
        }
      ],
      "system_requirements": {
        "os": ["windows"],
        "os_version_min": "Windows 10",
        "disk_space_mb": 2048,
        "ram_mb": 2048
      },
      "metadata": {
        "executable": "generals60hz.exe",
        "install_target": "workspace",
        "supports_quickmatch": true,
        "supports_custom_games": true,
        "max_players": 8
      }
    },
    {
      "id": "generalsonline-mappack-quickmatch",
      "content_type": "mappack",
      "variant": "quickmatch",
      "name": "QuickMatch MapPack",
      "version": "111825_QFE2",
      "version_date": "2025-11-18T00:00:00Z",
      "release_date": "2025-11-18T14:30:00Z",
      "target_game": "zerohour",
      "description": "Official map rotation for GeneralsOnline QuickMatch multiplayer",
      "changelog_url": "https://www.playgenerals.online/changelog/maps/111825_QFE2",
      "changelog_text": "## MapPack 111825_QFE2\n\n- Added 2 new tournament maps\n- Rebalanced resource spawns on Desert Fury\n- Fixed pathfinding issues on Winter Wolf",
      "tags": [
        "maps",
        "multiplayer",
        "quickmatch",
        "official"
      ],
      "downloads": [
        {
          "format": "embedded",
          "description": "Maps are included in the main game client download",
          "extraction_path": "Maps/",
          "install_target": "user_maps_directory"
        }
      ],
      "dependencies": [
        {
          "type": "game",
          "id": "zerohour",
          "name": "Command & Conquer: Generals Zero Hour",
          "version_min": "1.04",
          "required": true
        }
      ],
      "metadata": {
        "map_count": 24,
        "install_target": "user_maps_directory",
        "map_list": [
          "Tournament Desert 2v2",
          "Tournament Island 3v3",
          "Desert Fury 4v4",
          "Winter Wolf 2v2"
        ]
      }
    }
  ],
  "update_policy": {
    "check_interval_hours": 24,
    "auto_update_recommended": true,
    "breaking_changes": false
  },
  "api_metadata": {
    "generated_at": "2025-11-18T14:30:00Z",
    "cache_max_age_seconds": 3600,
    "next_update_eta": "2025-11-25T00:00:00Z"
  }
}
```

---

## Schema Field Definitions

### Root Level

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schema_version` | string | Yes | API schema version for backward compatibility (e.g., "1.0") |
| `publisher` | object | Yes | Publisher metadata (name, URLs, branding) |
| `releases` | array | Yes | Array of content releases (game clients, map packs, mods) |
| `update_policy` | object | No | Update check configuration |
| `api_metadata` | object | No | API generation metadata and caching hints |

### Publisher Object

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique publisher identifier (e.g., "generalsonline") |
| `name` | string | Yes | Display name (e.g., "Generals Online") |
| `website` | string | Yes | Main website URL |
| `support_url` | string | No | Support/Discord URL |
| `logo_url` | string | No | Publisher logo (256x256 recommended) |
| `cover_url` | string | No | Cover image for UI (1920x1080 recommended) |
| `theme_color` | string | No | Hex color for UI theming (e.g., "#4CAF50") |
| `description` | string | No | Short publisher description |

### Release Object

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique release identifier (e.g., "generalsonline-gameclient-60hz") |
| `content_type` | string | Yes | Content type: "gameclient", "mappack", "mod", "tool", "skin" |
| `variant` | string | No | Variant identifier (e.g., "60hz", "30hz", "quickmatch") |
| `name` | string | Yes | Display name (e.g., "Generals Online 60Hz") |
| `version` | string | Yes | Version string (e.g., "111825_QFE2") |
| `version_date` | string (ISO 8601) | Yes | Date encoded in version |
| `release_date` | string (ISO 8601) | Yes | Actual release timestamp |
| `target_game` | string | Yes | Target game: "zerohour", "generals", "cnc3", etc. |
| `description` | string | No | Detailed description |
| `changelog_url` | string | No | URL to full changelog page |
| `changelog_text` | string | No | Inline changelog (Markdown supported) |
| `tags` | array[string] | No | Searchable tags |
| `downloads` | array | Yes | Download options (see Download Object) |
| `dependencies` | array | No | Required dependencies (see Dependency Object) |
| `system_requirements` | object | No | System requirements |
| `metadata` | object | No | Content-specific metadata (flexible key-value) |

### Download Object

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `format` | string | Yes | Download format: "portable_zip", "installer_exe", "embedded" |
| `url` | string | Conditional | Direct download URL (required unless format="embedded") |
| `size` | integer | No | File size in bytes |
| `sha256` | string | Recommended | SHA-256 hash for verification |
| `md5` | string | No | MD5 hash (legacy support) |
| `description` | string | No | Download description |
| `extraction_path` | string | No | Subdirectory path for embedded content |
| `install_target` | string | No | Install location: "workspace", "user_maps_directory", "user_data" |

### Dependency Object

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | Yes | Dependency type: "game", "content", "runtime" |
| `id` | string | Yes | Dependency identifier |
| `name` | string | Yes | Display name |
| `version` | string | No | Exact version required |
| `version_min` | string | No | Minimum version required |
| `version_max` | string | No | Maximum version supported |
| `required` | boolean | Yes | Whether dependency is mandatory |
| `description` | string | No | Dependency description |

---

## Migration Path

### Phase 1: Parallel Deployment (Weeks 1-2)

1. **Deploy new catalog.json endpoint** alongside existing manifest.json
2. **Keep manifest.json active** for backward compatibility
3. **GenHub updates** to prefer catalog.json, fallback to manifest.json
4. **Monitor adoption** via CDN analytics

**GenHub Changes:**
```csharp
// Update provider.json
{
  "endpoints": {
    "catalogUrl": "https://cdn.playgenerals.online/catalog.json",
    "custom": {
      "legacyCatalogUrl": "https://cdn.playgenerals.online/manifest.json",
      "latestVersionUrl": "https://cdn.playgenerals.online/latest.txt"
    }
  }
}
```

### Phase 2: Deprecation Notice (Weeks 3-4)

1. **Add deprecation headers** to manifest.json:
   ```
   X-Deprecated: true
   X-Deprecation-Date: 2026-05-01
   X-Replacement-Endpoint: /catalog.json
   ```
2. **GenHub logs warnings** when using legacy endpoints
3. **Community announcement** about upcoming changes

### Phase 3: Sunset (Week 5+)

1. **Redirect manifest.json → catalog.json** (HTTP 301)
2. **Remove latest.txt** endpoint
3. **GenHub removes fallback logic** in next release

---

## Benefits for GeneralsOnline

### 1. Reduced Maintenance
- **Single endpoint** instead of dual manifest.json + latest.txt
- **Structured schema** reduces parsing errors
- **Versioned API** allows gradual feature rollout

### 2. Enhanced Features
- **Rich metadata** for better UI presentation in GenHub
- **Multiple variants** (30Hz, 60Hz, tournament builds) in one catalog
- **Dependency management** for complex content relationships
- **Changelog integration** for in-app release notes

### 3. Future Extensibility
- **Publisher Studio ready** - schema supports community content
- **Multiple content types** - mods, tools, skins, campaigns
- **Flexible metadata** - add custom fields without breaking changes
- **Version negotiation** - clients can request specific schema versions

### 4. Better Analytics
- **Track content popularity** via download format preferences
- **Monitor update adoption** via version distribution
- **Identify dependency issues** via error reporting

---

## Benefits for GenHub

### 1. Simplified Integration
- **Single parser** for all GeneralsOnline content
- **No fallback logic** - one source of truth
- **Automatic variant detection** - no manual splitting

### 2. Enhanced User Experience
- **Rich metadata** for better content cards
- **Inline changelogs** in update dialogs
- **Dependency visualization** in UI
- **Better error messages** when requirements not met

### 3. Publisher Studio Alignment
- **Schema matches** GenHub's internal manifest format
- **Easy migration** to Publisher Studio when ready
- **Community content support** without code changes

### 4. Performance Improvements
- **Single HTTP request** instead of multiple fallbacks
- **Structured JSON** faster to parse than text files
- **Caching hints** via `cache_max_age_seconds`

---

## Publisher Studio Integration (Future)

When Publisher Studio launches, GeneralsOnline can:

1. **Migrate to Publisher Studio API** with minimal changes
2. **Enable community submissions** (maps, mods, tools)
3. **Implement approval workflow** for curated content
4. **Provide analytics dashboard** for content creators
5. **Support multiple publishers** under GeneralsOnline umbrella

**Publisher Studio Endpoint (Future):**
```
https://api.genhub.gg/v1/publishers/generalsonline/catalog
```

**Migration:**
```json
{
  "endpoints": {
    "catalogUrl": "https://api.genhub.gg/v1/publishers/generalsonline/catalog",
    "custom": {
      "selfHostedCatalog": "https://cdn.playgenerals.online/catalog.json"
    }
  }
}
```

---

## Implementation Recommendations

### For GeneralsOnline Team

1. **Start with minimal schema** - Only populate required fields initially
2. **Use schema validation** - Validate catalog.json against JSON Schema
3. **Implement caching** - Set appropriate `Cache-Control` headers
4. **Monitor errors** - Log parsing failures from GenHub
5. **Version incrementally** - Use `schema_version` for breaking changes

### For GenHub Team

1. **Implement catalog.json parser** alongside existing parser
2. **Add feature flag** for new endpoint preference
3. **Maintain fallback** to manifest.json during migration
4. **Log deprecation warnings** when using legacy endpoints
5. **Update documentation** with new schema

---

## Example Catalog Responses

### Minimal Response (Phase 1)

```json
{
  "schema_version": "1.0",
  "publisher": {
    "id": "generalsonline",
    "name": "Generals Online",
    "website": "https://www.playgenerals.online/"
  },
  "releases": [
    {
      "id": "generalsonline-gameclient-60hz",
      "content_type": "gameclient",
      "name": "Generals Online 60Hz",
      "version": "111825_QFE2",
      "version_date": "2025-11-18T00:00:00Z",
      "release_date": "2025-11-18T14:30:00Z",
      "target_game": "zerohour",
      "downloads": [
        {
          "format": "portable_zip",
          "url": "https://cdn.playgenerals.online/releases/GeneralsOnline_portable_111825_QFE2.zip",
          "size": 1234567890,
          "sha256": "abc123..."
        }
      ]
    }
  ]
}
```

### Full Response (Phase 2+)

See "Proposed API Schema" section above for complete example with all optional fields.

---

## JSON Schema Definition

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "GeneralsOnline Catalog Schema",
  "type": "object",
  "required": ["schema_version", "publisher", "releases"],
  "properties": {
    "schema_version": {
      "type": "string",
      "pattern": "^\\d+\\.\\d+$"
    },
    "publisher": {
      "type": "object",
      "required": ["id", "name", "website"],
      "properties": {
        "id": { "type": "string" },
        "name": { "type": "string" },
        "website": { "type": "string", "format": "uri" },
        "support_url": { "type": "string", "format": "uri" },
        "logo_url": { "type": "string", "format": "uri" },
        "cover_url": { "type": "string", "format": "uri" },
        "theme_color": { "type": "string", "pattern": "^#[0-9A-Fa-f]{6}$" },
        "description": { "type": "string" }
      }
    },
    "releases": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["id", "content_type", "name", "version", "version_date", "release_date", "target_game", "downloads"],
        "properties": {
          "id": { "type": "string" },
          "content_type": {
            "type": "string",
            "enum": ["gameclient", "mappack", "mod", "tool", "skin", "campaign"]
          },
          "variant": { "type": "string" },
          "name": { "type": "string" },
          "version": { "type": "string" },
          "version_date": { "type": "string", "format": "date-time" },
          "release_date": { "type": "string", "format": "date-time" },
          "target_game": {
            "type": "string",
            "enum": ["zerohour", "generals", "cnc3", "kw", "ra3"]
          },
          "description": { "type": "string" },
          "changelog_url": { "type": "string", "format": "uri" },
          "changelog_text": { "type": "string" },
          "tags": {
            "type": "array",
            "items": { "type": "string" }
          },
          "downloads": {
            "type": "array",
            "items": {
              "type": "object",
              "required": ["format"],
              "properties": {
                "format": {
                  "type": "string",
                  "enum": ["portable_zip", "installer_exe", "embedded"]
                },
                "url": { "type": "string", "format": "uri" },
                "size": { "type": "integer", "minimum": 0 },
                "sha256": { "type": "string", "pattern": "^[a-f0-9]{64}$" },
                "md5": { "type": "string", "pattern": "^[a-f0-9]{32}$" },
                "description": { "type": "string" },
                "extraction_path": { "type": "string" },
                "install_target": {
                  "type": "string",
                  "enum": ["workspace", "user_maps_directory", "user_data"]
                }
              }
            }
          },
          "dependencies": {
            "type": "array",
            "items": {
              "type": "object",
              "required": ["type", "id", "name", "required"],
              "properties": {
                "type": {
                  "type": "string",
                  "enum": ["game", "content", "runtime"]
                },
                "id": { "type": "string" },
                "name": { "type": "string" },
                "version": { "type": "string" },
                "version_min": { "type": "string" },
                "version_max": { "type": "string" },
                "required": { "type": "boolean" },
                "description": { "type": "string" }
              }
            }
          },
          "system_requirements": {
            "type": "object",
            "properties": {
              "os": {
                "type": "array",
                "items": { "type": "string" }
              },
              "os_version_min": { "type": "string" },
              "disk_space_mb": { "type": "integer" },
              "ram_mb": { "type": "integer" }
            }
          },
          "metadata": {
            "type": "object",
            "additionalProperties": true
          }
        }
      }
    },
    "update_policy": {
      "type": "object",
      "properties": {
        "check_interval_hours": { "type": "integer", "minimum": 1 },
        "auto_update_recommended": { "type": "boolean" },
        "breaking_changes": { "type": "boolean" }
      }
    },
    "api_metadata": {
      "type": "object",
      "properties": {
        "generated_at": { "type": "string", "format": "date-time" },
        "cache_max_age_seconds": { "type": "integer", "minimum": 0 },
        "next_update_eta": { "type": "string", "format": "date-time" }
      }
    }
  }
}
```

---

## Testing & Validation

### Validation Tools

1. **JSON Schema Validator**: https://www.jsonschemavalidator.net/
2. **GenHub Test Suite**: Automated integration tests
3. **Postman Collection**: API endpoint testing

### Test Cases

1. **Minimal Valid Catalog** - Only required fields
2. **Full Featured Catalog** - All optional fields populated
3. **Multiple Releases** - 60Hz + 30Hz + MapPack
4. **Dependency Chain** - GameClient → MapPack → Zero Hour
5. **Invalid Schema** - Missing required fields (should fail gracefully)
6. **Legacy Fallback** - catalog.json unavailable, use manifest.json

---

## Contact & Support

**GenHub Team:**
- GitHub: https://github.com/enowx/GeneralsHub
- Discord: [GenHub Community Server]

**GeneralsOnline Team:**
- Website: https://www.playgenerals.online/
- Discord: https://discord.playgenerals.online/

---

## Appendix A: Current GenHub Implementation Files

**Key Files:**
- `GenHub/GenHub/Providers/generalsonline.provider.json` - Provider configuration
- `GenHub/GenHub/Features/Content/Services/GeneralsOnline/GeneralsOnlineJsonCatalogParser.cs` - Catalog parser
- `GenHub/GenHub/Features/Content/Services/GeneralsOnline/GeneralsOnlineManifestFactory.cs` - Manifest factory
- `GenHub/GenHub/Features/Content/Services/GeneralsOnline/GeneralsOnlineProfileReconciler.cs` - Update reconciler
- `GenHub/GenHub.Core/Models/GeneralsOnline/GeneralsOnlineApiResponse.cs` - API response model

---

## Appendix B: Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-03-20 | Initial proposal |

---

## Appendix C: FAQ

**Q: Why not just use GitHub Releases API?**
A: GeneralsOnline needs custom metadata (variants, dependencies, game-specific fields) that GitHub Releases doesn't support. This schema is tailored for game content distribution.

**Q: Can we add custom fields to the schema?**
A: Yes! The `metadata` object in each release supports arbitrary key-value pairs. For publisher-level custom fields, contact GenHub team to discuss schema extension.

**Q: What if we need to support multiple games (Generals, Zero Hour, Tiberium Wars)?**
A: Use the `target_game` field and create separate releases for each game. The catalog can contain releases for multiple games.

**Q: How do we handle beta/preview releases?**
A: Add a `release_channel` field to the release object:
```json
{
  "release_channel": "stable",  // or "beta", "preview", "nightly"
  "version": "111825_QFE2-beta1"
}
```

**Q: Can we host the catalog on our own CDN?**
A: Yes! The schema is CDN-agnostic. GenHub only needs the catalog URL in the provider configuration.

---

**End of Document**
