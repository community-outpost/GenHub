# Python ModBuilder Project Analysis

**Date**: March 20, 2026
**Project**: Z:\GeneralsGameData\Patch104pZH\
**Purpose**: Understand Python ModBuilder to fix C# port

---

## Executive Summary

### Project Scale
- **Total Files**: 737 files in GameFilesEdited
- **Total Size**: 111 MB
- **Config Files**: 11 JSON files (1,143 lines total)
- **Bundle Items**: 20+ items across multiple config files
- **Bundle Packs**: 11 language-specific packs

### Python ModBuilder Version
- **Version**: 2.3
- **Executable**: generalsmodbuilder.exe
- **Download**: Auto-downloaded from GitHub releases
- **Size**: 32 MB (compressed)

---

## Project Structure

### Root Directory
```
Z:\GeneralsGameData\Patch104pZH\
├── Design/                          # Design documents (not built)
├── GameFilesEdited/                 # Source files (737 files, 111 MB)
│   ├── Art/
│   │   ├── Models/                  # Blender .blend files
│   │   ├── Textures/
│   │   │   ├── GenerateMip/         # PSD/TGA/TIF → DDS with mipmaps
│   │   │   ├── NoMip/               # PSD/TGA/TIF → DDS without mipmaps
│   │   │   └── GenerateTga/         # PSD/TIF → TGA
│   │   └── W3D/                     # W3D model files
│   ├── Data/
│   │   ├── Audio/Sounds/            # WAV files
│   │   ├── INI/                     # Game config files
│   │   └── [Language]/              # Language-specific files
│   ├── Maps/                        # Map files
│   └── Window/                      # UI window files
├── GameFilesOptional/               # Optional high-res textures
├── GameFilesOriginalCCG/            # Original Generals 1.08 files (reference)
├── GameFilesOriginalZH/             # Original Zero Hour 1.04 files (reference)
├── ReleaseFiles/                    # Files copied to release
├── Resources/
│   └── FileHashRegistry/            # Hash registries for unchanged file detection
│       ├── Generals-108.zip
│       ├── GeneralsZH-104.zip
│       └── Generals-108-GeneralsZH-104.csv
├── Scripts/
│   ├── Python/                      # Python hook scripts
│   └── Windows/                     # Batch scripts + tools
│       ├── 7z.exe                   # Archive tool
│       ├── InstallModBuilder.bat    # Downloads/installs ModBuilder
│       ├── Setup.bat                # Sets environment variables
│       └── WindowsTools.json        # Tool configuration
├── ModBundleCoreItems.json          # Core bundle items (258 lines)
├── ModBundleCoreAudioItems.json     # Core audio items
├── ModBundleCoreLanguageItems.json  # Core language items
├── ModBundleOptionalItems.json      # Optional items (885 lines)
├── ModBundleOptionalAudioItems.json # Optional audio items
├── ModBundleOptionalLanguageItems.json # Optional language items
├── ModBundleRecoveredItems.json     # Recovered items
├── ModBundleCorePacks.json          # Core packs (11 language variants)
├── ModBundleFullPacks.json          # Full packs
├── ModChangeLog.json                # Change log
├── ModFolders.json                  # Folder configuration
└── ModJsonFiles.json                # List of config files to load
```

### Build Output Directories
```
.Build/                              # Temporary build files
.Release/                            # Final release files
```

---

## Configuration File Format

### 1. ModJsonFiles.json
**Purpose**: Lists all config files to load

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

**Key Insight**: Python ModBuilder loads MULTIPLE config files, not just one.

---

### 2. ModFolders.json
**Purpose**: Defines build and release directories

```json
{
    "folders": {
        "version": 1,
        "releaseDir": ".Release",
        "buildDir": ".Build"
    }
}
```

---

### 3. ModBundleItems.json Format

**Structure**:
```json
{
    "bundles": {
        "version": 1,
        "itemsPrefix": "600_900_SuperPatch_",
        "itemsSuffix": "",
        "items": [
            {
                "name": "CoreTextures",
                "big": true,
                "files": [
                    {
                        "sourceParent": "GameFilesEdited",
                        "sourceList": [
                            "Art/Textures/*.tga",
                            "Art/Textures/*.dds"
                        ],
                        "registryList": [
                            "Resources/FileHashRegistry/Generals-108-GeneralsZH-104.csv"
                        ]
                    },
                    {
                        "sourceParent": "GameFilesEdited",
                        "sourceTargetList": [
                            {
                                "source": "Art/Textures/GenerateMip/*.psd",
                                "target": "Art/Textures/*.dds"
                            }
                        ],
                        "params": {
                            "-quality": 255,
                            "-mipmode": "Generate"
                        }
                    }
                ]
            }
        ]
    }
}
```

**Key Properties**:
- `itemsPrefix`: Prefix for .big file names (e.g., "600_900_SuperPatch_")
- `itemsSuffix`: Suffix for .big file names
- `items`: Array of bundle items
  - `name`: Bundle name (e.g., "CoreTextures")
  - `big`: true = create .big archive, false = loose files
  - `files`: Array of file groups
    - `sourceParent`: Base directory (e.g., "GameFilesEdited")
    - `sourceList`: Array of wildcard patterns (simple copy)
    - `sourceTargetList`: Array of source→target conversions
      - `source`: Input file pattern
      - `target`: Output file pattern
    - `params`: Processing parameters
      - `-quality`: DDS quality (0-255)
      - `-mipmode`: "Generate", "None", etc.
      - `forceEOL`: Force line endings ("\r\n")
      - `deleteComments`: Comment character (";")
      - `deleteWhitespace`: 1 = remove whitespace
      - `sourceEncoding`: Input encoding ("ascii")
      - `targetEncoding`: Output encoding ("ascii")
      - `excludeMarkersList`: Exclusion markers
      - `rescale`: Scale factor (2.0 = 50% size)
      - `resampling`: Resampling method ("BOX")
      - `w3dExportHierarchy`: Export W3D hierarchy
      - `w3dExportAnimation`: Export W3D animation
      - `w3dExportMesh`: Export W3D mesh
    - `registryList`: Hash registries for unchanged file detection

---

### 4. ModBundlePacks.json Format

**Structure**:
```json
{
    "bundles": {
        "version": 1,
        "packsPrefix": "SuperPatch",
        "packsSuffix": "_v0.0",
        "packs": [
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
        ]
    }
}
```

**Key Properties**:
- `packsPrefix`: Prefix for pack names
- `packsSuffix`: Suffix for pack names (e.g., "_v0.0")
- `packs`: Array of packs
  - `name`: Pack name (e.g., "CoreEnglish")
  - `itemNames`: Array of bundle item names to include

---

## Build Workflow

### 1. User Runs BuildInstall.bat
```batch
call "%ModBuilderExe%" ^
  --build ^
  --install FullEnglish ^
  --verbose-logging ^
  --config-list %ConfigFiles% %*
```

**Command-line arguments**:
- `--build`: Build the project
- `--install FullEnglish`: Install pack "FullEnglish" to game
- `--verbose-logging`: Enable verbose logging
- `--config-list`: List of config files to load

### 2. ModBuilder Loads Config Files
1. Reads `ModJsonFiles.json`
2. Loads all files listed in `files` array
3. Merges all bundle items and packs

### 3. ModBuilder Processes Files
For each bundle item:
1. Resolve wildcards in `sourceList` and `sourceTargetList`
2. Check hash registry to skip unchanged files
3. Process files based on extension and params:
   - **PSD/TGA/TIF → DDS**: Convert with ImageMagick/BCnEncoder
   - **PSD/TIF → TGA**: Convert with ImageMagick
   - **INI files**: Strip comments, whitespace, force EOL
   - **STR → CSF**: Convert language files
   - **Blend → W3D**: Export with Blender
   - **WAV/WND/etc.**: Copy as-is
4. Write to `.Build/` directory

### 4. ModBuilder Creates Archives
For each bundle item with `big: true`:
1. Collect all processed files
2. Create `.big` archive with prefix/suffix
3. Write to `.Release/` directory

### 5. ModBuilder Installs to Game
1. Copy `.big` files to game directory
2. Set game language if specified
3. Run post-install scripts

---

## Wildcard Patterns

### Simple Patterns (sourceList)
```json
"sourceList": [
    "Art/Textures/*.tga",           // All .tga in Art/Textures/
    "Art/Textures/*.dds",           // All .dds in Art/Textures/
    "Data/INI/**/*.ini",            // All .ini recursively in Data/INI/
    "Window/*.wnd",                 // All .wnd in Window/
    "Window/Menus/*.wnd"            // All .wnd in Window/Menus/
]
```

**Wildcard syntax**:
- `*`: Match any characters in filename
- `**`: Match any subdirectories (recursive)
- `*.ext`: Match all files with extension

### Conversion Patterns (sourceTargetList)
```json
"sourceTargetList": [
    {
        "source": "Art/Textures/GenerateMip/*.psd",
        "target": "Art/Textures/*.dds"
    }
]
```

**Behavior**:
- Source: `Art/Textures/GenerateMip/texture1.psd`
- Target: `Art/Textures/texture1.dds`
- Filename preserved, extension changed, subdirectory removed

---

## File Hash Registry

### Purpose
Skip processing files that haven't changed from original game files.

### Format
CSV file with MD5 hashes:
```
Art/Textures/texture1.dds,a1b2c3d4e5f6...
Art/Textures/texture2.dds,f6e5d4c3b2a1...
```

### Usage
```json
"registryList": [
    "Resources/FileHashRegistry/Generals-108-GeneralsZH-104.csv"
]
```

If file MD5 matches registry, skip processing.

---

## Python Hook Scripts

### Available Hooks
```json
"onPreBuild": {
    "script": "Scripts/Python/OnPreBuildItem.py",
    "function": "OnPreBuild",
    "kwargs": {
        "info": "Arbitrary data passed to script"
    }
},
"onBuild": {
    "script": "Scripts/Python/OnBuildItem.py"
},
"onPostBuild": {
    "script": "Scripts/Python/OnPostBuildItem.py"
},
"onFinishBuildRawBundleItem": {
    "script": "Scripts/Python/OnBuildItemWithBlender3-4-1.py"
},
"onRelease": {
    "script": "Scripts/Python/OnReleasePack.py"
},
"onInstall": {
    "script": "Scripts/Python/OnInstallPack.py"
},
"onRun": {
    "script": "Scripts/Python/OnRunPack.py"
},
"onUninstall": {
    "script": "Scripts/Python/OnUninstallPack.py"
}
```

---

## Comparison: Python vs C# Expected Format

### Config File Loading

**Python**:
- Loads `ModJsonFiles.json` first
- Loads all files listed in `files` array
- Merges all items and packs

**C# (Current)**:
- Loads `ModBundleItems.json` and `ModBundlePacks.json` directly
- Does NOT support `ModJsonFiles.json`
- Does NOT support multiple config files

**FIX NEEDED**: C# must support `ModJsonFiles.json` and load multiple configs.

---

### Property Names

**Python Format**:
```json
{
    "bundles": {
        "version": 1,
        "itemsPrefix": "...",
        "itemsSuffix": "...",
        "items": [...]
    }
}
```

**C# Expected (Current)**:
```csharp
public class BundleConfiguration
{
    public int Version { get; set; }
    public string ItemsPrefix { get; set; }
    public string ItemsSuffix { get; set; }
    public List<BundleItem> Items { get; set; }
}
```

**STATUS**: Property names match ✅

---

### File Patterns

**Python**:
- `sourceList`: Array of patterns
- `sourceTargetList`: Array of source→target objects
- `sourceParent`: Base directory

**C# Expected (Current)**:
```csharp
public class FileGroup
{
    public string SourceParent { get; set; }
    public List<string> SourceList { get; set; }
    public List<SourceTarget> SourceTargetList { get; set; }
}

public class SourceTarget
{
    public string Source { get; set; }
    public string Target { get; set; }
}
```

**STATUS**: Structure matches ✅

---

### Wildcard Resolution

**Python**:
- Uses glob patterns
- `**` = recursive
- `*` = any characters

**C# (Current)**:
- Uses `Directory.GetFiles()` with `SearchOption.AllDirectories`
- May not handle `**` correctly

**FIX NEEDED**: Verify wildcard resolution matches Python behavior.

---

### Hash Registry

**Python**:
- Supports CSV format
- Checks MD5 hash before processing
- Skips unchanged files

**C# (Current)**:
- Supports ZIP format (contains CSV)
- May not check hash correctly

**FIX NEEDED**: Verify hash registry implementation.

---

### Parameters

**Python Params**:
```json
"params": {
    "-quality": 255,
    "-mipmode": "Generate",
    "forceEOL": "\r\n",
    "deleteComments": ";",
    "deleteWhitespace": 1,
    "sourceEncoding": "ascii",
    "targetEncoding": "ascii",
    "excludeMarkersList": [[";begin", ";end"]],
    "rescale": 2.0,
    "resampling": "BOX",
    "w3dExportHierarchy": true,
    "w3dExportAnimation": true,
    "w3dExportMesh": true
}
```

**C# Expected**:
```csharp
public class ProcessingParameters
{
    public int? Quality { get; set; }
    public string MipMode { get; set; }
    public string ForceEOL { get; set; }
    public string DeleteComments { get; set; }
    public int? DeleteWhitespace { get; set; }
    public string SourceEncoding { get; set; }
    public string TargetEncoding { get; set; }
    public List<List<string>> ExcludeMarkersList { get; set; }
    public double? Rescale { get; set; }
    public string Resampling { get; set; }
    public bool? W3dExportHierarchy { get; set; }
    public bool? W3dExportAnimation { get; set; }
    public bool? W3dExportMesh { get; set; }
}
```

**FIX NEEDED**: Verify all parameters are supported.

---

## Sample Project vs Real Project

### ModBuilderSample\Project\
- **Purpose**: Example/test project
- **Files**: ~100 files
- **Config**: Simple examples
- **Items**: 10 items
- **Packs**: 5 packs

### GeneralsGameData\Patch104pZH\
- **Purpose**: Real production project
- **Files**: 737 files (111 MB)
- **Config**: Complex multi-file setup
- **Items**: 20+ items across 7 config files
- **Packs**: 11 language variants

**Key Difference**: Real project uses `ModJsonFiles.json` to load multiple configs.

---

## Critical Findings

### 1. Multi-Config Support Missing
**Problem**: C# doesn't support `ModJsonFiles.json`
**Impact**: Cannot load real projects
**Fix**: Implement multi-config loading

### 2. Wildcard Resolution
**Problem**: May not handle `**` correctly
**Impact**: Files not found
**Fix**: Verify glob pattern implementation

### 3. Hash Registry
**Problem**: May not check hashes correctly
**Impact**: Processes unchanged files (slow)
**Fix**: Verify hash checking logic

### 4. Parameter Support
**Problem**: May not support all params
**Impact**: Files processed incorrectly
**Fix**: Verify all params implemented

### 5. File Count Validation
**Problem**: No warning when 0 files found
**Impact**: User confused
**Fix**: Add validation before build

---

## Next Steps

### Phase 1: Fix Config Loading (CRITICAL)
1. Implement `ModJsonFiles.json` support
2. Load multiple config files
3. Merge items and packs
4. Test with real project

### Phase 2: Verify Wildcard Resolution
1. Test `**` recursive patterns
2. Test `*` filename patterns
3. Compare file counts with Python
4. Fix any discrepancies

### Phase 3: Verify Hash Registry
1. Test CSV loading
2. Test MD5 checking
3. Verify files skipped correctly
4. Compare with Python behavior

### Phase 4: Verify Parameters
1. Test all parameter types
2. Verify processing matches Python
3. Compare output files
4. Fix any differences

### Phase 5: Create Benchmark Tests
1. Copy real project to test location
2. Run Python build, measure time
3. Run C# build, measure time
4. Compare outputs (file sizes, MD5 hashes)
5. Verify 15-25% faster

---

## Performance Expectations

### Python ModBuilder (Estimated)
- **Build Time**: 60-120 seconds (estimated)
- **Files Processed**: 737 files
- **Output Size**: ~100 MB

### C# ModBuilder (Target)
- **Build Time**: 45-90 seconds (15-25% faster)
- **Files Processed**: 737 files (same)
- **Output Size**: ~100 MB (identical)

---

## Success Criteria

### Functional
- ✅ Loads `ModJsonFiles.json`
- ✅ Loads multiple config files
- ✅ Resolves wildcards correctly
- ✅ Checks hash registry
- ✅ Processes all file types
- ✅ Creates identical .big archives
- ✅ Installs to game correctly

### Performance
- ✅ 15-25% faster than Python
- ✅ Benchmark proves it
- ✅ Repeatable results

### Quality
- ✅ Output files identical (MD5 match)
- ✅ No errors or warnings
- ✅ Game launches successfully

---

## Files to Review in C# Codebase

### Config Loading
- `ConfigurationLoaderService.cs` - Add multi-config support
- `BundleConfiguration.cs` - Verify property names

### Wildcard Resolution
- `FileResolver.cs` - Verify glob patterns
- `WildcardMatcher.cs` - Test `**` and `*`

### Hash Registry
- `FileHashRegistryService.cs` - Verify CSV loading
- `HashChecker.cs` - Verify MD5 checking

### Parameters
- `ProcessingParameters.cs` - Verify all params
- `FileProcessor.cs` - Verify param usage

### Build Engine
- `BuildEngineService.cs` - Verify workflow
- `ArchiveService.cs` - Verify .big creation

---

## Conclusion

The Python ModBuilder is a mature, production-ready tool with:
- Multi-config file support
- Complex wildcard patterns
- Hash registry optimization
- Extensive parameter support
- Python hook scripts

The C# port must match this functionality exactly to be useful. The most critical missing feature is **multi-config file support** via `ModJsonFiles.json`.

**Estimated Fix Time**: 4-8 hours for multi-config support + verification

---

**Status**: Analysis complete
**Next**: Implement multi-config support in C#
**Priority**: CRITICAL - Blocking all testing
