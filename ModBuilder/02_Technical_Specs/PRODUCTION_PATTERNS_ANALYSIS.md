# Production-Grade ModBuilder Patterns Analysis

## Project Overview

**Production Project**: `Z:\GeneralsGameData\Patch104pZH`
**Sample Project**: `Z:\ModBuilderSample\Project`

This document analyzes the advanced features, patterns, and best practices demonstrated in a real-world production ModBuilder project compared to the sample project.

---

## 1. Configuration Architecture

### Multi-File Configuration Strategy

**Production Project (9 JSON files)**:
```
ModJsonFiles.json (orchestrator)
├── ModBundleCoreAudioItems.json      (12 items)
├── ModBundleCoreItems.json           (6 items)
├── ModBundleCoreLanguageItems.json   (11 items)
├── ModBundleOptionalAudioItems.json  (12 items)
├── ModBundleOptionalItems.json       (3 items)
├── ModBundleOptionalLanguageItems.json (10 items)
├── ModBundleRecoveredItems.json      (1 item)
├── ModBundleCorePacks.json           (11 packs)
├── ModBundleFullPacks.json           (11 packs)
├── ModChangeLog.json
└── ModFolders.json
```

**Sample Project (2 JSON files)**:
```
ModJsonFiles.json (not present - uses default discovery)
├── ModBundleItems.json (10 items)
└── ModBundlePacks.json
```

### Key Differences

1. **Separation of Concerns**: Production splits configurations by:
   - Content type (Audio, Language, Data)
   - Purpose (Core, Optional, Recovered)
   - Distribution strategy (Core packs vs Full packs)

2. **Orchestration**: `ModJsonFiles.json` explicitly defines build order:
```json
{
    "build": {
        "version": 1,
        "files": [
            "ModBundleCoreAudioItems.json",
            "ModBundleCoreItems.json",
            "ModBundleCoreLanguageItems.json",
            "ModBundleOptionalAudioItems.json",
            "ModBundleOptionalItems.json",
            "ModBundleOptionalLanguageItems.json",
            "ModBundleRecoveredItems.json",
            "ModBundleCorePacks.json",
            "ModBundleFullPacks.json",
            "ModChangeLog.json",
            "ModFolders.json"
        ]
    }
}
```

3. **Maintainability Benefits**:
   - Easier to locate specific configurations
   - Reduces merge conflicts in team environments
   - Allows parallel development on different content types
   - Clear ownership boundaries

---

## 2. Scale and Complexity

### Bundle Items

| Metric | Production | Sample | Ratio |
|--------|-----------|--------|-------|
| Total Bundle Items | 55 | 10 | 5.5x |
| Core Items | 6 | - | - |
| Core Audio Items | 12 | - | - |
| Core Language Items | 11 | - | - |
| Optional Items | 3 | - | - |
| Optional Audio Items | 12 | - | - |
| Optional Language Items | 10 | - | - |
| Recovered Items | 1 | - | - |

### Bundle Packs

| Metric | Production | Sample | Ratio |
|--------|-----------|--------|-------|
| Core Packs | 11 (one per language) | 2 | 5.5x |
| Full Packs | 11 (one per language) | - | - |
| Total Packs | 22 | 2 | 11x |

### File Count

| Metric | Production | Sample | Ratio |
|--------|-----------|--------|-------|
| GameFilesEdited | 737 files | 75 files | 9.8x |
| Configuration Files | 11 JSON | 4 JSON | 2.75x |

### Naming Conventions

**Production uses strategic prefixes**:
- Core items: `600_900_SuperPatch_`
- Optional items: `600_899_SuperPatch_`
- Recovered items: `600_901_SuperPatch_`
- Packs: `SuperPatch` + `_v0.0` suffix

This ensures proper load order and version management.

---

## 3. Multi-Language Support

### Language Coverage

Production project supports **11 languages**:
1. Arabic
2. Brazilian
3. Chinese
4. English
5. French
6. German
7. Italian
8. Korean
9. Polish
10. Russian
11. Spanish

### Language Implementation Pattern

Each language has three dedicated bundle items:

1. **Audio Item** (`CoreAudio{Language}`):
```json
{
    "name": "CoreAudioEnglish",
    "big": true,
    "files": [{
        "sourceParent": "GameFilesEdited",
        "sourceList": ["Data/Audio/Sounds/English/*.wav"]
    }]
}
```

2. **Language Item** (`CoreLang{Language}`):
```json
{
    "name": "CoreLangEnglish",
    "big": true,
    "setGameLanguageOnInstall": "English",
    "files": [{
        "sourceParent": "GameFilesEdited",
        "sourceTargetList": [{
            "source": "Data/generals.str",
            "target": "Data/English/generals.csf"
        }],
        "params": {
            "language": "English",
            "excludeMarkersList": [[
                "//patch104p-optional-begin",
                "//patch104p-optional-end"
            ]]
        }
    }]
}
```

3. **Optional Language Item** (for extended content)

### Language-Specific Packs

Each language gets two distribution packs:

**Core Pack** (essential content only):
```json
{
    "name": "CoreEnglish",
    "itemNames": [
        "CoreAudio",
        "CoreAudioEnglish",
        "CoreINI",
        "CoreLangEnglish",
        "CoreMaps",
        "CoreMisc",
        "CoreTextures",
        "CoreW3D",
        "CoreWindow"
    ]
}
```

**Full Pack** (core + optional + recovered):
```json
{
    "name": "FullEnglish",
    "itemNames": [
        "OptionalAudio",
        "OptionalAudioEnglish",
        "OptionalINI",
        "OptionalLangEnglish",
        "OptionalTextures",
        "OptionalW3D",
        "CoreAudio",
        "CoreAudioEnglish",
        "CoreINI",
        "CoreLangEnglish",
        "CoreMaps",
        "CoreMisc",
        "CoreTextures",
        "CoreW3D",
        "CoreWindow",
        "RecoveredTextures"
    ]
}
```

### Special Language Handling

Some languages use **target remapping** for compatibility:

**Arabic** (maps to English folder):
```json
{
    "sourceTargetList": [{
        "source": "Data/Arabic/*.ini",
        "target": "Data/English/*.ini"
    }]
}
```

**Russian** (maps to English folder):
```json
{
    "sourceTargetList": [{
        "source": "Data/Russian/*.ini",
        "target": "Data/English/*.ini"
    }]
}
```

---

## 4. File Hash Registry

### Purpose

The file hash registry (`Resources/FileHashRegistry/Generals-108-GeneralsZH-104.csv`) contains **78,263 entries** tracking original game file hashes.

### Usage Pattern

Applied to most bundle items to detect file changes:

```json
{
    "sourceParent": "GameFilesEdited",
    "sourceList": ["Data/INI/**/*.ini"],
    "registryList": [
        "Resources/FileHashRegistry/Generals-108-GeneralsZH-104.csv"
    ]
}
```

### Benefits

1. **Change Detection**: Only rebuild files that differ from originals
2. **Optimization**: Skip unchanged files during builds
3. **Validation**: Verify file integrity
4. **Documentation**: Track which files are modified

---

## 5. Original File Preservation

### Directory Structure

```
GameFilesOriginalCCG/     # Original Command & Conquer Generals files
GameFilesOriginalZH/      # Original Zero Hour files
GameFilesEdited/          # Modified files for the mod
GameFilesOptional/        # Optional enhancement files
```

### Recovered Content Pattern

The `ModBundleRecoveredItems.json` demonstrates restoration of original content:

```json
{
    "name": "RecoveredTextures",
    "big": true,
    "files": [{
        "sourceParent": "GameFilesOriginalCCG",
        "sourceList": [
            "Art/Textures/*.dds",
            "Art/Textures/*.tga"
        ]
    }]
}
```

### Benefits

1. **Version Control**: Preserve original files outside of mod changes
2. **Rollback**: Easy restoration of original content
3. **Comparison**: Diff against originals to see changes
4. **Recovery**: Restore content removed in patches

---

## 6. Optional Content Management

### Three-Tier Content Strategy

1. **Core Content** (required):
   - Essential fixes and improvements
   - Loaded in all installations

2. **Optional Content** (user choice):
   - Enhanced textures
   - Additional audio
   - Extended language support
   - Experimental features

3. **Recovered Content** (restoration):
   - Original files removed in patches
   - Historical content preservation

### Optional Content Implementation

**Optional Items** use separate configuration files:
- `ModBundleOptionalItems.json`
- `ModBundleOptionalAudioItems.json`
- `ModBundleOptionalLanguageItems.json`

**Exclusion Markers** in source files:

```ini
; Core content here
;patch104p-optional-begin
; Optional content here
;patch104p-optional-end
; More core content
```

Configuration excludes optional sections from core builds:

```json
{
    "params": {
        "excludeMarkersList": [[
            ";patch104p-optional-begin",
            ";patch104p-optional-end"
        ]]
    }
}
```

### Distribution Strategy

Users can install:
- **Core Pack**: Essential content only (smaller download)
- **Full Pack**: Core + Optional + Recovered (complete experience)

---

## 7. Advanced File Processing

### Texture Processing Pipeline

**Multiple mipmap strategies**:

1. **Generate Mipmaps**:
```json
{
    "sourceTargetList": [{
        "source": "Art/Textures/GenerateMip/*.psd",
        "target": "Art/Textures/*.dds"
    }],
    "params": {
        "-quality": 255,
        "-mipmode": "Generate"
    }
}
```

2. **No Mipmaps**:
```json
{
    "sourceTargetList": [{
        "source": "Art/Textures/NoMip/*.psd",
        "target": "Art/Textures/*.dds"
    }],
    "params": {
        "-quality": 255,
        "-mipmode": "None"
    }
}
```

3. **TGA Output**:
```json
{
    "sourceTargetList": [{
        "source": "Art/Textures/GenerateTga/*.psd",
        "target": "Art/Textures/*.tga"
    }]
}
```

### 3D Model Processing

**Blender export with multiple configurations**:

1. **Animation Only**:
```json
{
    "source": "Art/Models/Animation/*.blend",
    "target": "Art/W3D/*.W3D",
    "params": {
        "w3dExportHierarchy": true,
        "w3dExportAnimation": true,
        "w3dExportMesh": true
    }
}
```

2. **Hierarchy Only**:
```json
{
    "source": "Art/Models/Hierarchy/*.blend",
    "target": "Art/W3D/*.W3D",
    "params": {
        "w3dExportHierarchy": true,
        "w3dExportAnimation": false,
        "w3dExportMesh": false
    }
}
```

3. **Mesh Only**:
```json
{
    "source": "Art/Models/Mesh/*.blend",
    "target": "Art/W3D/*.W3D",
    "params": {
        "w3dExportHierarchy": false,
        "w3dExportAnimation": false,
        "w3dExportMesh": true
    }
}
```

### INI File Processing

**Advanced text processing**:

```json
{
    "params": {
        "forceEOL": "\r\n",
        "deleteComments": ";",
        "deleteWhitespace": 1,
        "sourceEncoding": "ascii",
        "targetEncoding": "ascii",
        "excludeMarkersList": [[
            ";patch104p-optional-begin",
            ";patch104p-optional-end"
        ]]
    }
}
```

---

## 8. Event Callbacks and Automation

### Python Event Callbacks

**Blender Integration** (`Scripts/Python/OnBuildItemWithBlender3-4-1.py`):

```python
def OnEvent(**kwargs) -> None:
    tools: dict = kwargs.get(TOOLS)
    buildThing = kwargs.get(RAW_BUILD_THING)

    tool = tools.get("blender")
    exec: str = tool.GetExecutable()

    for buildFile in buildThing.files:
        if buildFile.RequiresRebuild():
            source: str = buildFile.AbsSource()
            if HasFileExt(source, "blend"):
                RunBlenderScript(exec, source)
```

**Callback Registration**:

```json
{
    "name": "CoreW3D",
    "onFinishBuildRawBundleItem": {
        "script": "Scripts/Python/OnBuildItemWithBlender3-4-1.py"
    }
}
```

### Batch Script Automation

**Build Workflows**:

1. `BuildInstall.bat` - Build and install
2. `BuildInstallRun.bat` - Build, install, and run game
3. `BuildInstallRunWithGui.bat` - Build with GUI options
4. `BuildRelease.bat` - Build release packages
5. `Uninstall.bat` - Remove installed mod

**Example Build Script**:

```batch
call "%ModBuilderExe%" ^
  --build ^
  --install FullEnglish ^
  --verbose-logging ^
  --config-list %ConfigFiles% %*
```

---

## 9. Changelog Generation

### Automated Changelog System

**Source**: YAML files in `Design/Changes/v1.0/` (925 files)

**Configuration** (`ModChangeLog.json`):

```json
{
    "changelog": {
        "version": 1,
        "records": [
            {
                "sourceList": ["Design/Changes/v1.0/*.yaml"],
                "targetList": ["ReleaseFiles/English/Changes/v1.0/AllSortedByDate.md"],
                "sortList": [{"date": "ascending"}]
            },
            {
                "sourceList": ["Design/Changes/v1.0/*.yaml"],
                "targetList": ["ReleaseFiles/English/Changes/v1.0/AllSortedBySeverity.md"],
                "sortList": [
                    {"label": "blocker"},
                    {"label": "critical"},
                    {"label": "major"},
                    {"label": "minor"},
                    {"date": "ascending"}
                ]
            }
        ]
    }
}
```

### Generated Changelog Variants

1. **AllSortedByDate.md** - Chronological order
2. **AllSortedBySeverity.md** - Priority order (blocker → critical → major → minor)
3. **ControversialOnlySortedByFaction.md** - Filtered by controversy label
4. **ArtOnlySortedByFaction.md** - Art changes only
5. **UsaOnlySortedByDate.md** - USA faction changes
6. **ChinaOnlySortedByDate.md** - China faction changes
7. **GlaOnlySortedByDate.md** - GLA faction changes

### Label System

**Severity Labels**: blocker, critical, major, minor
**Faction Labels**: usa, china, gla, boss, civilian
**Category Labels**: art, controversial

---

## 10. Design and Documentation Structure

### Design Folder Organization

```
Design/
├── Audio/                          # Audio design files
│   ├── Fixing/
│   │   └── AuditionFilters/       # Noise reduction profiles
│   └── gdemsela/                  # Audio project files
│       └── Imported Files/
├── Balancing/                      # Game balance documentation
│   ├── build_times.ods
│   ├── Damage/                    # Damage calculations per unit
│   │   ├── AmericaDrones.ods
│   │   ├── AmericaPatriotBattery.ods
│   │   ├── ChinaTankBattlemaster.ods
│   │   └── ...
│   └── Factions/
├── Changes/                        # Change tracking
│   ├── Legacy/                    # Historical changes
│   └── v1.0/                      # Current version (925 YAML files)
├── Launcher/                       # Launcher design
│   ├── Mockup/
│   └── Origin/
└── References/                     # Reference materials
    ├── Mods/
    └── Window/
```

### Documentation Best Practices

1. **Balancing Spreadsheets**: Track unit statistics and damage calculations
2. **Audio Profiles**: Preserve noise reduction settings
3. **Change Tracking**: Individual YAML files per change (granular history)
4. **Reference Materials**: Store original files and mod references
5. **Design Mockups**: UI/UX design iterations

---

## 11. Build Automation Patterns

### Batch Script Hierarchy

```
Scripts/
├── BuildInstall.bat
├── BuildInstallRun.bat
├── BuildInstallRunWithGui.bat
├── BuildRelease.bat
├── Uninstall.bat
├── Python/
│   ├── OnBuildItemWithBlender3-4-1.py
│   └── common.py
└── Windows/
    ├── RequestAdmin.bat
    ├── InstallModBuilder.bat
    ├── Setup.bat
    ├── WindowsRunner.json
    └── WindowsTools.json
```

### Admin Elevation Pattern

```batch
call "%ThisDir%\Windows\RequestAdmin.bat" "%~s0" %*

if %errorlevel% equ 111 (
    exit /B 0
)
```

### Modular Script Design

1. **RequestAdmin.bat** - Handle UAC elevation
2. **InstallModBuilder.bat** - Install/update ModBuilder
3. **Setup.bat** - Configure environment variables
4. **Build scripts** - Execute specific workflows

---

## 12. Production Best Practices

### 1. Content Organization

**Separation by Type**:
- Audio files in dedicated configurations
- Language files in dedicated configurations
- Data files (INI, maps, windows) grouped logically

**Separation by Purpose**:
- Core (required)
- Optional (user choice)
- Recovered (restoration)

### 2. Maintainability

**File Naming**:
- Descriptive configuration names
- Consistent prefixes/suffixes
- Version indicators in pack names

**Configuration Size**:
- Keep individual configs focused
- Split large configs by content type
- Use orchestrator file for build order

### 3. Team Collaboration

**Parallel Development**:
- Audio team works in `ModBundleCoreAudioItems.json`
- Language team works in `ModBundleCoreLanguageItems.json`
- Content team works in `ModBundleCoreItems.json`

**Merge Conflict Reduction**:
- Smaller, focused configuration files
- Clear ownership boundaries
- Independent change tracking

### 4. Version Management

**Naming Strategy**:
```
itemsPrefix: "600_900_SuperPatch_"
packsSuffix: "_v0.0"
```

**Benefits**:
- Load order control (600 series)
- Version identification (_v0.0)
- Namespace separation (SuperPatch_)

### 5. Quality Assurance

**File Hash Registry**:
- Track original file states
- Detect unintended changes
- Validate file integrity

**Exclusion Markers**:
- Separate optional content
- Enable A/B testing
- Support multiple distributions

### 6. Build Optimization

**Incremental Builds**:
- File hash comparison
- Rebuild only changed files
- Cache intermediate results

**Parallel Processing**:
- Independent bundle items
- Concurrent file processing
- Multi-core utilization

### 7. Distribution Strategy

**Multiple Pack Variants**:
- Core packs (smaller, essential)
- Full packs (complete experience)
- Language-specific packs

**User Choice**:
- Install only needed languages
- Choose core vs full
- Add optional content later

---

## 13. Comparison Summary

### Configuration Complexity

| Aspect | Sample | Production | Multiplier |
|--------|--------|-----------|-----------|
| JSON Files | 2 | 9 | 4.5x |
| Bundle Items | 10 | 55 | 5.5x |
| Bundle Packs | 2 | 22 | 11x |
| Source Files | 75 | 737 | 9.8x |
| Languages | 3 | 11 | 3.7x |
| Changelog Entries | 0 | 925 | ∞ |

### Feature Usage

| Feature | Sample | Production |
|---------|--------|-----------|
| Multi-file configs | ❌ | ✅ |
| File hash registry | ✅ | ✅ (78K entries) |
| Original file preservation | ❌ | ✅ |
| Optional content system | ❌ | ✅ |
| Multi-language support | Basic | Advanced (11 languages) |
| Changelog generation | ❌ | ✅ (7 variants) |
| Event callbacks | ✅ | ✅ |
| Design documentation | ❌ | ✅ |
| Balancing spreadsheets | ❌ | ✅ |
| Build automation | Basic | Advanced |

### Organizational Patterns

**Sample Project**:
- Single configuration file
- All items in one place
- Simple structure
- Quick to understand

**Production Project**:
- Multi-file configuration
- Separated by concern
- Complex structure
- Optimized for scale

---

## 14. Key Takeaways

### When to Use Simple Configuration (Sample Pattern)

- Small mods (< 100 files)
- Single developer
- Single language
- Rapid prototyping
- Learning ModBuilder

### When to Use Advanced Configuration (Production Pattern)

- Large mods (> 500 files)
- Team development
- Multiple languages
- Multiple distribution variants
- Long-term maintenance
- Professional releases

### Migration Path

1. **Start Simple**: Use single configuration file
2. **Add Languages**: Split audio and language items
3. **Add Optional Content**: Create optional item configs
4. **Add Changelog**: Implement YAML-based tracking
5. **Add Automation**: Create build scripts
6. **Add Documentation**: Organize design files
7. **Optimize**: Implement file hash registry

### Critical Success Factors

1. **Clear Separation**: Content type, purpose, language
2. **Consistent Naming**: Prefixes, suffixes, conventions
3. **Documentation**: Design files, balancing data, references
4. **Automation**: Build scripts, callbacks, changelog generation
5. **Version Control**: File hashes, original preservation
6. **Distribution Strategy**: Core vs full, language variants
7. **Team Workflow**: Parallel development, merge conflict reduction

---

## Conclusion

The production project demonstrates ModBuilder's capability to handle enterprise-scale game modifications with:

- **55 bundle items** across 9 configuration files
- **11 languages** with dedicated audio and localization
- **737 source files** with automated processing
- **78,263 file hash entries** for change detection
- **925 changelog entries** with automated generation
- **22 distribution packs** for flexible deployment

This represents a **10x scale increase** over the sample project while maintaining organization, maintainability, and build performance through strategic use of ModBuilder's advanced features.
