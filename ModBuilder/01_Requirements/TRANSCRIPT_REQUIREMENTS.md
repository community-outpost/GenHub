# ModBuilder C# Port - Comprehensive Requirements Document

**Source**: 1-hour transcript between Visionist (original creator) and C# port developer
**Date Extracted**: 2026-03-18
**Purpose**: Complete requirements specification for porting Python ModBuilder to C#

---

## Executive Summary

ModBuilder is a build automation tool for game mod development that transforms source files into distributable mod packages. The core promise is **one-click build-install-run workflow** with intelligent change detection and incremental builds. Performance is CRITICAL - the C# implementation must be faster than the Python version.

---

## 1. CORE CONCEPTS

### 1.1 Bundle Hierarchy
- **Bundle Packs**: Top-level distribution units (e.g., "Core Arabic", "Full English")
  - Represent complete products for end users
  - Cater to different languages/configurations
  - Two types: Core (essential) and Full (includes optional content)

- **Bundle Items**: Mid-level components within packs
  - Group related game files together
  - Examples: "core_audio_arabic", "core_textures", "core_ini"
  - Have naming schemes with prefixes for .big file generation
  - Can be marked for .big file inclusion or loose file distribution

- **Game Files**: Lowest level - actual source files
  - Can be used directly (symlinked) or transformed
  - Subject to file conversions and processing

### 1.2 File Transformation Pipeline
Source files in repository → Intermediate processing → Final distribution files

**Key Transformations**:
- PSD (Photoshop) → DDS (DirectDraw Surface)
- TGA → DDS
- TIFF → DDS
- STR (text) → CSF (compiled string file)
- BLEND (Blender) → W3D (Westwood 3D)
- INI files: whitespace removal, text optimization
- Custom filename transformations supported

---

## 2. BUILD PIPELINE STAGES

### 2.1 Build Step State Machine
The build process follows a strict state machine with these stages:

1. **Pre-Build**
   - Re-interpret configuration data
   - Validate JSON configurations
   - Prepare build environment

2. **Uninstall** (if previous build exists)
   - Remove previously installed files from game directory
   - Restore clean state
   - Uses tracked file list from previous build

3. **Clean** (optional)
   - Clear build artifacts
   - Reset build cache

4. **Build**
   - Process source files
   - Apply transformations
   - Create raw bundle items (intermediate files)
   - Generate .big archive files
   - Hash all files for change detection

5. **Post-Build**
   - Additional processing steps
   - Custom script execution (if configured)

6. **Build Release**
   - Create final distribution packages
   - Generate ZIP files for distribution
   - Optional: Create installer packages

7. **Install**
   - Copy/install built files to game directory
   - Track installed files for future uninstall
   - Requires admin rights if game in C:\Program Files

8. **Run**
   - Launch the game with installed mod
   - Restore game language settings after exit

9. **Uninstall** (post-run)
   - Remove installed mod files
   - Restore original game state
   - Minimize intrusion to game installation

### 2.2 Build Artifacts Structure
```
build/
├── raw_bundle_items/          # Intermediate processed files
│   ├── core_ini/              # Symlinks or processed files
│   ├── core_textures/         # Converted textures
│   └── ...
├── bundles/                   # .big archive files
│   ├── core_ini.big
│   ├── core_textures.big
│   └── ...
├── bundle_packs/              # Language/config specific packs
│   ├── core_english/
│   ├── core_arabic/
│   └── ...
└── release/                   # Final distribution ZIPs
    ├── SuperPatch_Core_English_v0.zip
    └── SuperPatch_Full_English_v0.zip
```

---

## 3. CHANGE DETECTION & INCREMENTAL BUILDS

### 3.1 MD5 Hashing System
**CRITICAL REQUIREMENT**: Robust change detection is essential

- **Hash all source files** on initial discovery
- **Store hashes** in serialized cache files (Python uses pickle, C# should use appropriate format)
- **Compare hashes** on subsequent builds to detect changes
- **Track modification timestamps** (optimization, but hash is authoritative)

### 3.2 Incremental Build Logic
**Performance Optimization**: Only rebuild what changed

Example workflow:
1. Developer modifies `weapon.ini`
2. ModBuilder detects hash change
3. Only rebuilds:
   - `weapon.ini` → processed version
   - `core_ini.big` (containing weapon.ini)
   - Bundle packs containing core_ini
4. Skips all unchanged bundles/items

### 3.3 Change Detection Requirements
- **MUST detect**: File content changes (via MD5)
- **MUST detect**: New files added
- **MUST detect**: Files deleted
- **SHOULD optimize**: Use file modification date as pre-check (but always verify with hash)
- **MUST be robust**: No false negatives - if file changed, MUST rebuild

**Creator's Warning**: "I had bugs where I touched source files and it didn't pick it up for some reason. You need to be very conscious about this."

---

## 4. PERFORMANCE REQUIREMENTS

### 4.1 Critical Performance Benchmarks
**MANDATORY**: C# version MUST be faster than Python

- **Full clean build** (all languages): Currently 10-15 minutes in Python
- **Single file change**: Should be near-instant (seconds, not minutes)
- **Texture compression**: Major bottleneck, needs parallelization

### 4.2 Performance Constraints
**Creator's Emphasis**: "There's no excuse for being slower than Python. Python is a slow piece of shit."

**Key Optimization Areas**:

1. **Disk I/O Optimization** (CRITICAL)
   - Minimize file existence checks
   - Minimize file reads
   - Cache file metadata in memory
   - Only access files when absolutely necessary
   - "Even a seemingly harmless file exist check is a cost"

2. **Multi-Threading** (HIGH PRIORITY)
   - Python version is single-threaded (multiprocessing broken in frozen builds)
   - Texture compression is highly parallelizable
   - Many build tasks can run in parallel
   - Expected: Significant speedup with proper threading

3. **Memory Management**
   - Keep state in memory where possible
   - Serialize/deserialize build cache efficiently
   - Avoid redundant file operations

### 4.3 Benchmarking Strategy
- Use Python version as baseline
- Disable verbose logging for accurate timing
- Build all bundles in sequence for full benchmark
- Compare individual stage timings

---

## 5. EXTERNAL TOOL INTEGRATION

### 5.1 Required External Tools
ModBuilder orchestrates these external tools:

1. **crunch** - Texture compression (DDS generation)
2. **gametextcompiler** - STR → CSF conversion
3. **generalsbigcreator** - .big archive creation
4. **Blender** - BLEND → W3D conversion (via command line)

### 5.2 Tool Configuration
- Tools defined in `tools.json` configuration
- Paths configurable per platform (Windows/Linux)
- Command-line argument templates
- Error handling for missing tools

---

## 6. CONFIGURATION SYSTEM

### 6.1 JSON Configuration Files
**Four core configuration files**:

1. **bundles.json** - Defines packs, items, file mappings
   - Bundle packs (distribution units)
   - Bundle items (component groups)
   - Source/target file mappings
   - Transformation rules
   - Wildcard support (*.tga, *.dds)

2. **tools.json** - External tool definitions
   - Tool paths
   - Command-line templates
   - Platform-specific configurations

3. **runner.json** - Game execution settings
   - Game executable path
   - Launch arguments
   - Install/uninstall behavior

4. **main.json** - Project-wide settings
   - Folder locations
   - Build output paths
   - Version information
   - Product name

### 6.2 Configuration Schema
**Current Issues** (per creator):
- JSON files have deep nesting (bundles → items → files → transformations)
- Not intuitive for new users
- Lacks documentation
- Hard to maintain without knowing the schema

**Future Enhancement** (NICE TO HAVE):
- GUI editor for JSON configurations
- Drag-and-drop interface
- Schema validation
- Auto-completion/IntelliSense

### 6.3 Version Management
- Configuration files include version numbers
- Supports legacy compatibility
- Projects can use different ModBuilder versions
- Version bumps when breaking changes occur

---

## 7. USER WORKFLOW

### 7.1 Developer Workflow
**Primary Use Case**: Mod developer iterating on content

1. Clone game patch repository
2. Run setup script (downloads ModBuilder)
3. Open project in IDE
4. Modify source files (INI, textures, etc.)
5. Click "Execute" button in ModBuilder
6. ModBuilder:
   - Detects changes
   - Rebuilds only affected files
   - Installs to game directory
   - Launches game
7. Test changes in-game
8. Exit game
9. ModBuilder auto-uninstalls, restores clean state
10. Repeat from step 4

**Key Promise**: "One click and everything is done and ready to test"

### 7.2 Command-Line Interface
**MUST HAVE**: Full CLI support for CI/CD

- All GUI operations available via CLI
- JSON-driven configuration (no GUI required)
- Suitable for automated builds
- Example: `modbuilder.exe --build --config=bundles.json`

### 7.3 GUI Requirements
**MUST HAVE**: Desktop GUI for developers

Current Python GUI features:
- Bundle pack selection (checkboxes)
- Build step buttons (Build, Install, Run, Clean, etc.)
- Verbose logging toggle
- Progress output (terminal window)
- Single-click execution

**Creator's Note**: Terminal UI (TUI) acceptable if it has buttons/interactivity

---

## 8. DISTRIBUTION MODEL

### 8.1 Current Model (Python)
**Issues**:
- ModBuilder distributed as frozen executable
- Downloaded from GitHub releases
- Version pinned in project's batch scripts
- Requires updating hash/size in setup scripts
- Not easily modifiable by developers

### 8.2 Desired Model (Future)
**SHOULD HAVE**:
- ModBuilder as loose scripts in project repository
- Auto-download dependencies
- Easily modifiable by developers
- OR: Native compiled executable (C#/Go) that's fast enough to justify binary distribution

### 8.3 Project-Tool Relationship
**CRITICAL DESIGN**: Project drives tool version, not vice versa

- Each project specifies ModBuilder version
- Multiple projects can use different versions
- Prevents breaking changes from affecting old projects
- ModBuilder installed locally per project (not global)

---

## 9. ADVANCED FEATURES

### 9.1 Custom Build Scripts
**OPTIONAL** (may not be needed with code compilation):

- Python scripts injectable into build pipeline
- Triggered on specific build events
- Example events:
  - `on_finish_build_raw_bundle_item`
  - `on_post_build`
  - `on_pre_build`
- Use cases:
  - Custom logging
  - Data transformations
  - Resolution-specific adjustments (e.g., 4K font scaling)

**Creator's Note**: "Maybe we don't need this in the future because if we need customizability, it should be a code feature, not data hacks."

### 9.2 Change Log Generation
**USEFUL FEATURE**:

- Reads YAML change log files
- Generates multiple output formats (Markdown, HTML)
- Filtering by labels (severity, faction, controversial)
- Sorting options (date, severity, category)
- Multiple views for different audiences

### 9.3 Original File Tracking
**DEPRECATED FEATURE**:

- Tracked unmodified original files
- Excluded from distribution if unchanged
- Creator now considers this "nonsense" - unmodified files shouldn't be in repository

---

## 10. FUTURE REQUIREMENTS

### 10.1 Code Compilation Integration
**HIGH PRIORITY** (not yet implemented):

When code becomes part of the mod:
- One-button build should compile code + build data
- Support for referencing pre-compiled binaries
- OR: Automatic compilation in correct configuration
- Ensure distributed build matches developer's test build exactly

**Creator's Vision**: "Press execute, it compiles the code, builds the data, installs it, runs it - one button press."

### 10.2 Better Distribution Model
**SHOULD HAVE**:
- Eliminate manual version updates in batch scripts
- Auto-update mechanism
- OR: Fast enough native binary that updates are rare

### 10.3 Enhanced GUI
**NICE TO HAVE**:
- Configuration editor (visual JSON editing)
- Drag-and-drop file management
- Override settings without editing JSON (e.g., executable name)
- Better progress visualization

---

## 11. TECHNICAL CONSTRAINTS

### 11.1 Python Limitations (Why Port is Needed)
1. **Performance**: Slow, hit optimization ceiling
2. **Multi-threading**: Multiprocessing broken in frozen builds
3. **Distribution**: Frozen executables are opaque, hard to modify
4. **Startup time**: Interpreted language overhead

### 11.2 Critical Implementation Warnings

**From Creator**:

1. **Disk I/O**: "You have to be very conscious about not wasting cycles on disk access"
   - Minimize file existence checks
   - Cache metadata in memory
   - Only read files when necessary

2. **Change Detection**: "It's easy to mess up"
   - Must be 100% reliable
   - No false negatives allowed
   - Hash-based detection is authoritative

3. **Testing**: "Be diligent with testing and making sure this produces correct results"
   - Easy to make mistakes
   - Compare output with Python version
   - Verify file hashes match

4. **Performance**: "No excuse for being slower than Python"
   - Use Python version as benchmark
   - Must be faster, especially with multi-threading

### 11.3 Platform Considerations
- **Primary**: Windows (game is Windows-only)
- **Admin Rights**: Required for installation to C:\Program Files
- **Paths**: Use forward slashes (Unix-style) even on Windows
- **Symlinks**: Used for unchanged files (Windows symlink support required)

---

## 12. REFERENCE IMPLEMENTATIONS

### 12.1 Projects to Study
1. **generals-game-patch** (primary reference)
   - Most comprehensive use of ModBuilder
   - Latest version (v23)
   - Full feature set

2. **modbuilder-sample** (learning reference)
   - Minimal configuration
   - Simpler to understand
   - Less noise than full project

3. **generals-control-bar-pro** (legacy reference)
   - Uses older ModBuilder v1.5
   - Shows version compatibility

### 12.2 Code Structure (Python)
- `engine.py` - Main build logic
- `build_steps.py` - State machine implementation
- `copy_logic.py` - File copying/transformation
- `gui.py` - GUI implementation
- `main.py` - Entry point, CLI/GUI routing

---

## 13. REQUIREMENTS PRIORITY MATRIX

### MUST HAVE (P0)
- ✅ Bundle pack/item/file hierarchy
- ✅ File transformation pipeline (PSD→DDS, STR→CSF, etc.)
- ✅ MD5-based change detection
- ✅ Incremental builds
- ✅ Build state machine (all 9 stages)
- ✅ External tool integration
- ✅ JSON configuration system
- ✅ CLI interface
- ✅ Desktop GUI (or TUI with buttons)
- ✅ One-click build-install-run workflow
- ✅ Performance: Faster than Python baseline
- ✅ Multi-threading support
- ✅ Disk I/O optimization
- ✅ .big archive creation
- ✅ Install/uninstall tracking

### SHOULD HAVE (P1)
- ✅ Code compilation integration
- ✅ Better distribution model (loose scripts or fast binary)
- ✅ Legacy configuration compatibility
- ✅ Change log generation
- ✅ Verbose logging toggle
- ✅ Symlink support for unchanged files
- ✅ File modification date optimization
- ✅ Build cache serialization

### NICE TO HAVE (P2)
- ⚪ Visual JSON configuration editor
- ⚪ Drag-and-drop file management
- ⚪ GUI overrides for JSON settings
- ⚪ Custom build script injection
- ⚪ HTML change log output
- ⚪ Installer package generation
- ⚪ Progress visualization improvements

### DEPRECATED (Don't Implement)
- ❌ Original file tracking (unmodified file exclusion)
- ❌ Python script injection (replace with code features)

---

## 14. PERFORMANCE BENCHMARKS

### 14.1 Target Metrics
- **Full clean build**: < 10 minutes (Python: 10-15 min)
- **Single file change**: < 5 seconds
- **Texture batch conversion**: 50%+ faster with multi-threading
- **File hashing**: Minimal overhead (< 1% of build time)

### 14.2 Optimization Priorities
1. Multi-threading (highest impact)
2. Disk I/O reduction (critical)
3. Memory caching (important)
4. Algorithm efficiency (important)

---

## 15. TESTING STRATEGY

### 15.1 Correctness Validation
- **Output Comparison**: C# build output must match Python byte-for-byte
- **Hash Verification**: All generated files must have identical MD5 hashes
- **Incremental Build Testing**: Verify only changed files are rebuilt
- **Edge Cases**: Empty bundles, missing files, invalid configs

### 15.2 Performance Testing
- **Baseline**: Run Python version with verbose logging disabled
- **Comparison**: Run C# version with same configuration
- **Metrics**: Total time, per-stage time, file operation counts
- **Regression**: Ensure performance doesn't degrade over time

### 15.3 Integration Testing
- **External Tools**: Verify all tool integrations work
- **File Formats**: Test all transformation types
- **Platforms**: Windows (primary), consider Linux/Mac
- **Admin Rights**: Test with/without elevation

---

## 16. MIGRATION PATH

### 16.1 Phased Implementation
**Phase 1**: Core engine (P0 features)
- Configuration loading
- File discovery and hashing
- Change detection
- Basic build pipeline
- External tool integration

**Phase 2**: Performance optimization
- Multi-threading
- Disk I/O optimization
- Memory caching
- Benchmark validation

**Phase 3**: User interface
- CLI implementation
- GUI/TUI implementation
- Progress reporting
- Error handling

**Phase 4**: Advanced features (P1)
- Code compilation integration
- Change log generation
- Enhanced distribution model

**Phase 5**: Polish (P2)
- Visual configuration editor
- Additional GUI features
- Documentation

### 16.2 Compatibility Strategy
- Support existing JSON schema (version 1)
- Provide migration tools if schema changes
- Maintain backward compatibility where possible
- Clear versioning and changelog

---

## 17. CRITICAL SUCCESS FACTORS

1. **Performance**: Must be faster than Python (non-negotiable)
2. **Correctness**: Must produce identical output to Python version
3. **Reliability**: Change detection must be 100% accurate
4. **Usability**: One-click workflow must be preserved
5. **Maintainability**: Code must be understandable by creator
6. **Compatibility**: Must work with existing projects

---

## 18. CREATOR'S PHILOSOPHY

**Key Quotes**:

- "One click and everything is done and ready to test"
- "There's no excuse for being slower than Python"
- "You have to be very conscious about not wasting cycles on disk access"
- "It's easy to make mistakes and easy to build things in slow ways"
- "The project is in the driver's seat, not ModBuilder"
- "Whatever it builds here is an exact representation of what the user will get"

**Design Principles**:
- Minimize intrusion to game installation
- Restore clean state after testing
- Make change detection robust, not clever
- Optimize for developer iteration speed
- Keep configuration separate from tool
- Version compatibility over forced upgrades

---

## 19. OPEN QUESTIONS FOR IMPLEMENTATION

1. **Serialization Format**: What replaces Python pickle for build cache?
2. **GUI Framework**: WPF, WinForms, Avalonia, or Terminal.Gui?
3. **Threading Model**: Task Parallel Library, async/await, or manual threads?
4. **Configuration Validation**: JSON Schema, FluentValidation, or custom?
5. **Logging Framework**: Serilog, NLog, or built-in?
6. **Archive Library**: SharpCompress, DotNetZip, or native?
7. **Hashing**: System.Security.Cryptography or faster alternative?

---

## 20. NEXT STEPS

1. **Review**: Share this document with creator for validation
2. **Prototype**: Build proof-of-concept for single bundle
3. **Benchmark**: Establish baseline performance metrics
4. **Design**: Create C# architecture matching Python structure
5. **Implement**: Phase 1 (core engine)
6. **Test**: Validate against Python output
7. **Iterate**: Optimize and add features

---

**Document Version**: 1.0
**Last Updated**: 2026-03-18
**Status**: Ready for Review