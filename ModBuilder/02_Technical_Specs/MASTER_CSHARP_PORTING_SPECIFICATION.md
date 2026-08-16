# ModBuilder to GeneralsHub C# - Master Porting Specification

**Version**: 2.3
**Source**: Python 3 (6,145 lines, 26 files)
**Target**: C# for GeneralsHub (Z:\GeneralsHub)
**Game Target**: Command & Conquer Generals Zero Hour (Z:\Workspaces\CnC\CnC_Generals_Zero_Hour)
**Sample Project**: Z:\ModBuilderSample
**Analysis Date**: March 15, 2026

---

## Executive Summary

ModBuilder is a sophisticated build automation system for C&C Generals Zero Hour mods. It processes raw game data files (textures, models, strings, etc.) through format conversions, packages them into .big archives, and manages installation/testing workflows. This document provides a complete technical specification for porting all functionality to C# with zero feature loss.

**Core Capabilities**:
- 7 file format conversion pipelines (PSD/TGA/TIFF/DDS/BLEND/CSF/archives)
- Incremental build system with MD5-based change detection
- Multi-processing support for parallel file operations
- JSON-based configuration with wildcard support
- Event-driven extensibility via Python script callbacks
- CLI and GUI interfaces
- External tool integration with SHA256 verification
- Changelog generation from YAML sources

**Analysis Scope**:
- Complete Python codebase analysis (26 files)
- Real-world sample project analysis (ModBuilderSample)
- Configuration schemas and data models
- Build workflows and user interaction patterns
- External tool dependencies and integration

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Core Build Engine](#2-core-build-engine)
3. [File Conversion System](#3-file-conversion-system)
4. [Data Models & Configuration](#4-data-models--configuration)
5. [User Interfaces (CLI & GUI)](#5-user-interfaces-cli--gui)
6. [External Tool Integration](#6-external-tool-integration)
7. [Changelog System](#7-changelog-system)
8. [Sample Project Analysis](#8-sample-project-analysis)
9. [C# Implementation Roadmap](#9-c-implementation-roadmap)
10. [Technology Stack Recommendations](#10-technology-stack-recommendations)

---

## 1. Architecture Overview

### 1.1 Module Structure

See detailed module breakdown in section documentation.

### 1.2 Build Pipeline Stages

The system uses a **5-stage build index** architecture:

1. **RawBundleItem** - Process source files with conversions
2. **BigBundleItem** - Package into .big archives
3. **RawBundlePack** - Group items into packs
4. **ReleaseBundlePack** - Create distribution archives (.zip)
5. **InstallBundlePack** - Install to game directory

Each stage has associated start/finish events for extensibility.

### 1.3 Key Design Patterns

**Incremental Build System**:
- MD5 hash-based change detection
- Pickle serialization for build state persistence
- File modification time optimization
- FileHashRegistry for external file validation

**Multi-Processing**:
- ProcessPoolExecutor for parallel file operations
- Serializable BuildCopy jobs
- Thread-safe BuildEngine with RLock

**Event-Driven Architecture**:
- 17 event types across build lifecycle
- Python script callbacks with kwargs
- Event types: OnPreBuild, OnBuild, OnPostBuild, OnRelease, OnInstall, OnRun, OnUninstall
- Per-stage events: OnStartBuild*, OnFinishBuild*

**Configuration Layering**:
- Multiple JSON files can be loaded
- Default configs + custom configs
- Later configs override earlier ones
- Wildcard resolution with glob patterns

---

## 2. Core Build Engine

### 2.1 BuildEngine Class (engine.py)

**Location**: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\build\engine.py`

**Purpose**: Central orchestrator for the entire build process

**Key Responsibilities**:
- Manages 5-stage build pipeline
- Coordinates file change detection
- Handles multi-processing
- Executes bundle events
- Manages game installation/uninstall

**Thread Safety**: Uses `threading.RLock` for process pool management

**Main Entry Point**:
```python
def Run(self, setup: BuildSetup) -> bool:
    # Returns True on success, False on failure
```

**Build Execution Flow**:
1. **PreBuild**: Initialize structure, populate things, fire OnPreBuild events
2. **Clean**: Delete build/release directories (if requested)
3. **Build**: Execute 3 stages (RawBundleItem → BigBundleItem → RawBundlePack)
4. **PostBuild**: Fire OnPostBuild events
5. **Release**: Create ReleaseBundlePack (.zip archives)
6. **Install**: Copy to game directory, manage registry settings
7. **Run**: Launch game executable
8. **Uninstall**: Remove installed files, restore settings

### 2.2 Change Detection System

**BuildDiff Architecture**:
- **BuildDiffRegistry**: Tracks file metadata (path, modifiedTime, md5, params)
- **BuildFilePathInfo**: Serializable dataclass for file state
- **Pickle Persistence**: Saves state between builds at `.Build/*.pickle`

**Change Detection Algorithm**:
```
For each file:
  1. Check FileHashRegistry (if enabled) → marks Irrelevant if hash matches
  2. Load old diff registry from previous build
  3. Compare:
     - Not in old registry → Status: Added
     - In old registry:
       - MD5 + params match → Status: Unchanged
       - MD5/params differ → Status: Changed
     - In old but not new → Status: Removed
  4. Optimization: Reuse old MD5 if modification time unchanged
```

**BuildFileStatus Enum**:
- Unknown, Irrelevant, Unchanged, Removed, Missing, Added, Changed

### 2.3 BuildStructure & BuildThing

**BuildStructure**: Container for all 5 build stages
```python
class BuildStructure:
    indexDatas: list[BuildIndexData]  # Array indexed by BuildIndex enum
```

**BuildThing**: Represents a buildable entity (bundle item or pack)
```python
class BuildThing:
    name: str
    files: BuildFilesT  # List of BuildFile objects
    status: BuildFileStatus
    parentThing: BuildThing | None
```

**BuildFile**: Source→Target file mapping
```python
class BuildFile:
    absSource: str
    absTarget: str
    params: ParamsT
    status: BuildFileStatus
```

### 2.4 Multi-Processing Support

**Implementation**: `concurrent.futures.ProcessPoolExecutor`

**Process Flow**:
1. Create process pool with worker count
2. Submit BuildCopy jobs to pool
3. Wait for completion with `concurrent.futures.wait()`
4. Collect results and update BuildFile status

**Serialization**: BuildCopy instances pickled for inter-process communication

**C# Equivalent**: Use `System.Threading.Tasks.Parallel` or `Task.Run()` with `Task.WhenAll()`

---

## 3. File Conversion System

### 3.1 Supported Conversions

| Source Format | Target Formats | Since Version | Notes |
|--------------|----------------|---------------|-------|
| PSD | BMP, DDS, TGA | 1.0 | Compositing support (v2.2+), multi-alpha |
| TGA | BMP, DDS, TGA | 1.0 | RGB→DXT1, RGBA→DXT5 |
| TIFF | BMP, DDS, TGA | 2.2 | Single alpha only, no transparent BG |
| DDS | DDS | 2.3 | Format re-export (e.g., DXT5→DXT1) |
| BLEND | W3D | 1.8 | Blender 3.4.1 with io_mesh_w3d plugin |
| CSF | STR | 1.0 | Game string table to text |
| STR | CSF | 1.0 | Text to game string table |
| Any | BIG | 1.0 | Game archive format |
| Any | ZIP, TAR, TAR.GZ | 1.0 | Standard archives |

### 3.2 BuildCopy Class (copy.py)

**Location**: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\build\copy.py`

**Purpose**: Executes file copy operations with format transformations

**Key Methods**:
- `Copy()`: Main entry point, routes to appropriate converter
- `__GetCopyFunction()`: Determines conversion function based on source/target types
- `CopyWithProcess()`: Wrapper for multi-processing

**BuildFileType Enum** (19 types):
```python
big, blend, bmp, csf, dds, gz, ini, psd, str, tar, tga, tiff, w3d, wnd, zip, Any, Auto
```

**Conversion Routing Priority**:
1. Text file conversions (INI, WND, STR without CSF source)
2. DDS to DDS (with optional processing)
3. Direct copy (source == target, no params)
4. CSF ↔ STR conversions
5. Archive creation (BIG, ZIP, TAR, GZ)
6. Image conversions (PSD/TGA/TIFF → BMP/TGA/DDS)
7. 3D model conversion (BLEND → W3D)
8. Fallback (direct copy/symlink)

### 3.3 Image Conversion Details

#### PSD Processing

**Requirements**:
- Color mode: RGB only
- Minimum channels: 3

**RGB Mode (3 channels)**:
- Uses `psd.composite()` to render image
- Reads pre-computed composite if "Maximize Compatibility" enabled

**RGBA Mode (>3 channels)**:
- Composites with `psd.composite(color=0.0, alpha=1.0)`
- Extracts R, G, B channels separately
- **Multi-Alpha Compositing**: Merges ALL alpha channels (channels 3+)
  - Creates white and black base images
  - Iterates through each alpha channel
  - Uses `PIL.Image.composite(an, black, a)` to blend alphas
- Final output: RGBA image with merged alpha

**C# Implementation Notes**:
- Use ImageSharp or Magick.NET for image processing
- PSD parsing: PsdPlugin for ImageSharp or custom parser
- Alpha compositing requires per-pixel blending logic

#### DDS Compression

**Tool**: Crunch v1.04 from GeneralsTools

**Special Handling**:
- PSD/TIFF → intermediate TGA conversion first
  - Reason: Crunch has issues with PSD alpha channels and resizing

**Automatic DXT Format Selection**:
```python
if __HasAlphaChannel(image):
    format = "DXT5"  # 8-bit alpha
else:
    format = "DXT1"  # No alpha, better compression
```

**Command**: `crunch -file <source> -out <target> -fileformat dds -noprogress [format_params]`

**C# Implementation**:
- Use DirectXTex library (texconv.exe) or BCnEncoder.NET
- Implement alpha detection logic
- Support explicit format override via params

#### Image Resizing

**Parameters**:
- `resize`: Absolute size [width, height] or single value
- `rescale`: Scale factor [x_scale, y_scale] or single value
- `resampling`: Algorithm (NEAREST, BOX, BILINEAR, HAMMING, BICUBIC, LANCZOS)

**RGBA Special Handling**:
- Splits RGBA into separate R, G, B, A channels
- Resizes each channel independently
- Merges back to RGBA
- **Reason**: Prevents color information loss where alpha is black

**C# Implementation**: Use ImageSharp's `Resize()` with per-channel processing for RGBA

### 3.4 String Table Conversion (CSF ↔ STR)

**Tool**: gametextcompiler v1.1 from GeneralsTools

**STR to CSF**:
```bash
gametextcompiler -LOAD_STR <source.str> -SAVE_CSF <target.csf> [-LOAD_STR_LANGUAGES <lang>] [-SWAP_AND_SET_LANGUAGE <lang>]
```

**CSF to STR**:
```bash
gametextcompiler -LOAD_CSF <source.csf> -SAVE_STR <target.str> [-SAVE_STR_LANGUAGES <lang>]
```

**Parameters**:
- `language`: Specifies language code
- `swapAndSetLanguage`: Changes language in CSF file

### 3.5 Archive Creation

**BIG Archives**:
```bash
generalsbigcreator -source <folder> -dest <target.big>
```

**ZIP/TAR/TAR.GZ**:
- Python: `shutil.make_archive(format="zip|tar|gztar")`
- C#: `System.IO.Compression.ZipFile` or `SharpCompress` library

---

## 4. Data Models & Configuration

See separate document: `DATA_MODELS_AND_CONFIGURATION_ANALYSIS.md`

Key configuration files:
- **ModBundleItems.json**: Defines bundle items with file mappings
- **ModBundlePacks.json**: Groups items into distributable packs
- **ModFolders.json**: Output directory configuration
- **WindowsRunner.json**: Game execution settings
- **WindowsTools.json**: External tool definitions

---

## 5. User Interfaces (CLI & GUI)

See separate document: `CSHARP_PORTING_GUIDE_UI_AND_FLOW.md`

**CLI**: 25+ command-line arguments for build automation
**GUI**: Tkinter-based 660x270 window with multi-threading

---

## 6. External Tool Integration

**Tools Required**:
1. **crunch** v1.04 - DDS compression
2. **gametextcompiler** v1.1 - CSF/STR conversion
3. **generalsbigcreator** v1.3 - BIG archive creation
4. **blender** v3.4.1 - W3D model export

**Security**: SHA256 verification before execution

---

## 7. Changelog System

See separate document: `CHANGELOG_AND_DOCUMENTATION_ANALYSIS.md`

**Features**:
- YAML source files
- Markdown generation
- Filter and sort capabilities
- Auto-generated with warning headers

---

## 8. Sample Project Analysis

Detailed analysis in progress by agents. Key findings:

### 8.1 Real-World Configuration Examples

**ModBundleItems.json** demonstrates:
- 10 bundle items with various file types
- Wildcard patterns: `**/*.ini`, `Art/*.psd`, `Data/Audio/Sounds/*`
- Text processing params: `forceEOL`, `deleteComments`, `deleteWhitespace`
- Image processing params: `rescale`, `resampling`
- Multi-language string tables with language params
- Event callbacks: `onPreBuild`, `onBuild`, `onPostBuild`, `onFinishBuildRawBundleItem`
- File hash registry integration
- Exclude markers for conditional content

**ModBundlePacks.json** demonstrates:
- 5 bundle packs grouping items
- Install language settings
- Event callbacks at pack level
- Version suffixes

### 8.2 File Organization Patterns

**GameFilesEdited/** structure mirrors game structure:
- `Art/` - Textures (PSD, TGA, TIFF) and models (BLEND, W3D)
- `Data/` - Game data (INI files, audio, string tables)
- `Window/` - UI files (WND)

**ReleaseFiles/** contains distribution content:
- ReadMe.txt
- Documentation in Doc/ subfolder

---

## 9. C# Implementation Roadmap

### Phase 1: Core Infrastructure
1. Utility classes (file I/O, hashing, path handling)
2. Data models and JSON deserialization
3. Configuration loading system

### Phase 2: Build Engine
1. BuildEngine orchestration
2. BuildDiff change detection
3. BuildStructure and BuildThing models
4. Multi-processing support

### Phase 3: File Conversion
1. Image processing (PSD, TGA, TIFF, DDS)
2. String table conversion (CSF ↔ STR)
3. 3D model conversion (BLEND → W3D)
4. Archive creation (BIG, ZIP)

### Phase 4: External Tools
1. Tool download and installation
2. SHA256 verification
3. Process execution and output capture

### Phase 5: User Interfaces
1. CLI argument parsing
2. GUI implementation (WPF)
3. Progress reporting

### Phase 6: Advanced Features
1. Event system with script callbacks
2. Changelog generation
3. File hash registry
