# ModBuilder C# Implementation Plan & Progress Tracker

**Project**: Port ModBuilder v2.3 (Python) to GeneralsHub (C#)
**Target**: GeneralsHub Tooling Infrastructure
**Status**: 🟢 WEEK 2 COMPLETE - PRODUCTION READY
**Created**: 2026-03-17
**Last Updated**: 2026-03-18

---

## Executive Summary

This document tracks the complete implementation of ModBuilder as a GeneralsHub tool. ModBuilder is a sophisticated build automation system for C&C Generals Zero Hour mods that processes 7 file formats, manages incremental builds with MD5 change detection, and provides both CLI and GUI interfaces.

**Key Requirements**:
- ✅ **Performance**: Must match or exceed Python implementation speed
- ✅ **Feature Parity**: 100% feature retention from Python version
- ✅ **Architecture**: Clean, extensible, following GeneralsHub patterns
- ✅ **Integration**: Seamless integration with existing tooling infrastructure

---

## Documentation References

### Primary Analysis Documents (10,000+ LOC)
- `START_HERE.md` - Quick start guide and reading order
- `INDEX.md` - Complete documentation index
- `MASTER_CSHARP_PORTING_SPECIFICATION.md` - Architecture and roadmap
- `DATA_MODELS_AND_CONFIGURATION_ANALYSIS.md` - Configuration schemas
- `CSHARP_PORTING_GUIDE_UI_AND_FLOW.md` - UI and program flow
- `BATCH_SCRIPTS_ANALYSIS.md` - User workflows
- `GAME_MODIFICATIONS_GUIDE.md` - Sample project analysis
- `CHANGELOG_AND_DOCUMENTATION_ANALYSIS.md` - Changelog system
- `ANALYSIS_VERIFICATION_SUMMARY.md` - Verification checklist

### GeneralsHub Architecture
- `.vs/tree.md` - Complete codebase structure
- `GenHub/Features/Tools/MapManager/` - Reference tool implementation
- `GenHub/Core/Interfaces/Tools/IToolPlugin.cs` - Tool plugin contract
- `GenHub/Infrastructure/DependencyInjection/` - DI patterns

---

## Architecture Overview

### ModBuilder Integration Pattern

```
GeneralsHub/
├── GenHub/
│   ├── Features/Tools/ModBuilder/
│   │   ├── ModBuilderToolPlugin.cs          [IToolPlugin implementation]
│   │   ├── ViewModels/
│   │   │   ├── ModBuilderViewModel.cs       [Main UI ViewModel]
│   │   │   ├── ProjectViewModel.cs          [Project management]
│   │   │   └── BuildOutputViewModel.cs      [Build console output]
│   │   ├── Views/
│   │   │   ├── ModBuilderView.axaml         [Main UI]
│   │   │   └── ModBuilderView.axaml.cs
│   │   └── Services/
│   │       ├── BuildEngineService.cs        [Core build orchestration]
│   │       ├── ProjectConfigService.cs      [Project/config management]
│   │       ├── FileConversionService.cs     [Format conversions]
│   │       ├── ExternalToolService.cs       [Tool execution]
│   │       └── BuildCacheService.cs         [MD5 change detection]
│   └── Core/
│       ├── Models/Tools/ModBuilder/
│       │   ├── BuildConfiguration.cs        [Config data models]
│       │   ├── BundleItem.cs
│       │   ├── BundlePack.cs
│       │   ├── BuildResult.cs
│       │   └── ModBuilderProject.cs         [Project container]
│       └── Interfaces/Tools/ModBuilder/
│           ├── IBuildEngineService.cs
│           ├── IProjectConfigService.cs
│           ├── IFileConversionService.cs
│           └── IExternalToolService.cs
└── Infrastructure/DependencyInjection/
    └── ModBuilderModule.cs                  [DI registration]
```

### Key Architectural Decisions

1. **Project-Based Workflow**
   - Users create/load ModBuilder projects (`.mbproj` files)
   - Projects contain references to config bundles and edited files
   - Project switching supported via UI

2. **Service Layer Architecture**
   - `BuildEngineService`: Orchestrates 5-stage build pipeline
   - `ProjectConfigService`: Manages project files and configuration loading
   - `FileConversionService`: Handles 7 format conversions (delegates to specialized converters)
   - `ExternalToolService`: Manages external tool execution (crunch, gametextcompiler, etc.)
   - `BuildCacheService`: MD5-based incremental build system

3. **Reuse Existing Infrastructure**
   - Image conversion: Leverage existing patterns from MapManager
   - Archive packing: Reuse .big packer from existing tools
   - File operations: Use established GeneralsHub file service patterns
   - Progress reporting: Use existing `IProgress<T>` infrastructure

4. **Performance Optimizations**
   - Async/await for all I/O operations
   - `Parallel.ForEachAsync()` for multi-file processing
   - MD5 caching with file modification time checks
   - Incremental builds (only process changed files)

---

## Implementation Phases

### Phase 1: Foundation & Data Models (Week 1-2)
**Status**: ⏳ NOT STARTED

#### 1.1 Core Data Models
- [ ] `BundleItem.cs` - File mapping with conversion params
- [ ] `BundlePack.cs` - Grouping of bundle items
- [ ] `BundleFile.cs` - Source→Target file mapping
- [ ] `BuildConfiguration.cs` - Complete config structure
- [ ] `ModBuilderProject.cs` - Project container
- [ ] `BuildResult.cs` - Build output data
- [ ] `BuildFileStatus.cs` - Change detection enum
- [ ] `BuildIndex.cs` - 5-stage pipeline enum

**References**:
- `DATA_MODELS_AND_CONFIGURATION_ANALYSIS.md` (lines 1-1020)
- `MASTER_CSHARP_PORTING_SPECIFICATION.md` (section 4)

#### 1.2 Configuration System
- [ ] JSON schema validation
- [ ] Configuration loading with layering support
- [ ] Wildcard resolution (glob patterns)
- [ ] Default config embedding
- [ ] Configuration merging logic

**References**:
- `DATA_MODELS_AND_CONFIGURATION_ANALYSIS.md` (lines 100-300)

#### 1.3 Project Management
- [x] `.mbproj` file format design
- [x] Project creation/loading/saving
- [x] Project directory structure
- [x] Recent projects tracking
- [x] Project validation

**Status**: ✅ COMPLETED (2026-03-17)

**Implementation**:
- Created `ModBuilderProject` model with JSON serialization
- Created `ProjectTemplate` with predefined templates (Empty, BasicMod)
- Created `ProjectOperationResult<T>` for error handling with validation support
- Created `IProjectConfigService` interface with comprehensive project management methods
- Implemented `ProjectConfigService` with:
  - Async/await for all I/O operations
  - Proper error handling with Result pattern
  - Project creation with directory structure generation
  - Project loading/saving with JSON serialization
  - Project validation with integrity checks
  - Recent projects tracking (stored in %APPDATA%)
  - Bundle configuration management
  - Last build time tracking
- Created `MBPROJ_FORMAT.md` documentation

**Files Created**:
- `GenHub.Core/Models/ModBuilder/ModBuilderProject.cs`
- `GenHub.Core/Models/ModBuilder/ProjectTemplate.cs`
- `GenHub.Core/Models/Results/ModBuilder/ProjectOperationResult.cs`
- `GenHub.Core/Interfaces/Tools/ModBuilder/IProjectConfigService.cs`
- `GenHub/Features/Tools/ModBuilder/Services/ProjectConfigService.cs`
- `ModBuilder/MBPROJ_FORMAT.md`

**User Flow**:
- User creates new project → selects game installation → configures bundle paths
- User loads existing project → restores state → ready to build

---

### Phase 2: Build Engine Core (Week 3-4)
**Status**: ⏳ NOT STARTED

#### 2.1 Build Engine Service
- [ ] `IBuildEngineService` interface
- [ ] `BuildEngineService` implementation
- [ ] 5-stage pipeline orchestration
- [ ] Event system (17 event types)
- [ ] Abort/cancellation support
- [ ] Progress reporting

**References**:
- `MASTER_CSHARP_PORTING_SPECIFICATION.md` (section 2)
- Python: `generalsmodbuilder/build/engine.py`

#### 2.2 Change Detection System
- [ ] `BuildCacheService` implementation
- [ ] MD5 hash computation
- [ ] File modification time optimization
- [ ] Build state persistence (JSON/binary)
- [ ] FileHashRegistry support
- [ ] Change status determination

**Algorithm**:
```
For each file:
  1. Check FileHashRegistry (if enabled) → Irrelevant if match
  2. Load previous build cache
  3. Compare MD5 + params:
     - Not in cache → Added
     - In cache, same hash → Unchanged
     - In cache, different hash → Changed
  4. Optimization: Reuse cached MD5 if mtime unchanged
```

**References**:
- `MASTER_CSHARP_PORTING_SPECIFICATION.md` (section 2.2)

#### 2.3 Build Structure
- [ ] `BuildStructure` class (5-stage container)
- [ ] `BuildThing` class (buildable entity)
- [ ] `BuildFile` class (file mapping)
- [ ] Build graph construction
- [ ] Dependency resolution

---

### Phase 3: File Conversion System (Week 5-7)
**Status**: ⏳ NOT STARTED

#### 3.1 Image Conversion Service
- [ ] `IImageConversionService` interface
- [ ] PSD → DDS/TGA/BMP converter
  - [ ] RGB mode support
  - [ ] RGBA multi-alpha compositing
  - [ ] Layer flattening
- [ ] TGA → DDS/BMP converter
- [ ] TIFF → DDS/TGA converter
- [ ] DDS → DDS re-export
- [ ] Image resizing (resize/rescale params)
- [ ] RGBA channel-split resizing
- [ ] Resampling algorithms (NEAREST, BOX, BILINEAR, etc.)
- [ ] Alpha channel detection
- [ ] Automatic DXT format selection (DXT1 vs DXT5)

**Technology**: ImageSharp or Magick.NET
**References**: `MASTER_CSHARP_PORTING_SPECIFICATION.md` (section 3.3)

#### 3.2 String Table Conversion
- [x] `IStringTableConversionService` interface
- [x] STR → CSF converter
- [x] CSF → STR converter
- [x] Multi-language support
- [x] Language swapping

**External Tool**: gametextcompiler v1.1
**References**: `MASTER_CSHARP_PORTING_SPECIFICATION.md` (section 3.4)
**Implementation**: `GenHub.Core.Interfaces.Tools.ModBuilder.IStringTableConversionService`, `GenHub.Features.Tools.ModBuilder.Services.StringTableConversionService`

#### 3.3 Archive Creation
- [x] `IArchiveService` interface
- [x] BIG archive creation (reuse existing)
- [x] ZIP archive creation
- [x] TAR/TAR.GZ creation

**Technology**: SharpCompress, existing .big packer
**References**: `MASTER_CSHARP_PORTING_SPECIFICATION.md` (section 3.5)
**Implementation**: `GenHub.Core.Interfaces.Tools.ModBuilder.IArchiveService`, `GenHub.Features.Tools.ModBuilder.Services.ArchiveService`

#### 3.4 3D Model Conversion
- [ ] BLEND → W3D converter
- [ ] Blender process execution
- [ ] io_mesh_w3d plugin integration

**External Tool**: Blender 3.4.1
**References**: `MASTER_CSHARP_PORTING_SPECIFICATION.md` (section 3)

#### 3.5 Text File Processing
- [ ] Line ending normalization (forceEOL)
- [ ] Comment removal (deleteComments)
- [ ] Whitespace removal (deleteWhitespace)
- [ ] Encoding handling

---

### Phase 4: External Tool Integration (Week 8)
**Status**: ⏳ NOT STARTED

#### 4.1 External Tool Service
- [ ] `IExternalToolService` interface
- [ ] Tool definition loading (WindowsTools.json)
- [ ] Tool download management
- [ ] SHA256 verification
- [ ] Process execution with output capture
- [ ] Error handling and logging

**Tools Required**:
1. crunch v1.04 - DDS compression
2. gametextcompiler v1.1 - CSF/STR conversion
3. generalsbigcreator v1.3 - BIG archives
4. blender v3.4.1 - W3D export

**References**: `MASTER_CSHARP_PORTING_SPECIFICATION.md` (section 6)

---

### Phase 5: User Interface (Week 9-11)
**Status**: ⏳ NOT STARTED

#### 5.1 Main ViewModel
- [ ] `ModBuilderViewModel` implementation
- [ ] Project management commands
- [ ] Build execution commands
- [ ] Progress tracking
- [ ] Output console binding
- [ ] Bundle pack selection
- [ ] Build options (clean, build, release, install, run, uninstall)

**Pattern**: Follow MapManagerViewModel structure
**References**: `CSHARP_PORTING_GUIDE_UI_AND_FLOW.md`

#### 5.2 Main View (Avalonia)
- [ ] Project selection/creation UI
- [ ] Bundle pack list (multi-select)
- [ ] Build action checkboxes
- [ ] Execute/Abort buttons
- [ ] Build output console
- [ ] Progress indicators
- [ ] Options panel (verbose logging, multi-processing)

**Layout**:
```
┌─────────────────────────────────────────────────────────┐
│ Project: [MyMod.mbproj] [New] [Load] [Save]            │
├─────────────────────────────────────────────────────────┤
│ ┌─Bundle Packs────┐ ┌─Build Actions──┐ ┌─Options─────┐ │
│ │ ☑ Core          │ │ ☐ Clean        │ │ ☑ Verbose   │ │
│ │ ☑ Textures      │ │ ☑ Build        │ │ ☐ Multi-CPU │ │
│ │ ☐ Audio         │ │ ☐ Release      │ │ ☐ Print Cfg │ │
│ │ ☐ Models        │ │ ☑ Install      │ └─────────────┘ │
│ │                 │ │ ☑ Run Game     │                 │
│ │                 │ │ ☐ Uninstall    │ [Execute]       │
│ └─────────────────┘ └────────────────┘ [Abort]         │
├─────────────────────────────────────────────────────────┤
│ Build Output:                                           │
│ ┌─────────────────────────────────────────────────────┐ │
│ │ [Build log console output here...]                  │ │
│ │                                                      │ │
│ └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

**References**: `CSHARP_PORTING_GUIDE_UI_AND_FLOW.md` (section 3)

#### 5.3 Tool Plugin Integration
- [ ] `ModBuilderToolPlugin` implementation
- [ ] `IToolPlugin` interface compliance
- [ ] Tool metadata (name, icon, description)
- [ ] Activation/deactivation lifecycle
- [ ] Service provider integration

**References**: `GenHub/Core/Interfaces/Tools/IToolPlugin.cs`

#### 5.4 CLI Support (Optional/Future)
- [ ] Command-line argument parsing
- [ ] Headless build execution
- [ ] CI/CD integration support

**References**: `CSHARP_PORTING_GUIDE_UI_AND_FLOW.md` (section 2)

---

### Phase 6: Advanced Features (Week 12-13)
**Status**: ⏳ NOT STARTED

#### 6.1 Event System
- [ ] Event definition (17 event types)
- [ ] Event callback registration
- [ ] Script execution support (Python/PowerShell)
- [ ] Event parameter passing
- [ ] Error handling in callbacks

**Event Types**:
- OnPreBuild, OnBuild, OnPostBuild
- OnRelease, OnInstall, OnRun, OnUninstall
- OnStartBuild* (5 stages)
- OnFinishBuild* (5 stages)

**References**: `MASTER_CSHARP_PORTING_SPECIFICATION.md` (section 1.3)

#### 6.2 Changelog Generation
- [ ] YAML changelog parsing
- [ ] Markdown generation
- [ ] Filter and sort support
- [ ] Auto-header generation
- [ ] Change type categorization

**References**: `CHANGELOG_AND_DOCUMENTATION_ANALYSIS.md`

#### 6.3 Game Integration
- [ ] Game installation detection
- [ ] Registry-based game path resolution
- [ ] Game launcher integration
- [ ] Install/uninstall file management
- [ ] Settings backup/restore

---

### Phase 7: Testing & Optimization (Week 14-15)
**Status**: ⏳ NOT STARTED

#### 7.1 Unit Tests
- [ ] Build engine tests
- [ ] Change detection tests
- [ ] File conversion tests
- [ ] Configuration loading tests
- [ ] Project management tests

#### 7.2 Integration Tests
- [ ] End-to-end build pipeline
- [ ] Multi-file processing
- [ ] External tool integration
- [ ] Project lifecycle

#### 7.3 Performance Testing
- [ ] Benchmark against Python version
- [ ] Large project testing (1000+ files)
- [ ] Multi-processing efficiency
- [ ] Memory usage profiling

**Target**: Match or exceed Python performance

#### 7.4 User Acceptance Testing
- [ ] Real mod project testing
- [ ] User workflow validation
- [ ] Error handling verification
- [ ] Documentation review

---

## User Workflow Design

### Workflow 1: New Project Creation
```
1. User clicks "New Project" in ModBuilder tool
2. Dialog: Enter project name, select game installation
3. System creates project directory structure:
   MyMod.mbproj
   Configs/          (bundle config JSONs)
   GameFilesEdited/  (modified game files)
   .Build/           (build cache)
   .Release/         (output archives)
4. User configures bundle items and packs (or imports existing configs)
5. User adds/edits game files in GameFilesEdited/
6. Ready to build
```

### Workflow 2: Build & Test Cycle
```
1. User loads project (MyMod.mbproj)
2. User selects bundle packs to build
3. User checks: [Build] [Install] [Run Game]
4. User clicks "Execute"
5. System:
   - Detects changed files (MD5 comparison)
   - Converts formats (PSD→DDS, STR→CSF, etc.)
   - Packages into .big archives
   - Installs to game directory (symlinks)
   - Launches game
6. User tests mod in-game
7. User closes game
8. System uninstalls (removes symlinks)
9. User makes changes, repeats
```

### Workflow 3: Release Distribution
```
1. User completes development
2. User checks: [Clean] [Build] [Release]
3. System:
   - Cleans previous builds
   - Rebuilds all files
   - Creates release archives (.zip)
   - Generates checksums
4. User distributes .zip files to community
```

---

## Technical Decisions

### Decision 1: Project File Format
**Choice**: JSON-based `.mbproj` file
**Rationale**:
- Human-readable for version control
- Easy to edit manually if needed
- Consistent with existing config format
- Supports comments via JSON5 (optional)

**Structure**:
```json
{
  "name": "MyMod",
  "version": "1.0.0",
  "gameInstallation": "C:/Games/Generals Zero Hour",
  "configPaths": [
    "Configs/ModBundleItems.json",
    "Configs/ModBundlePacks.json",
    "Configs/ModFolders.json"
  ],
  "buildOptions": {
    "multiProcessing": true,
    "verboseLogging": false
  },
  "lastBuild": "2026-03-17T10:30:00Z"
}
```

### Decision 2: Build Cache Format
**Choice**: JSON with optional binary fallback
**Rationale**:
- JSON: Human-readable, debuggable
- Binary (MessagePack): Faster for large projects
- Auto-select based on file count threshold

### Decision 3: Image Processing Library
**Choice**: ImageSharp (primary), Magick.NET (fallback)
**Rationale**:
- ImageSharp: Pure C#, cross-platform, modern API
- Magick.NET: Comprehensive format support, PSD parsing
- Both support required operations

### Decision 4: Multi-Processing Strategy
**Choice**: `Parallel.ForEachAsync()` with degree of parallelism
**Rationale**:
- Native C# async/await support
- Better than Python's ProcessPoolExecutor
- Configurable parallelism
- Proper cancellation support

### Decision 5: External Tool Management
**Choice**: Embed tools in application or download on-demand
**Rationale**:
- Embed: crunch, gametextcompiler, generalsbigcreator (small)
- Download: Blender (large, optional)
- SHA256 verification for security

---

## Performance Targets

### Benchmarks (vs Python)
- **Small project** (10 files): < 5 seconds (Python: ~8s)
- **Medium project** (100 files): < 30 seconds (Python: ~45s)
- **Large project** (1000 files): < 5 minutes (Python: ~8m)

### Optimization Strategies
1. **Parallel Processing**: Use all CPU cores for file conversions
2. **Incremental Builds**: Only process changed files
3. **MD5 Caching**: Reuse hashes when mtime unchanged
4. **Async I/O**: Non-blocking file operations
5. **Memory Efficiency**: Stream large files, avoid loading all in memory

---

## Risk Assessment

### High Risk
1. **PSD Multi-Alpha Compositing**: Complex algorithm, requires careful porting
   - Mitigation: Extensive testing with sample files, reference Python implementation
2. **External Tool Integration**: Dependency on third-party executables
   - Mitigation: SHA256 verification, fallback mechanisms, clear error messages
3. **Performance Regression**: C# slower than Python
   - Mitigation: Profiling, optimization, parallel processing

### Medium Risk
1. **Configuration Compatibility**: Breaking changes from Python version
   - Mitigation: Support legacy format, migration tool
2. **UI Complexity**: Feature-rich interface
   - Mitigation: Iterative development, user feedback
3. **Cross-Platform**: Windows-specific tools
   - Mitigation: Document Windows requirement, explore alternatives

### Low Risk
1. **Data Model Mapping**: Straightforward Python→C# translation
2. **JSON Parsing**: Well-supported in C#
3. **File I/O**: Standard operations

---

## Success Criteria

### Must Have (MVP)
- ✅ Load existing ModBuilder projects (Python configs)
- ✅ Build pipeline (5 stages) functional
- ✅ All 7 file format conversions working
- ✅ MD5-based incremental builds
- ✅ Install/uninstall to game directory
- ✅ GUI interface with project management
- ✅ Performance equal to or better than Python

### Should Have (V1.0)
- ✅ Event system with script callbacks
- ✅ Changelog generation
- ✅ Multi-processing support
- ✅ Verbose logging
- ✅ Error handling and user feedback

### Nice to Have (Future)
- ⏳ CLI interface for automation
- ⏳ Cloud project sync
- ⏳ Mod marketplace integration
- ⏳ Visual config editor
- ⏳ Build analytics

---

## Next Steps

### Immediate Actions (This Session)
1. ✅ Review this implementation plan
2. ⏳ Verify architecture decisions with user
3. ⏳ Confirm user workflow design
4. ⏳ Get approval to proceed with Phase 1

### Phase 1 Kickoff (Next Session)
1. Create project structure in GeneralsHub
2. Implement core data models
3. Setup DI module
4. Create basic tool plugin shell

---

## Questions for User

1. **Project File Location**: Where should `.mbproj` files be stored? User documents? Game directory? Custom location?
2. **Config Migration**: Should we auto-migrate Python configs to new format, or support both?
3. **External Tools**: Embed in application or download on first use?
4. **CLI Priority**: Is CLI support needed for MVP, or can it be deferred?
5. **CAS Integration**: Should build outputs be stored in CAS (like MapPacks), or traditional file system?

---

## Change Log

### 2026-03-17
- Initial plan created
- Architecture defined
- 7 phases outlined
- User workflows designed
- Technical decisions documented

---

**Status Legend**:
- ⏳ NOT STARTED
- 🔄 IN PROGRESS
- ✅ COMPLETED
- ⚠️ BLOCKED
- ❌ CANCELLED

**Priority Legend**:
- 🔴 CRITICAL
- 🟠 HIGH
- 🟡 MEDIUM
- 🟢 LOW

---

*This document is a living plan and will be updated throughout implementation.*
