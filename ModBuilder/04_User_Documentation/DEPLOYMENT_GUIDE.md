# ModBuilder Deployment Guide

**Version**: 1.0.0
**Date**: March 19, 2026
**Status**: Production Ready

---

## Overview

This guide covers installation, configuration, and deployment of ModBuilder for C&C Generals Zero Hour mod development.

---

## System Requirements

### Minimum Requirements
- **OS**: Windows 10 (64-bit) or later
- **CPU**: Dual-core processor (2.0 GHz)
- **RAM**: 4GB
- **Disk**: 500MB free space
- **.NET**: .NET 8.0 Runtime

### Recommended Requirements
- **OS**: Windows 11 (64-bit)
- **CPU**: Quad-core processor (3.0 GHz or higher)
- **RAM**: 8GB or more
- **Disk**: 2GB free space (for projects and cache)
- **.NET**: .NET 8.0 Runtime
- **GPU**: Not required (CPU-based processing)

### External Tools (Optional)
- **Blender 3.4.1** - For W3D model export (optional)
- **Photoshop** - For PSD file creation (optional)

---

## Installation

### Step 1: Install .NET 8.0 Runtime

1. Download .NET 8.0 Runtime from: https://dotnet.microsoft.com/download/dotnet/8.0
2. Run the installer
3. Verify installation:
   ```bash
   dotnet --version
   ```
   Should output: `8.0.x` or higher

### Step 2: Install GeneralsHub

1. Download GeneralsHub from: [Release URL]
2. Extract to desired location (e.g., `C:\Program Files\GeneralsHub`)
3. Run `GenHub.exe`

### Step 3: Verify ModBuilder Tool

1. Launch GeneralsHub
2. Navigate to **Tools** menu
3. Verify **ModBuilder** is listed
4. Click **ModBuilder** to open the tool

---

## First-Time Setup

### Configure Game Installation

1. Open ModBuilder tool
2. Click **Settings** → **Game Installation**
3. Browse to your C&C Generals Zero Hour installation directory
   - Default: `C:\Program Files (x86)\EA Games\Command & Conquer Generals Zero Hour`
4. Click **Save**

### Configure External Tools (Optional)

ModBuilder includes embedded tools for most operations. External tools are optional:

#### Blender (for W3D export)
1. Download Blender 3.4.1 from: https://www.blender.org/download/
2. Install to default location
3. In ModBuilder: **Settings** → **External Tools** → **Blender Path**
4. Browse to `blender.exe`
5. Click **Save**

### Verify Installation

1. Click **Help** → **Verify Installation**
2. ModBuilder will check:
   - .NET Runtime version
   - Game installation path
   - External tools (if configured)
   - Write permissions
3. Resolve any issues reported

---

## Creating Your First Project

### Step 1: Create New Project

1. Open ModBuilder
2. Click **File** → **New Project**
3. Enter project details:
   - **Name**: MyFirstMod
   - **Location**: `C:\Users\[YourName]\Documents\ModBuilder\Projects`
   - **Game**: C&C Generals Zero Hour
4. Click **Create**

### Step 2: Project Structure

ModBuilder creates the following structure:

```
MyFirstMod/
├── MyFirstMod.mbproj          # Project file
├── Configs/                   # Build configuration
│   ├── ModBundleItems.json    # File mappings
│   ├── ModBundlePacks.json    # Bundle packs
│   └── ModFolders.json        # Folder structure
├── GameFilesEdited/           # Your mod files
│   ├── Data/                  # Game data files
│   ├── Art/                   # Textures and models
│   └── Audio/                 # Sound files
├── .Build/                    # Build cache (auto-generated)
└── .Release/                  # Output archives (auto-generated)
```

### Step 3: Add Mod Files

1. Copy your mod files to `GameFilesEdited/`
2. Organize by type:
   - `.ini` files → `GameFilesEdited/Data/`
   - `.dds` files → `GameFilesEdited/Art/Textures/`
   - `.w3d` files → `GameFilesEdited/Art/Models/`
   - `.mp3` files → `GameFilesEdited/Audio/`

### Step 4: Configure Build

1. Open `Configs/ModBundleItems.json`
2. Add file mappings:
   ```json
   {
     "items": [
       {
         "source": "GameFilesEdited/Data/*.ini",
         "target": "Data/INI/",
         "conversion": "copy"
       },
       {
         "source": "GameFilesEdited/Art/Textures/*.psd",
         "target": "Art/Textures/",
         "conversion": "psd_to_dds",
         "params": {
           "compression": "dxt5",
           "mipmaps": true
         }
       }
     ]
   }
   ```

### Step 5: Build and Test

1. Select bundle packs to build
2. Check build options:
   - ☑ **Build** - Compile mod files
   - ☑ **Install** - Install to game directory
   - ☑ **Run Game** - Launch game for testing
3. Click **Execute**
4. Monitor build output
5. Test mod in-game

---

## Configuration

### Build Options

#### Clean Build
- Deletes all cached files
- Forces complete rebuild
- Use when: Major changes or troubleshooting

#### Incremental Build (Default)
- Only rebuilds changed files
- Uses MD5 hash comparison
- Use when: Regular development

#### Release Build
- Creates distribution archives
- Optimizes file sizes
- Generates checksums
- Use when: Preparing for release

### Performance Settings

#### Multi-Processing
- **Enabled** (default): Uses all CPU cores
- **Disabled**: Single-threaded processing
- Recommendation: Keep enabled for faster builds

#### Verbose Logging
- **Enabled**: Detailed build output
- **Disabled** (default): Summary output only
- Use when: Debugging build issues

#### Compression Levels
- **Store** (0): No compression, fastest
- **Fast** (1-3): Light compression, fast
- **Normal** (6): Balanced (default)
- **Best** (9): Maximum compression, slowest

### File Hash Registry

Automatically skips unchanged game files:
- **Enabled** (default): 20-30% faster builds
- **Disabled**: Processes all files
- Recommendation: Keep enabled

---

## Advanced Configuration

### Custom Bundle Packs

Create custom bundle packs in `Configs/ModBundlePacks.json`:

```json
{
  "packs": [
    {
      "name": "CoreMod",
      "description": "Core mod files",
      "items": ["Data", "Scripts"],
      "enabled": true
    },
    {
      "name": "HighResTextures",
      "description": "Optional HD textures",
      "items": ["Textures_HD"],
      "enabled": false
    }
  ]
}
```

### Event Hooks

Execute scripts at build stages:

```json
{
  "events": {
    "onPreBuild": "scripts/pre_build.ps1",
    "onPostBuild": "scripts/post_build.ps1",
    "onInstall": "scripts/install.ps1"
  }
}
```

### External Tool Configuration

Configure external tools in `Configs/WindowsTools.json`:

```json
{
  "tools": [
    {
      "name": "crunch",
      "version": "1.04",
      "executable": "crunch.exe",
      "sha256": "abc123...",
      "downloadUrl": "https://..."
    }
  ]
}
```

---

## Deployment Scenarios

### Scenario 1: Development Workstation

**Goal**: Fast iteration and testing

**Configuration**:
- Multi-processing: Enabled
- Compression: Fast (level 1)
- File hash registry: Enabled
- Incremental builds: Enabled

**Workflow**:
1. Make changes to mod files
2. Build → Install → Run Game
3. Test in-game
4. Repeat

### Scenario 2: CI/CD Pipeline

**Goal**: Automated builds and releases

**Configuration**:
- CLI mode: Enabled
- Verbose logging: Enabled
- Compression: Best (level 9)
- Clean builds: Enabled

**Command**:
```bash
GenHub.exe modbuilder build --project MyMod.mbproj --clean --release --verbose
```

### Scenario 3: Release Distribution

**Goal**: Create distribution packages

**Configuration**:
- Release build: Enabled
- Compression: Best (level 9)
- Archive formats: ZIP, TAR.GZ
- Checksums: Enabled

**Workflow**:
1. Clean build
2. Release build
3. Generate checksums
4. Upload to distribution platform

---

## Troubleshooting

### Build Fails with "File Not Found"

**Cause**: Source file path incorrect

**Solution**:
1. Verify file exists in `GameFilesEdited/`
2. Check path in `ModBundleItems.json`
3. Use forward slashes: `Art/Textures/file.psd`

### Build is Slow

**Cause**: Multi-processing disabled or large files

**Solution**:
1. Enable multi-processing in settings
2. Enable file hash registry
3. Use incremental builds
4. Check disk I/O performance

### Game Doesn't Load Mod

**Cause**: Installation path incorrect

**Solution**:
1. Verify game installation path in settings
2. Check install directory permissions
3. Verify mod files installed correctly
4. Check game logs for errors

### Out of Memory Error

**Cause**: Large project or insufficient RAM

**Solution**:
1. Close other applications
2. Enable streaming for large files
3. Increase system page file
4. Process files in smaller batches

### External Tool Fails

**Cause**: Tool not found or incorrect version

**Solution**:
1. Verify tool path in settings
2. Check tool version compatibility
3. Re-download tool if corrupted
4. Verify SHA256 checksum

---

## Performance Optimization

### Build Performance

**Optimize for Speed**:
- Enable multi-processing
- Enable file hash registry
- Use incremental builds
- Use fast compression (level 1-3)
- Enable build structure caching
- Enable file existence caching

**Optimize for Size**:
- Use best compression (level 9)
- Enable DDS texture compression
- Remove unnecessary files
- Use release builds

### Disk I/O Optimization

- Use SSD for project files
- Use SSD for build cache
- Exclude build directories from antivirus
- Use 64KB buffer size (default)

### Memory Optimization

- Enable streaming for large files (>100MB)
- Use ArrayPool for buffers
- Close unused applications
- Increase system page file if needed

---

## Maintenance

### Regular Tasks

**Daily** (during active development):
- Incremental builds
- Test in-game
- Commit changes to version control

**Weekly**:
- Clean build to verify integrity
- Review build logs for warnings
- Update external tools if needed

**Monthly**:
- Archive old projects
- Clean build cache
- Update ModBuilder to latest version

### Backup Strategy

**Critical Files** (backup regularly):
- `*.mbproj` - Project files
- `Configs/*.json` - Configuration
- `GameFilesEdited/` - Your mod files

**Generated Files** (can be rebuilt):
- `.Build/` - Build cache
- `.Release/` - Output archives

**Recommended Backup**:
- Use Git for version control
- Backup to cloud storage
- Keep local backups on external drive

---

## Upgrading

### Upgrading ModBuilder

1. Backup current projects
2. Download new version
3. Install over existing installation
4. Verify projects load correctly
5. Run test build

### Migrating from Python ModBuilder

1. Install C# ModBuilder
2. Open existing project directory
3. ModBuilder auto-detects Python configs
4. Click **Migrate Project**
5. Verify configuration
6. Run test build

---

## Support

### Documentation
- **User Guide**: `USER_GUIDE.md`
- **Project Format**: `MBPROJ_FORMAT.md`
- **Troubleshooting**: `TROUBLESHOOTING_GUIDE.md`
- **Performance**: `PERFORMANCE_VALIDATION_REPORT.md`

### Community
- **Discord**: [Community Server]
- **Forums**: [Forum URL]
- **GitHub**: [Repository URL]

### Reporting Issues
1. Check troubleshooting guide
2. Search existing issues
3. Collect build logs
4. Submit issue with:
   - ModBuilder version
   - .NET version
   - OS version
   - Build logs
   - Steps to reproduce

---

## Appendix

### File Format Reference

| Extension | Description | Conversion |
|-----------|-------------|------------|
| `.psd` | Photoshop Document | → DDS, TGA, BMP |
| `.tga` | Targa Image | → DDS, BMP |
| `.tiff` | Tagged Image File | → DDS, TGA |
| `.dds` | DirectDraw Surface | → DDS (re-export) |
| `.str` | String Table | → CSF |
| `.csf` | Compiled String File | → STR |
| `.blend` | Blender File | → W3D |
| `.big` | C&C Archive | Created from files |
| `.zip` | ZIP Archive | Created from files |
| `.tar` | TAR Archive | Created from files |
| `.tar.gz` | Compressed TAR | Created from files |

### Command-Line Reference

```bash
# Build project
GenHub.exe modbuilder build --project MyMod.mbproj

# Clean build
GenHub.exe modbuilder build --project MyMod.mbproj --clean

# Release build
GenHub.exe modbuilder build --project MyMod.mbproj --release

# Install and run
GenHub.exe modbuilder build --project MyMod.mbproj --install --run

# Verbose output
GenHub.exe modbuilder build --project MyMod.mbproj --verbose

# Help
GenHub.exe modbuilder --help
```

### Performance Benchmarks

| Project Size | Files | Size | Build Time | vs Python |
|--------------|-------|------|------------|-----------|
| Small | 10 | 5MB | 1.9s | 24% faster |
| Medium | 100 | 50MB | 9.5s | 23% faster |
| Large | 1000 | 500MB | 6.3min | 23% faster |
| Production | 5405 | 892MB | 23min | 23% faster |

---

**Document Version**: 1.0.0
**Last Updated**: March 19, 2026
**Status**: Production Ready
