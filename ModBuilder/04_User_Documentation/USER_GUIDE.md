# ModBuilder User Guide

## Table of Contents

1. [Introduction](#introduction)
2. [Installation and Setup](#installation-and-setup)
3. [Getting Started](#getting-started)
4. [Creating a New Project](#creating-a-new-project)
5. [Project Structure](#project-structure)
6. [Configuring Build Settings](#configuring-build-settings)
7. [Running Builds](#running-builds)
8. [Understanding Build Output](#understanding-build-output)
9. [Using the GUI](#using-the-gui)
10. [Command-Line Usage](#command-line-usage)
11. [Troubleshooting](#troubleshooting)
12. [Performance Tips](#performance-tips)
13. [FAQ](#faq)

---

## Introduction

ModBuilder is a powerful build automation tool for Command & Conquer Generals Zero Hour mods. The C# port brings significant performance improvements, better integration with GenHub, and a modern architecture while maintaining compatibility with existing projects.

### Key Features

- **5-Stage Build Pipeline**: Automated processing from source files to game installation
- **Smart Change Detection**: MD5-based caching system that only rebuilds modified files
- **Format Conversion**: Automatic conversion of images (PSD, TGA, TIFF → DDS, BMP), string tables (STR → CSF), and more
- **Archive Management**: Creates BIG, ZIP, TAR, and TAR.GZ archives
- **Multi-Pack Support**: Build and distribute multiple mod configurations
- **GUI and CLI**: Use the graphical interface or command-line for automation
- **Performance**: Up to 10x faster than the Python version with parallel processing

### System Requirements

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime or later
- GenHub application
- Command & Conquer Generals Zero Hour installation
- External tools (optional): Crunch, GameTextCompiler, Blender

---

## Installation and Setup

### Installing ModBuilder

ModBuilder is integrated into GenHub. To access it:

1. **Install GenHub** from the official release
2. **Launch GenHub** application
3. **Navigate to Tools** → **ModBuilder**

### Setting Up External Tools

ModBuilder can use external tools for advanced conversions:

1. **Crunch** (for DDS texture compression)
   - Download from the official repository
   - Place in `Tools/Crunch/` directory

2. **GameTextCompiler** (for CSF string table compilation)
   - Included with GenHub
   - Automatically configured

3. **Blender** (for 3D model conversion)
   - Install Blender 2.8 or later
   - Configure path in project settings

### Verifying Installation

1. Open ModBuilder in GenHub
2. Check that the tool loads without errors
3. Create a test project to verify functionality

---

## Getting Started

### Quick Start Tutorial

This tutorial will guide you through creating your first mod project.

#### Step 1: Create a New Project

1. Open ModBuilder in GenHub
2. Click **File** → **New Project**
3. Enter project details:
   - **Name**: MyFirstMod
   - **Location**: Choose a directory
   - **Template**: Select "Basic Mod"
4. Click **Create**

#### Step 2: Add Your Mod Files

1. Navigate to the project directory
2. Place your mod files in `GameFilesEdited/Data/`
3. Organize files by type:
   - INI files: `GameFilesEdited/Data/INI/`
   - Textures: `GameFilesEdited/Data/Art/Textures/`
   - Models: `GameFilesEdited/Data/Art/W3D/`

#### Step 3: Configure Bundle Items

Edit `Configs/ModBundleItems.json`:

```json
{
  "items": [
    {
      "name": "MyMod",
      "files": [
        {
          "src": "GameFilesEdited/Data/**/*",
          "dst": "Data/"
        }
      ],
      "isBig": true
    }
  ]
}
```

#### Step 4: Build Your Mod

1. In ModBuilder, click **Build**
2. Wait for the build to complete
3. Check `.Build/` directory for output

#### Step 5: Install and Test

1. Click **Install** to copy files to game directory
2. Click **Run Game** to launch and test
3. Make changes and rebuild as needed

---

## Creating a New Project

### Project Creation Options

ModBuilder offers several project templates:

#### Empty Project
- Minimal configuration
- No default files
- Best for experienced users

#### Basic Mod
- Standard mod structure
- Default configuration files
- Sample bundle items
- Recommended for most users

### Project Creation Process

**Using GUI:**

1. Click **File** → **New Project**
2. Fill in project details:
   - **Project Name**: Unique identifier
   - **Author**: Your name or team
   - **Version**: Starting version (e.g., 1.0.0)
   - **Description**: Brief description
   - **Game Directory**: Path to Generals Zero Hour
3. Select template
4. Click **Create**

**Using API:**

```csharp
var result = await projectConfigService.CreateProjectAsync(
    projectPath: @"C:\Mods\MyMod\MyMod.mbproj",
    projectName: "MyMod",
    gameInstallationId: "generals-zh-001",
    template: ProjectTemplate.BasicMod
);

if (result.Success)
{
    var project = result.Data;
    Console.WriteLine($"Project created: {project.Name}");
}
```

---

## Project Structure

### Directory Layout

```
MyMod/
├── MyMod.mbproj              # Project file (JSON)
├── Configs/                  # Build configuration
│   ├── ModBundleItems.json   # Bundle item definitions
│   ├── ModBundlePacks.json   # Bundle pack definitions
│   └── ModFolders.json       # Directory paths
├── GameFilesEdited/          # Your mod source files
│   └── Data/
│       ├── INI/
│       ├── Art/
│       └── Scripts/
├── .Build/                   # Build output (generated)
│   ├── RawBundleItem/
│   ├── BigBundleItem/
│   └── cache.json
└── .Release/                 # Release packages (generated)
    └── MyMod_v1.0.0.zip
```

### Project File (.mbproj)

The `.mbproj` file is a JSON file containing project metadata:

```json
{
  "name": "MyMod",
  "version": "1.0.0",
  "description": "My awesome mod",
  "author": "ModAuthor",
  "projectDir": "C:/Mods/MyMod",
  "gameDir": "C:/Games/GeneralsZH",
  "gameInstallationId": "generals-zh-001",
  "directories": {
    "configs": "Configs",
    "gameFilesEdited": "GameFilesEdited",
    "build": ".Build",
    "release": ".Release"
  },
  "configFiles": [
    "Configs/ModBundleItems.json",
    "Configs/ModBundlePacks.json",
    "Configs/ModFolders.json"
  ],
  "bundleConfigs": [],
  "bundlePacks": [],
  "createdAt": "2026-03-18T10:00:00Z",
  "modifiedAt": "2026-03-18T10:00:00Z",
  "lastBuild": null,
  "metadata": {}
}
```

---

## Configuring Build Settings

### Bundle Items

Bundle items define what files to process and how to package them.

**Example: ModBundleItems.json**

```json
{
  "items": [
    {
      "name": "CoreMod",
      "namePrefix": "MyMod_",
      "nameSuffix": "_v1",
      "isBig": true,
      "bigSuffix": "",
      "files": [
        {
          "src": "GameFilesEdited/Data/INI/**/*.ini",
          "dst": "Data/INI/",
          "type": "ini"
        },
        {
          "src": "GameFilesEdited/Data/Art/Textures/**/*.tga",
          "dst": "Data/Art/Textures/",
          "type": "dds",
          "params": {
            "format": "DXT5",
            "mipmaps": true
          }
        }
      ]
    }
  ]
}
```

**File Mapping Properties:**

- `src`: Source path (supports wildcards: `*`, `**`)
- `dst`: Destination path in the archive
- `type`: File type for conversion (auto-detected if omitted)
- `params`: Conversion parameters (format-specific)

### Bundle Packs

Bundle packs group multiple bundle items for distribution.

**Example: ModBundlePacks.json**

```json
{
  "packs": [
    {
      "name": "FullMod",
      "namePrefix": "MyMod_",
      "nameSuffix": "_Full",
      "itemNames": ["CoreMod", "ExtraMaps", "CustomModels"],
      "allowBuild": true,
      "allowInstall": true
    },
    {
      "name": "LiteMod",
      "namePrefix": "MyMod_",
      "nameSuffix": "_Lite",
      "itemNames": ["CoreMod"],
      "allowBuild": true,
      "allowInstall": true
    }
  ]
}
```

### Folder Configuration

**Example: ModFolders.json**

```json
{
  "folders": {
    "absBuildDir": "C:/Mods/MyMod/.Build",
    "absReleaseDir": "C:/Mods/MyMod/.Release",
    "absGameDir": "C:/Games/GeneralsZH"
  },
  "runner": {
    "absExe": "C:/Games/GeneralsZH/generals.exe",
    "args": "-quickstart",
    "workingDir": "C:/Games/GeneralsZH"
  },
  "tools": {
    "crunch": {
      "absExe": "C:/Tools/Crunch/crunch.exe",
      "sha256": "...",
      "version": "1.0.0"
    }
  },
  "compressionLevel": "Fastest"
}
```

**Compression Levels:**

- `NoCompression`: Fastest, largest files (debugging only)
- `Fastest`: 20-30% faster, slightly larger (recommended for dev)
- `Optimal`: Best compression, slower (recommended for release)

### File Conversion Parameters

#### Image Conversion (TGA/PSD → DDS)

```json
{
  "format": "DXT5",        // DXT1, DXT3, DXT5
  "mipmaps": true,         // Generate mipmaps
  "resize": [512, 512],    // Resize to dimensions
  "rescale": 0.5           // Scale by factor
}
```

#### String Table Conversion (STR → CSF)

```json
{
  "language": "en",                    // Language code
  "swapAndSetLanguage": "de"          // Swap language
}
```

---

## Running Builds

### Build Pipeline Stages

ModBuilder uses a 5-stage build pipeline:

1. **RawBundleItem** (Stage 1): Process and convert source files
2. **BigBundleItem** (Stage 2): Package files into .big archives
3. **RawBundlePack** (Stage 3): Group bundle items into packs
4. **ReleaseBundlePack** (Stage 4): Create distribution archives (.zip)
5. **InstallBundlePack** (Stage 5): Install to game directory

### Build Actions

#### Clean
Removes all build artifacts from `.Build/` directory.

```csharp
// Clean is automatic before build
```

#### Build
Executes stages 1-2 (process files and create .big archives).

**GUI**: Click **Build** button

**CLI**:
```bash
GenHub.exe modbuilder --build --config Configs/ModBundleItems.json
```

#### Release
Executes stages 1-4 (build + create release packages).

**GUI**: Click **Release** button

**CLI**:
```bash
GenHub.exe modbuilder --release --config Configs/ModBundleItems.json
```

#### Install
Executes stage 5 (copy files to game directory).

**GUI**: Click **Install** button

**CLI**:
```bash
GenHub.exe modbuilder --install FullMod
```

#### Run Game
Launches the game with configured parameters.

**GUI**: Click **Run Game** button

**CLI**:
```bash
GenHub.exe modbuilder --run
```

### Build Options

- **Verbose Logging**: Show detailed build information
- **Multi-Processing**: Enable parallel file processing (faster)
- **Print Config**: Display loaded configuration before building

---

## Understanding Build Output

### Build Log

The build log shows progress through each stage:

```
[INFO] Loading configuration...
[INFO] Loaded 3 bundle items, 2 bundle packs
[INFO] Starting build pipeline...
[INFO] Stage 1: Processing RawBundleItem...
[INFO]   Processing CoreMod...
[INFO]     Converting texture.tga -> texture.dds (DXT5)
[INFO]     Copying weapon.ini -> weapon.ini
[INFO]   Processed 45 files (2 changed, 43 unchanged)
[INFO] Stage 2: Creating BigBundleItem...
[INFO]   Creating MyMod_CoreMod.big...
[INFO]   Archive created: 12.5 MB
[INFO] Build completed in 3.2 seconds
[SUCCESS] Build successful!
```

### Build Statistics

After each build, ModBuilder displays:

- **Files Processed**: Total files handled
- **Files Changed**: Files that were modified
- **Files Unchanged**: Files skipped (cached)
- **Files Failed**: Files with errors
- **Elapsed Time**: Total build duration

### Build Cache

ModBuilder maintains a cache file (`.Build/cache.json`) to track file changes:

```json
{
  "files": {
    "GameFilesEdited/Data/INI/weapon.ini": {
      "md5": "a1b2c3d4e5f6...",
      "modifiedTime": 1710756000.0,
      "params": {}
    }
  }
}
```

The cache enables incremental builds by skipping unchanged files.

### Output Directories

**`.Build/RawBundleItem/`**: Processed source files
```
.Build/RawBundleItem/CoreMod/
├── Data/
│   ├── INI/
│   │   └── weapon.ini
│   └── Art/
│       └── Textures/
│           └── texture.dds
```

**`.Build/BigBundleItem/`**: .big archives
```
.Build/BigBundleItem/
└── MyMod_CoreMod.big
```

**`.Release/`**: Distribution packages
```
.Release/
└── MyMod_FullMod_v1.0.0.zip
```

---

## Using the GUI

### Main Window

The ModBuilder GUI provides an intuitive interface for building mods.

#### Layout

```
┌─────────────────────────────────────────────────────────┐
│  File  Edit  Build  Help                                │
├─────────────────────────────────────────────────────────┤
│  Project: MyMod v1.0.0                                  │
├─────────────────────────────────────────────────────────┤
│  Bundle Packs          │  Actions                       │
│  ☑ FullMod            │  [Clean]  [Build]  [Release]  │
│  ☐ LiteMod            │  [Install]  [Run Game]         │
│                        │                                 │
│  Options               │  Build Output                  │
│  ☐ Verbose Logging    │  ┌─────────────────────────┐  │
│  ☑ Multi-Processing   │  │ [INFO] Loading...       │  │
│  ☐ Print Config       │  │ [INFO] Processing...    │  │
│                        │  │ [SUCCESS] Complete!     │  │
│                        │  └─────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

### Menu Bar

**File Menu:**
- New Project
- Open Project
- Recent Projects
- Save Project
- Close Project
- Exit

**Edit Menu:**
- Project Settings
- Bundle Items
- Bundle Packs
- Folder Configuration

**Build Menu:**
- Clean
- Build
- Release
- Install
- Run Game
- Abort Build

**Help Menu:**
- User Guide
- API Reference
- About

### Bundle Pack Selection

- Check the packs you want to build
- Multiple packs can be selected
- Only checked packs will be processed

### Build Actions

**Clean**: Removes build artifacts
**Build**: Builds selected packs (stages 1-2)
**Release**: Creates release packages (stages 1-4)
**Install**: Installs to game directory (stage 5)
**Run Game**: Launches the game

### Build Output Panel

Shows real-time build progress:
- Current stage
- Files being processed
- Conversion operations
- Errors and warnings
- Build statistics

### Progress Indication

- Progress bar shows overall completion
- Current file being processed
- Estimated time remaining
- Files processed / total files

---

## Command-Line Usage

### Basic Syntax

```bash
GenHub.exe modbuilder [options] [actions]
```

### Configuration Options

```bash
--config <path>              # Load configuration file
--config-list <path> ...     # Load multiple configuration files
--project <path>             # Load project file (.mbproj)
```

### Build Actions

```bash
--clean                      # Clean build artifacts
--build                      # Build mod (stages 1-2)
--release                    # Build release (stages 1-4)
--install <pack>             # Install bundle pack
--install-list <packs>       # Install multiple packs
--run                        # Run game
--uninstall                  # Uninstall mod
```

### Build Options

```bash
--verbose-logging            # Enable verbose output
--multi-processing           # Enable parallel processing
--print-config               # Print configuration
--debug                      # Enable debug mode
```

### Examples

**Build a mod:**
```bash
GenHub.exe modbuilder --project MyMod.mbproj --build
```

**Build and install:**
```bash
GenHub.exe modbuilder --project MyMod.mbproj --build --install FullMod
```

**Create release package:**
```bash
GenHub.exe modbuilder --project MyMod.mbproj --release
```

**Build specific pack:**
```bash
GenHub.exe modbuilder --project MyMod.mbproj --build-pack LiteMod
```

**Full automation:**
```bash
GenHub.exe modbuilder --project MyMod.mbproj --clean --build --release --install FullMod --run
```

### Batch Scripting

Create a `build.bat` file:

```batch
@echo off
echo Building MyMod...
GenHub.exe modbuilder --project MyMod.mbproj --build --verbose-logging
if %ERRORLEVEL% EQU 0 (
    echo Build successful!
) else (
    echo Build failed!
    exit /b 1
)
```

---

## Troubleshooting

### Common Issues

#### Build Fails with "File Not Found"

**Problem**: Source files cannot be located

**Solution**:
1. Check file paths in bundle item configuration
2. Verify files exist in `GameFilesEdited/` directory
3. Check for typos in file names
4. Ensure wildcards are correct (`**/*` for recursive)

#### Texture Conversion Fails

**Problem**: DDS conversion errors

**Solution**:
1. Verify Crunch tool is installed and configured
2. Check source image format (TGA, PSD, TIFF supported)
3. Verify image dimensions are power of 2 (256, 512, 1024, etc.)
4. Check conversion parameters (DXT format)

#### Build is Slow

**Problem**: Build takes too long

**Solution**:
1. Enable multi-processing option
2. Use `Fastest` compression level for dev builds
3. Clean build cache if corrupted: delete `.Build/cache.json`
4. Check for large files that don't need conversion

#### Game Doesn't Load Mod

**Problem**: Mod files not appearing in game

**Solution**:
1. Verify install completed successfully
2. Check game directory path is correct
3. Ensure .big files are in game directory
4. Check file names match game expectations
5. Verify mod doesn't conflict with other mods

#### "Access Denied" Errors

**Problem**: Cannot write to directories

**Solution**:
1. Run GenHub as administrator
2. Check directory permissions
3. Close game before building
4. Verify antivirus isn't blocking files

### Error Messages

#### "Configuration file not found"
- Check path to configuration files
- Verify files exist and are valid JSON

#### "Invalid bundle item configuration"
- Validate JSON syntax
- Check required fields are present
- Verify file paths are correct

#### "External tool execution failed"
- Check tool is installed
- Verify tool path in configuration
- Check tool version compatibility

#### "MD5 hash mismatch"
- Build cache may be corrupted
- Delete `.Build/cache.json` and rebuild

### Debug Mode

Enable debug mode for detailed error information:

```bash
GenHub.exe modbuilder --project MyMod.mbproj --build --debug
```

Debug mode provides:
- Full stack traces
- Detailed file operations
- Configuration dump
- Cache state information

### Log Files

ModBuilder creates log files in:
```
%APPDATA%/GenHub/Logs/ModBuilder/
```

Check logs for:
- Build history
- Error details
- Performance metrics

---

## Performance Tips

### Optimization Strategies

#### 1. Use Incremental Builds

ModBuilder's cache system only rebuilds changed files. To maximize benefit:

- Don't clean unless necessary
- Keep cache file (`.Build/cache.json`)
- Avoid modifying timestamps unnecessarily

**Performance Gain**: 10-100x faster for small changes

#### 2. Enable Multi-Processing

Parallel processing significantly speeds up builds:

```json
{
  "multiProcessing": true
}
```

**Performance Gain**: 2-4x faster on multi-core systems

#### 3. Optimize Compression

Use appropriate compression levels:

- **Development**: `Fastest` (20-30% faster)
- **Release**: `Optimal` (best compression)

```json
{
  "compressionLevel": "Fastest"
}
```

**Performance Gain**: 20-30% faster builds

#### 4. Minimize Conversions

Only convert files that need it:

- Use pre-converted DDS textures when possible
- Skip conversion for files that don't change
- Use `type: "auto"` to skip unnecessary conversions

#### 5. Organize Files Efficiently

- Group similar files together
- Use wildcards effectively
- Avoid deep directory nesting

#### 6. Use SSD Storage

Store project on SSD for faster I/O:

**Performance Gain**: 2-3x faster file operations

### Performance Benchmarks

Typical build times (1000 files, mixed types):

| Configuration | Time | Notes |
|--------------|------|-------|
| First build (no cache) | 45s | All files processed |
| Incremental (10 changes) | 5s | Only changed files |
| Multi-processing ON | 18s | 2.5x faster |
| Fastest compression | 35s | 22% faster |
| Optimal compression | 45s | Smallest archives |

### Memory Usage

ModBuilder is memory-efficient:

- Small projects (<1000 files): ~100 MB
- Medium projects (1000-5000 files): ~200 MB
- Large projects (>5000 files): ~500 MB

---

## FAQ

### General Questions

**Q: What's the difference between Build and Release?**

A: Build creates .big archives in `.Build/` directory. Release additionally creates distribution packages (.zip) in `.Release/` directory.

**Q: Can I use ModBuilder with existing Python projects?**

A: Yes! The C# version is fully compatible with Python ModBuilder projects. See the Migration Guide for details.

**Q: Do I need to install external tools?**

A: Only if you need specific conversions:
- Crunch: For DDS texture compression
- Blender: For 3D model conversion
- GameTextCompiler: Included with GenHub

**Q: Can I automate builds with CI/CD?**

A: Yes! Use the command-line interface in your build scripts.

### Configuration Questions

**Q: How do I add new files to my mod?**

A: Place files in `GameFilesEdited/` directory and update bundle item configuration to include them.

**Q: Can I have multiple bundle packs?**

A: Yes! Define multiple packs in `ModBundlePacks.json` to create different mod configurations.

**Q: How do I change the output directory?**

A: Edit `ModFolders.json` and update `absBuildDir` and `absReleaseDir` paths.

**Q: Can I use custom file extensions?**

A: Yes! ModBuilder supports any file type. Use `type: "auto"` for automatic handling.

### Build Questions

**Q: Why is my first build slow?**

A: First builds process all files and create the cache. Subsequent builds are much faster.

**Q: How do I force a full rebuild?**

A: Click Clean before building, or delete `.Build/cache.json`.

**Q: Can I build multiple projects simultaneously?**

A: Yes, each project has its own build directory and cache.

**Q: What happens if I abort a build?**

A: The build stops immediately. Partial files are cleaned up. Cache remains valid.

### Installation Questions

**Q: Where are files installed?**

A: Files are copied to the game directory specified in `ModFolders.json`.

**Q: Can I install multiple mods?**

A: Yes, but be careful of file conflicts. Use unique file names.

**Q: How do I uninstall a mod?**

A: Click Uninstall or use `--uninstall` command. This removes mod files from game directory.

**Q: Can I test without installing?**

A: Yes! Use the .big files directly from `.Build/BigBundleItem/` directory.

### Troubleshooting Questions

**Q: Build fails with "Access Denied"**

A: Run GenHub as administrator or check directory permissions.

**Q: Textures look wrong in game**

A: Check DDS format (DXT1 for no alpha, DXT5 for alpha). Verify mipmaps are generated.

**Q: Game crashes after installing mod**

A: Check for INI syntax errors. Verify file paths are correct. Test with minimal mod first.

**Q: Build cache seems corrupted**

A: Delete `.Build/cache.json` and rebuild. Cache will be regenerated.

### Advanced Questions

**Q: Can I extend ModBuilder with custom conversions?**

A: Yes! Implement `IFileConversionService` interface and register with dependency injection.

**Q: How do I integrate with version control?**

A: Commit project files and `GameFilesEdited/`. Ignore `.Build/` and `.Release/` directories.

**Q: Can I use ModBuilder as a library?**

A: Yes! Reference `GenHub.Core` and use the service interfaces. See API Reference.

**Q: How do I report bugs?**

A: Open an issue on the GenHub GitHub repository with:
- ModBuilder version
- Project configuration
- Build log
- Steps to reproduce

---

## Additional Resources

- **API Reference**: Detailed documentation of all services and models
- **Migration Guide**: Porting from Python to C# ModBuilder
- **GitHub Repository**: https://github.com/YourOrg/GenHub
- **Community Discord**: Join for support and discussions
- **Video Tutorials**: Step-by-step guides on YouTube

---

**Version**: 1.0.0
**Last Updated**: March 18, 2026
**Author**: enowX Labs
