# Python ModBuilder Analysis - Real Workflow Documentation

**Date**: 2026-03-20
**Purpose**: Document ACTUAL Python ModBuilder workflow to guide C# implementation

---

## Section 1: Python Project Structure

### Complete Directory Tree
```
Project/
├── Documents/
│   └── Changes/                    # YAML change logs for each modification
│       ├── 1_avsentry.yaml
│       ├── 2_avhummer.yaml
│       └── ...
│
├── GameFilesEdited/                # USER'S EDITED GAME FILES (source files)
│   ├── Art/
│   │   ├── Models/                 # Blender .blend files
│   │   │   └── AVSentry.blend
│   │   ├── *.psd                   # Photoshop source textures
│   │   ├── *.tga                   # TGA source textures
│   │   └── *.tif                   # TIFF source textures
│   │
│   ├── Data/
│   │   ├── Audio/Sounds/           # WAV audio files
│   │   ├── INI/                    # Game configuration files
│   │   │   ├── GameData.ini
│   │   │   ├── Object/*.ini
│   │   │   └── ...
│   │   ├── English/*.ini           # Language files
│   │   ├── generals.str            # String table source
│   │   └── ...
│   │
│   └── Window/                     # UI window definitions
│       └── *.wnd
│
├── ReleaseFiles/                   # STATIC FILES copied as-is to release
│   ├── Doc/
│   │   └── ReadMe.txt
│   └── ...
│
├── Resources/
│   └── FileHashRegistry/           # Hash registries for unchanged file detection
│       ├── GeneralsZH-104.zip      # Zero Hour 1.04 file hashes
│       ├── Generals-108.zip        # Generals 1.08 file hashes
│       └── Generals-108-GeneralsZH-104.zip
│
├── Scripts/
│   ├── Python/                     # Python hook scripts
│   │   ├── OnPreBuildItem.py
│   │   ├── OnBuildItem.py
│   │   ├── OnPostBuildItem.py
│   │   ├── OnPreBuildPack.py
│   │   ├── OnReleasePack.py
│   │   ├── OnInstallPack.py
│   │   ├── OnRunPack.py
│   │   └── OnUninstallPack.py
│   │
│   └── Windows/                    # Windows batch scripts
│       ├── Setup.bat               # Downloads/configures ModBuilder
│       ├── InstallModBuilder.bat
│       └── RequestAdmin.bat
│
├── BuildInstall.bat                # Main build + install script
├── BuildRelease.bat                # Build release packages
├── ModBundleItems.json             # DEFINES WHAT TO BUILD (items)
├── ModBundlePacks.json             # DEFINES WHAT TO PACKAGE (packs)
├── ModChangeLog.json               # Changelog generation config
└── ModFolders.json                 # Output folder configuration

# Generated during build (not in source):
├── .Build/                         # Temporary build artifacts
│   └── (intermediate files)
│
└── .Release/                       # Final release packages
    └── (BIG archives, installers)
```

---

## Section 2: Python Workflow - Step by Step

### User Workflow

#### Step 1: User Edits Game Files
```
User modifies files in GameFilesEdited/:
- Edit Art/Models/AVSentry.blend (3D model)
- Edit Art/RGB_PSD.psd (texture)
- Edit Data/INI/Object/FactionUnit.ini (game config)
- Edit Data/generals.str (strings)
```

#### Step 2: User Runs Build Script
```batch
Scripts\BuildInstall.bat
```

**What BuildInstall.bat does:**
1. Requests admin privileges (RequestAdmin.bat)
2. Downloads/installs ModBuilder if needed (InstallModBuilder.bat)
3. Sets up environment variables (Setup.bat)
4. Calls ModBuilder executable:
   ```
   generalsmodbuilder.exe --build --install --verbose-logging --config-list <configs>
   ```

#### Step 3: ModBuilder Processes Files

**Phase 1: Read Configuration**
- Loads `ModBundleItems.json` (what to build)
- Loads `ModBundlePacks.json` (what to package)
- Loads `ModFolders.json` (where to output)
- Loads `ModChangeLog.json` (changelog generation)

**Phase 2: Build Items** (for each item in ModBundleItems.json)

Example: `SampleTexturesDDS512` item
```json
{
  "name": "SampleTexturesDDS512",
  "big": true,
  "files": [
    {
      "sourceParent": "GameFilesEdited",
      "sourceTargetList": [
        { "source": "Art/*.psd", "target": "Art/*.dds" },
        { "source": "Art/*.tga", "target": "Art/*.dds" }
      ],
      "params": {
        "rescale": 2.0,
        "resampling": "BOX",
        "-quality": 255
      }
    }
  ]
}
```

**Processing:**
1. Find all `GameFilesEdited/Art/*.psd` files
2. For each PSD:
   - Load PSD with alpha compositing
   - Rescale by 2.0x (downscale)
   - Convert to DDS with BC3 compression
   - Output to `.Build/SampleTexturesDDS512/Art/*.dds`
3. Check FileHashRegistry to skip unchanged files
4. Create BIG archive: `.Build/000_001_SampleTexturesDDS512.big`

**Phase 3: Build Packs** (for each pack in ModBundlePacks.json)

Example: `ProjectCore` pack
```json
{
  "name": "ProjectCore",
  "itemNames": [
    "SampleINI",
    "SampleLanguages",
    "SampleModels",
    "SampleTexturesDDS512",
    "SampleWindow",
    "Misc"
  ]
}
```

**Processing:**
1. Collect all BIG files from listed items
2. Copy to `.Release/ProjectCore_v1.0/` folder
3. Run `onRelease` script if defined
4. Create installer/archive

**Phase 4: Install** (if --install flag)
1. Copy BIG files to game directory
2. Run `onInstall` script
3. Patch game registry/config

---

## Section 3: File Organization & Flow

### Source Files (GameFilesEdited/)
**Purpose**: User's working directory with edited game files

**File Types:**
- **Art/Models/**: `.blend` (Blender 3D models)
- **Art/**: `.psd`, `.tga`, `.tif` (source textures)
- **Data/INI/**: `.ini` (game configuration)
- **Data/Audio/Sounds/**: `.wav` (audio)
- **Data/generals.str**: String table source
- **Window/**: `.wnd` (UI definitions)

**Key Point**: These are EDITABLE SOURCE FILES, not final game files

### Intermediate Files (.Build/)
**Purpose**: Temporary build artifacts

**Contents:**
- Converted textures (DDS, TGA)
- Compiled models (W3D)
- Processed INI files (comments removed, whitespace normalized)
- Compiled string tables (CSF)
- Individual item folders before BIG creation

**Example:**
```
.Build/
├── SampleTexturesDDS512/
│   └── Art/
│       ├── texture1.dds
│       └── texture2.dds
├── 000_001_SampleTexturesDDS512.big
└── cache.json
```

### Output Files (.Release/)
**Purpose**: Final distributable packages

**Contents:**
```
.Release/
├── ProjectCore_v1.0/
│   ├── 000_001_SampleINI.big
│   ├── 000_002_SampleLanguages.big
│   ├── 000_003_SampleModels.big
│   ├── 000_004_SampleTexturesDDS512.big
│   ├── 000_005_SampleWindow.big
│   ├── 000_006_Misc.big
│   ├── Doc/
│   │   └── ReadMe.txt
│   └── Install.bat
│
└── ProjectExtras_v1.0/
    └── 000_001_SampleAudio.big
```

### Static Files (ReleaseFiles/)
**Purpose**: Files copied as-is to release (no processing)

**Contents:**
- Documentation (ReadMe.txt)
- Installers
- Licenses
- Pre-compiled binaries

---

## Section 4: Key Processing Features

### 1. File Hash Registry (Performance Optimization)
**Location**: `Resources/FileHashRegistry/GeneralsZH-104.zip`

**Purpose**: Skip processing files that haven't changed from vanilla game

**How it works:**
```python
# Python logic (conceptual)
if file_hash_matches_registry(file_path, "GeneralsZH-104.zip"):
    skip_processing()  # File unchanged from vanilla
else:
    process_file()     # File was modified
```

**Example from ModBundleItems.json:**
```json
{
  "sourceList": ["Data/INI/**/*.ini"],
  "registryList": ["Resources/FileHashRegistry/GeneralsZH-104.zip"]
}
```

**Result**: Skips 78,263 unchanged INI files in production builds

### 2. Image Conversion Pipeline

**PSD → DDS Conversion:**
```json
{
  "source": "Art/*.psd",
  "target": "Art/*.dds",
  "params": {
    "rescale": 2.0,        // Downscale by 2x
    "resampling": "BOX",   // Box filter
    "-quality": 255,       // Max quality
    "-mipmode": "None"     // No mipmaps
  }
}
```

**Processing:**
1. Load PSD with Magick.NET (multi-alpha compositing)
2. Composite alpha layers
3. Rescale with ImageSharp
4. Compress to BC3 DDS with BCnEncoder.Net
5. Output to target path

**Supported Formats:**
- Input: PSD, TGA, TIF, BMP, PNG
- Output: DDS, TGA, BMP

### 3. INI File Processing

**Features:**
```json
{
  "params": {
    "forceEOL": "\r\n",              // Normalize line endings
    "deleteComments": ";",            // Remove comments
    "deleteWhitespace": 1,            // Remove extra whitespace
    "sourceEncoding": "ascii",        // Input encoding
    "targetEncoding": "ascii",        // Output encoding
    "excludeMarkersList": [           // Conditional exclusion
      [";begin-exclusion-marker", ";end-exclusion-marker"]
    ]
  }
}
```

**Example INI:**
```ini
; This comment will be removed
Object FactionUnit
  ; begin-exclusion-marker
  DebugOption = Yes  ; This entire section removed
  ; end-exclusion-marker
  Health = 100
End
```

**Output:**
```ini
Object FactionUnit
Health=100
End
```

### 4. String Table Compilation

**STR → CSF Conversion:**
```json
{
  "source": "Data/generals.str",
  "target": "Data/English/generals.csf",
  "params": {
    "language": "English"
  }
}
```

**Multi-language support:**
- Single `.str` source file
- Multiple `.csf` outputs (English, German, Spanish, etc.)
- Language-specific string selection

### 5. 3D Model Export

**Blender → W3D Conversion:**
```json
{
  "source": "Art/Models/AVSentry.blend",
  "target": "Art/W3D/*.w3d",
  "params": {
    "w3dExportHierarchy": true,
    "w3dExportAnimation": true,
    "w3dExportMesh": true,
    "w3dUseExistingSkeleton": false
  }
}
```

**Processing:**
1. Launch Blender headless
2. Load .blend file
3. Export W3D hierarchy, animations, meshes
4. Output multiple W3D files

### 6. BIG Archive Creation

**What goes in a BIG:**
- All processed files for an item
- Maintains game directory structure
- Compressed archive format

**Naming convention:**
```
{itemsPrefix}_{namePrefix}_{itemName}{nameSuffix}.big{bigSuffix}
```

**Example:**
```
000_001_SampleTexturesDDS512.big
└── Art/
    ├── texture1.dds
    └── texture2.dds
```

### 7. Build Cache

**Purpose**: Skip rebuilding unchanged files

**Cache file**: `.Build/cache.json` (or MessagePack in C#)

**Cached data:**
- File paths
- MD5 hashes
- Last modified timestamps
- Processing parameters

**Logic:**
```python
if cache_exists(file) and hash_matches(file) and params_match(file):
    skip_rebuild()
else:
    rebuild_and_update_cache()
```

---

## Section 5: C# Implementation Requirements

### What MUST Match Python Behavior

#### 1. Project Structure
✅ **MUST support:**
- `GameFilesEdited/` as source directory
- `ReleaseFiles/` for static files
- `.Build/` for intermediate files
- `.Release/` for final packages
- Same JSON config files (ModBundleItems.json, etc.)

#### 2. File Processing
✅ **MUST support:**
- PSD multi-alpha compositing (Magick.NET)
- DDS BC3 compression (BCnEncoder.Net)
- Image rescaling with quality filters (ImageSharp)
- INI comment removal and whitespace normalization
- STR → CSF string table compilation
- Blender → W3D model export (external process)
- BIG archive creation

#### 3. Performance Features
✅ **MUST support:**
- FileHashRegistry for skipping unchanged files
- Build cache (MessagePack for 10x speedup)
- Parallel file processing
- Incremental builds

#### 4. Configuration Format
✅ **MUST support:**
- Same JSON schema as Python
- Glob patterns (`**/*.ini`, `Art/*.psd`)
- Source/target path mapping
- Processing parameters
- Hook scripts (onPreBuild, onBuild, etc.)

### What's Different in C#

#### 1. Performance Improvements
🚀 **C# is faster:**
- Span<T> for zero-copy image processing (50x speedup)
- Parallel.ForEachAsync for multi-file ops (8x speedup)
- MessagePack cache serialization (10x speedup)
- Pre-allocated buffers (ArrayPool<T>)

#### 2. Modern Patterns
✨ **C# uses:**
- Primary constructors for DI
- `await using` for async disposal
- `ConfigureAwait(false)` for library code
- Structured logging with ILogger<T>

#### 3. Type Safety
🔒 **C# provides:**
- Compile-time type checking
- Null reference analysis
- Enum validation
- Interface contracts

### What's Missing (To Be Implemented)

#### Critical Missing Features
❌ **Not yet implemented:**
1. **Blender W3D export** - External process execution
2. **STR → CSF compilation** - String table compiler
3. **BIG archive creation** - Archive format writer
4. **Hook script execution** - Python script runner
5. **Install/uninstall logic** - Game integration
6. **Changelog generation** - YAML → Markdown converter

#### Nice-to-Have Features
⚠️ **Lower priority:**
1. GUI progress reporting
2. Detailed error messages with file context
3. Dry-run mode (preview without building)
4. Incremental pack updates
5. Multi-language UI

---

## Section 6: Real-World Example

### Example: Building a Texture Mod

**User's files:**
```
GameFilesEdited/
└── Art/
    ├── tank_texture.psd      (2048x2048, 4 alpha layers)
    └── building_texture.tga  (1024x1024, 1 alpha channel)
```

**ModBundleItems.json:**
```json
{
  "name": "MyTextures",
  "big": true,
  "files": [
    {
      "sourceParent": "GameFilesEdited",
      "sourceTargetList": [
        { "source": "Art/*.psd", "target": "Art/*.dds" },
        { "source": "Art/*.tga", "target": "Art/*.dds" }
      ],
      "params": {
        "rescale": 2.0,
        "resampling": "BOX",
        "-quality": 255
      }
    }
  ]
}
```

**Build process:**
1. **Load PSD**: `tank_texture.psd`
   - Composite 4 alpha layers with Magick.NET
   - Result: 2048x2048 RGBA image

2. **Rescale**: 2048x2048 → 1024x1024
   - Use ImageSharp with Box filter
   - Span<Rgba32> for zero-copy processing

3. **Compress**: RGBA → BC3 DDS
   - Use BCnEncoder.Net
   - Quality 255 (max)

4. **Output**: `.Build/MyTextures/Art/tank_texture.dds`

5. **Repeat for TGA**: `building_texture.tga`
   - Already 1024x1024, rescale to 512x512
   - Convert to DDS

6. **Create BIG**: `000_001_MyTextures.big`
   ```
   000_001_MyTextures.big
   └── Art/
       ├── tank_texture.dds      (1024x1024 BC3)
       └── building_texture.dds  (512x512 BC3)
   ```

7. **Copy to Release**: `.Release/MyMod_v1.0/000_001_MyTextures.big`

**Result**: User gets a single BIG file ready to install

---

## Section 7: Key Insights for C# Implementation

### 1. GameFilesEdited is the Source of Truth
- Users edit files here
- Never modify these files during build
- Always copy/convert to .Build/

### 2. .Build/ is Disposable
- Can be deleted and rebuilt
- Contains intermediate files
- Cache lives here

### 3. .Release/ is the Final Product
- Ready to distribute
- Contains BIG archives
- Includes static files from ReleaseFiles/

### 4. FileHashRegistry is Critical
- Skips 78,263 unchanged files
- Must check BEFORE cache lookup
- Huge performance win

### 5. Parallel Processing is Essential
- 100+ files to process
- 8x speedup with Parallel.ForEachAsync
- Must handle cancellation properly

### 6. Image Processing is the Bottleneck
- PSD compositing is slow (Magick.NET)
- Use Span<T> for ImageSharp (50x speedup)
- Pre-allocate buffers with ArrayPool<T>

### 7. Configuration is Complex
- Nested JSON with glob patterns
- Source/target path mapping
- Per-file processing parameters
- Hook scripts at multiple stages

### 8. Error Handling is Critical
- Invalid PSD files
- Missing dependencies
- Disk space issues
- Blender crashes

---

## Section 8: Testing Strategy

### Test Projects Needed

#### 1. Minimal Test Project
```
Project/
├── GameFilesEdited/
│   └── Data/
│       └── test.ini
├── ModBundleItems.json
└── ModFolders.json
```

**Purpose**: Verify basic build pipeline

#### 2. Texture Test Project
```
Project/
├── GameFilesEdited/
│   └── Art/
│       ├── test.psd
│       ├── test.tga
│       └── test.tif
└── ModBundleItems.json
```

**Purpose**: Test all image formats and conversions

#### 3. Full Sample Project
**Use**: `Z:\ModBuilderSample\Project\`

**Purpose**: Real-world complexity test

### Validation Criteria

✅ **Build succeeds:**
- No errors or warnings
- All files processed
- BIG archives created

✅ **Output matches Python:**
- Same file count
- Same file sizes (±1%)
- Same MD5 hashes for deterministic files

✅ **Performance targets:**
- 15-25% faster than Python (current)
- 20-30% faster than Python (target)

✅ **Cache works:**
- Second build is instant
- Only changed files rebuild

---

## Section 9: Implementation Checklist

### Phase 1: Core Pipeline ✅
- [x] Project loading (JSON configs)
- [x] File discovery (glob patterns)
- [x] Image conversion (PSD/TGA/TIF → DDS/TGA)
- [x] Build cache (MessagePack)
- [x] FileHashRegistry integration
- [x] Parallel processing

### Phase 2: Advanced Features 🔄
- [ ] INI processing (comment removal, whitespace)
- [ ] STR → CSF compilation
- [ ] Blender W3D export
- [ ] BIG archive creation
- [ ] Hook script execution

### Phase 3: Polish 🔄
- [ ] Error handling and reporting
- [ ] Progress reporting
- [ ] Logging and diagnostics
- [ ] Dry-run mode
- [ ] Validation and testing

### Phase 4: Integration 📋
- [ ] GenHub UI integration
- [ ] Project templates
- [ ] Documentation
- [ ] User guides

---

## Conclusion

The Python ModBuilder workflow is:
1. **User edits** files in `GameFilesEdited/`
2. **ModBuilder processes** files (convert, compress, optimize)
3. **ModBuilder packages** into BIG archives
4. **User distributes** `.Release/` packages

The C# implementation must:
- Match Python's file structure and JSON configs
- Support all file formats and conversions
- Maintain or exceed Python's performance
- Provide better error handling and diagnostics

**Current Status**: Core pipeline complete, advanced features in progress.

**Next Steps**: Implement BIG archive creation, INI processing, and hook scripts.
