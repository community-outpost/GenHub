# GeneralsOnline API Schema - Executive Summary

**TL;DR:** Unified catalog API to replace manifest.json + latest.txt, enabling Publisher Studio integration and extensible content distribution.

---

## Current Problems

1. **Dual endpoints** (manifest.json + latest.txt) require fallback logic
2. **Limited metadata** - no variants, dependencies, or rich content info
3. **Manual splitting** - GenHub must manually create 60Hz + MapPack manifests
4. **Not extensible** - can't add mods, tools, or community content
5. **No Publisher Studio alignment** - will require rewrite when PS launches

---

## Proposed Solution

**Single Endpoint:** `https://cdn.playgenerals.online/catalog.json`

**Key Features:**
- ✅ Multiple content types (game clients, map packs, mods, tools)
- ✅ Variant support (60Hz, 30Hz, tournament builds)
- ✅ Dependency management (requires Zero Hour 1.04, QuickMatch maps)
- ✅ Rich metadata (changelogs, cover images, tags)
- ✅ Publisher Studio ready (same schema)
- ✅ Backward compatible migration path

---

## Minimal Example

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
      ],
      "dependencies": [
        {
          "type": "game",
          "id": "zerohour",
          "name": "C&C Generals Zero Hour",
          "version_min": "1.04",
          "required": true
        }
      ]
    }
  ]
}
```

---

## Migration Plan

### Phase 1: Parallel (Weeks 1-2)
- Deploy catalog.json alongside manifest.json
- GenHub prefers catalog.json, falls back to manifest.json
- Monitor adoption

### Phase 2: Deprecation (Weeks 3-4)
- Add deprecation headers to manifest.json
- Community announcement
- GenHub logs warnings

### Phase 3: Sunset (Week 5+)
- Redirect manifest.json → catalog.json
- Remove latest.txt
- GenHub removes fallback logic

---

## Benefits

### For GeneralsOnline
- **Reduced maintenance** - single endpoint instead of two
- **Enhanced features** - rich metadata, variants, dependencies
- **Future-proof** - Publisher Studio ready
- **Better analytics** - track content popularity

### For GenHub
- **Simplified integration** - single parser, no fallback logic
- **Better UX** - rich content cards, inline changelogs
- **Publisher Studio alignment** - same schema
- **Performance** - single HTTP request

---

## What GenHub Currently Does

1. **Fetches** manifest.json (or falls back to latest.txt)
2. **Parses** version, download URL, size, changelog
3. **Creates TWO manifests** from single release:
   - 60Hz Game Client (executable + shared files)
   - QuickMatch MapPack (multiplayer maps)
4. **Computes SHA-256 hashes** for all files post-extraction
5. **Integrates with CAS** (Content-Addressable Storage)
6. **Checks for updates** every 24 hours
7. **Reconciles profiles** when updates detected

---

## What New Schema Enables

1. **Multiple variants** in one catalog (60Hz, 30Hz, tournament)
2. **Dependency declarations** (requires Zero Hour, requires MapPack)
3. **Rich metadata** (changelogs, cover images, tags)
4. **Multiple content types** (game clients, maps, mods, tools)
5. **Publisher Studio migration** without code changes
6. **Community content** when Publisher Studio launches

---

## Next Steps

1. **Review** full proposal: `GENERALSONLINE_API_SCHEMA_PROPOSAL.md`
2. **Validate** JSON schema against your current data
3. **Implement** catalog.json endpoint (start minimal)
4. **Test** with GenHub development team
5. **Deploy** in parallel with existing endpoints
6. **Migrate** gradually over 4-6 weeks

---

## Questions?

**GenHub Team:**
- GitHub: https://github.com/enowx/GeneralsHub
- Discord: [GenHub Community]

**Full Documentation:**
- See `GENERALSONLINE_API_SCHEMA_PROPOSAL.md` for complete schema
- See `GenHub/GenHub/Providers/generalsonline.provider.json` for current config
- See `GenHub/GenHub/Features/Content/Services/GeneralsOnline/` for implementation

---

**Status:** ✅ Ready for review
**Priority:** Medium (enables Publisher Studio integration)
**Effort:** Low (minimal schema to start, gradual enhancement)
