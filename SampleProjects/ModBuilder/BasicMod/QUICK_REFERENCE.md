# BasicMod Quick Reference

## File Structure
```
GameFilesEdited/          → Your edited files (source)
  ├── Data/INI/           → Game data files
  ├── Data/Audio/         → Sound files
  └── Art/Textures/       → Image files

.Build/                   → Intermediate files (auto-generated)
  └── build_cache.msgpack → Build cache for fast rebuilds

.Release/                 → Final output (auto-generated)
  └── BasicMod.big        → Complete mod package
```

## Workflow
1. **Edit** files in `GameFilesEdited/`
2. **Configure** processing in `config/ModBundleItems.json`
3. **Build** to create `.big` archive
4. **Install** to game folder
5. **Test** in-game

## File Status Colors
- 🔴 Red = Modified (different from game)
- 🟢 Green = New (not in game)
- ⚪ Gray = Unchanged (same as game)

## Build Options
- ✅ **Build** - Process files and create archive
- ✅ **Release** - Create final package
- ✅ **Install** - Copy to game folder
- ✅ **Run Game** - Launch game after install

## Configuration Files

### ModBundleItems.json
Defines **how files are processed**:
- **Name** - Unique identifier
- **SourceFiles** - Glob patterns (`**/*.ini`)
- **OutputFormat** - Target format (INI, DDS, WAV)
- **Compression** - For textures (DXT1, DXT5, BC7)

### ModBundlePacks.json
Defines **how items are bundled**:
- **Name** - Bundle pack name
- **Items** - List of bundle items to include
- **OutputFile** - Where to create .big file

## Common Tasks

### Add a file to project
1. Browse game files in File Manager
2. Right-click → "Add to Project"
3. File is copied to `GameFilesEdited/`
4. Edit the file
5. Rebuild

### Change texture compression
Edit `config/ModBundleItems.json`:
```json
"Compression": "DXT1"  // No alpha, smallest
"Compression": "DXT5"  // With alpha, medium
"Compression": "BC7"   // Best quality, largest
```

### Add new bundle item
1. Edit `config/ModBundleItems.json` - add item
2. Edit `config/ModBundlePacks.json` - add to pack
3. Rebuild

## Troubleshooting

**Build processes 0 files?**
- Check files exist in `GameFilesEdited/`
- Verify wildcards match files
- Check JSON syntax

**Game doesn't show changes?**
- Verify "Install" was checked
- Check `.Release/BasicMod.big` exists
- Verify game launched from correct installation

**Build is slow?**
- Check cache exists (`.Build/build_cache.msgpack`)
- Delete cache to reset
- Verify only changed files are processed

## Performance
- First build: ~2-5 seconds
- Cached build: ~0.5-1 second
- Cache tracks file hashes for fast rebuilds

## Glob Patterns
- `**/*.ini` - All INI files recursively
- `Data/**/*.ini` - All INI under Data/
- `*.ini` - INI files in root only
- `**/{Object,Weapon}/*.ini` - Multiple folders
