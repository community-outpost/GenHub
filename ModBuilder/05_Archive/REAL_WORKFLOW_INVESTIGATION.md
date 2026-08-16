# ModBuilder Real Workflow Investigation

**Date**: March 20, 2026
**Purpose**: Understand the REAL ModBuilder workflow and fix the UI to match user expectations
**Status**: COMPLETE ANALYSIS

---

## Section 1: Original Workflow (Python ModBuilder)

### How Python ModBuilder Worked

**Core Concept**: ModBuilder is a **build automation tool** that transforms source game files into distributable mod packages. It's NOT a file editor - it's a **build pipeline orchestrator**.

**User Experience Flow**:
1. **Setup Phase**: User clones/downloads a mod project repository
2. **Edit Phase**: User edits game files in their preferred editors (Photoshop, text editor, etc.)
3. **Build Phase**: User clicks "Execute" in ModBuilder GUI
4. **Test Phase**: ModBuilder builds, installs, and launches the game automatically
5. **Iterate**: User exits game, ModBuilder uninstalls, user edits more files, repeat

**Key Insight**: ModBuilder is like a **Makefile system** for game mods, not a file manager or editor.

### Python GUI Layout

```
┌─────────────────────────────────────────────────────────────┐
│  Bundle Pack List  │  Sequence Execution  │  Single Actions  │  Options  │
├────────────────────┼──────────────────────┼──────────────────┼───────────┤
│  ☑ Core English   │  ☐ Make Change Log   │  [Make Change]   │  ☑ Auto   │
│  ☐ Core Arabic    │  ☐ Clean             │  [Clean]         │    Clear  │
│  ☑ Full English   │  ☑ Build             │  [Build]         │  ☐ Print  │
│  ☐ Full Arabic    │  ☑ Build Release     │  [Build Release] │    Config │
│  ☐ Lite English   │  ☑ Install           │  [Install]       │  ☐ Verbose│
│                    │  ☑ Run Game          │  [Run Game]      │  ☐ Multi  │
│                    │  ☐ Uninstall         │  [Uninstall]     │    Process│
│                    │                      │  [Abort]         │           │
│                    │  [Execute]           │                  │           │
│                    │  (runs all checked)  │  (runs one)      │           │
└────────────────────┴──────────────────────┴──────────────────┴───────────┘
│                         Build Output Console                              │
│  [INFO] Loading configuration...                                         │
│  [INFO] Processing CoreMod...                                            │
│  [INFO]   Converting texture.tga -> texture.dds (DXT5)                   │
│  [SUCCESS] Build completed in 3.2 seconds                                │
└───────────────────────────────────────────────────────────────────────────┘
```

**Critical Features**:
- **Bundle Pack Selection**: Choose which mod variants to build (languages, configurations)
- **Sequence Execution**: Check multiple actions, click Execute once
- **Single Actions**: Run individual steps for debugging
- **Options**: Control verbosity and performance
- **Console Output**: Real-time build progress

---

## Section 2: Project Structure

### Actual ModBuilder Project Structure

```
MyMod/
├── MyMod.mbproj                    # Project file (JSON metadata)
│
├── Configs/                        # Build configuration JSONs
│   ├── ModBundleItems.json         # Defines what files to process
│   ├── ModBundlePacks.json         # Defines distribution packages
│   ├── ModFolders.json             # Directory paths
│   └── ModJsonFiles.json           # Orchestrates other configs
│
├── GameFilesEdited/                # YOUR MOD SOURCE FILES (you edit these)
│   └── Data/
│       ├── INI/                    # Game configuration files
│       │   ├── Object/
│       │   │   └── AmericaTank.ini # Example: edit tank stats
│       │   └── Weapon/
│       │       └── TankCannon.ini  # Example: edit weapon damage
│       ├── Art/
│       │   └── Textures/
│       │       └── tank.tga        # Example: edit tank texture (PSD/TGA)
│       └── Audio/
│           └── Sounds/
│               └── explosion.wav   # Example: edit sound effects
│
├── .Build/                         # BUILD OUTPUT (generated, don't edit)
│   ├── cache.json                  # MD5 hashes for change detection
│   ├── RawBundleItem/              # Processed files (intermediate)
│   │   └── CoreMod/
│   │       └── Data/
│   │           ├── INI/
│   │           │   └── Object/
│   │           │       └── AmericaTank.ini  # Processed INI
│   │           └── Art/
│   │               └── Textures/
│   │                   └── tank.dds         # Converted to DDS
│   └── BigBundleItem/              # .big archive files
│       └── MyMod_CoreMod.big       # Final game archive
│
└── .Release/                       # DISTRIBUTION PACKAGES (generated)
    └── MyMod_v1.0.0.zip            # Release package for users
```

### Key Directories Explained

**GameFilesEdited/** - **THIS IS WHERE YOU WORK**
- Contains your mod's source files
- Mirrors the game's Data/ structure
- You edit files here with your preferred tools:
  - INI files: Any text editor
  - Textures: Photoshop, GIMP (save as PSD/TGA)
  - Audio: Audacity, Adobe Audition (save as WAV)
  - Models: Blender (save as BLEND)

**.Build/** - **BUILD ARTIFACTS (auto-generated)**
- RawBundleItem/: Processed files (conversions applied)
- BigBundleItem/: .big archives ready for game
- cache.json: Tracks file changes (MD5 hashes)
- **Don't edit these** - they're regenerated on build

**.Release/** - **DISTRIBUTION PACKAGES (auto-generated)**
- ZIP files for end users
- Created by "Release" action
- Contains .big files and documentation

**Configs/** - **BUILD CONFIGURATION**
- JSON files that tell ModBuilder:
  - Which files to process
  - How to convert them (TGA→DDS, STR→CSF)
  - How to package them (.big archives)
  - Which languages/variants to build

---

## Section 3: User Workflow (Step-by-Step)

### The REAL Workflow

#### Step 1: Create/Open Project
**What happens**:
- User creates new project OR opens existing .mbproj file
- ModBuilder loads configuration from Configs/ directory
- UI populates bundle pack list from configuration

**User sees**:
- Project path displayed
- Bundle packs listed (e.g., "Core English", "Full Arabic")
- Build actions available

#### Step 2: Edit Game Files
**What happens**:
- User opens GameFilesEdited/ directory in file explorer
- User edits files with external tools:
  - Open `GameFilesEdited/Data/INI/Weapon/TankCannon.ini` in Notepad++
  - Change damage value from 100 to 150
  - Save file
  - OR: Open `GameFilesEdited/Data/Art/Textures/tank.psd` in Photoshop
  - Change tank color to red
  - Save as PSD or TGA

**User sees**:
- Files in GameFilesEdited/ directory
- **ModBuilder UI doesn't show file editing** - that's external!

#### Step 3: Configure Build
**What happens**:
- User selects which bundle packs to build (checkboxes)
- User checks which actions to run:
  - ☑ Clean (optional, removes old build)
  - ☑ Build (required, processes files)
  - ☑ Install (optional, copies to game)
  - ☑ Run Game (optional, launches game)

**User sees**:
- Checkboxes for bundle packs
- Checkboxes for build actions
- Options (verbose logging, multi-processing)

#### Step 4: Execute Build
**What happens**:
1. **Clean** (if checked): Deletes .Build/ contents
2. **Build**:
   - Reads GameFilesEdited/ files
   - Checks MD5 hashes against cache
   - Only processes changed files (incremental build)
   - Applies conversions:
     - tank.tga → tank.dds (DDS compression)
     - weapon.ini → weapon.ini (whitespace removal)
     - strings.str → strings.csf (compiled string table)
   - Creates .big archives in .Build/BigBundleItem/
3. **Install** (if checked): Copies .big files to game directory
4. **Run Game** (if checked): Launches game executable

**User sees**:
- Real-time console output:
  ```
  [INFO] Loading configuration...
  [INFO] Loaded 3 bundle items, 2 bundle packs
  [INFO] Starting build pipeline...
  [INFO] Stage 1: Processing RawBundleItem...
  [INFO]   Processing CoreMod...
  [INFO]     Converting tank.tga -> tank.dds (DXT5)
  [INFO]     Copying TankCannon.ini -> TankCannon.ini
  [INFO]   Processed 45 files (2 changed, 43 unchanged)
  [INFO] Stage 2: Creating BigBundleItem...
  [INFO]   Creating MyMod_CoreMod.big...
  [INFO]   Archive created: 12.5 MB
  [INFO] Build completed in 3.2 seconds
  [SUCCESS] Build successful!
  ```

#### Step 5: Test in Game
**What happens**:
- Game launches with mod installed
- User tests changes (red tank, new damage value)
- User exits game

**User sees**:
- Game running with mod active

#### Step 6: Iterate
**What happens**:
- User edits more files in GameFilesEdited/
- User clicks Execute again
- ModBuilder detects only changed files (fast incremental build)
- Repeat cycle

**User sees**:
- Fast rebuilds (only changed files processed)
- Immediate testing feedback

---

## Section 4: Current C# Implementation Status

### What's Implemented ✅

**Core Services**:
- ✅ `IBuildEngineService` - Build pipeline orchestration
- ✅ `IProjectConfigService` - Project file management (.mbproj)
- ✅ `IConfigurationLoaderService` - JSON configuration loading
- ✅ `IImageConversionService` - PSD/TGA/TIFF → DDS conversion
- ✅ `IArchiveService` - .big/.zip archive creation
- ✅ `IBuildCacheService` - MD5-based change detection
- ✅ `IFileHashRegistryService` - File hash tracking

**UI Components**:
- ✅ ModBuilderView.axaml - Main UI layout
- ✅ ModBuilderViewModel.cs - ViewModel with commands
- ✅ Project management (New, Open, Save)
- ✅ Bundle pack selection (checkboxes)
- ✅ Build action checkboxes (Clean, Build, Release, Install, Run, Uninstall)
- ✅ Build output console
- ✅ Progress tracking

**Build Pipeline**:
- ✅ 5-stage pipeline (RawBundleItem → BigBundleItem → RawBundlePack → ReleaseBundlePack → InstallBundlePack)
- ✅ Incremental builds (MD5 caching)
- ✅ Parallel processing (multi-threading)
- ✅ File conversions (images, strings, archives)

### What's Missing/Broken ❌

**Critical Issues**:
1. ❌ **No sample project included** - User has nothing to test with
2. ❌ **Unclear workflow** - UI doesn't explain what to do
3. ❌ **No file browser** - Can't easily navigate to GameFilesEdited/
4. ❌ **Crash after running** - Likely exception in build execution
5. ❌ **No error handling UI** - Exceptions not displayed properly
6. ❌ **No project templates** - Can't create basic project easily

**Missing Features**:
- ❌ Recent projects list (UI shows it, but not populated)
- ❌ Project dashboard (shows project stats, file counts)
- ❌ Bundle pack editor (visual JSON editing)
- ❌ Settings panel (compression level, game directory)
- ❌ Help/documentation links in UI
- ❌ "Open GameFilesEdited folder" button
- ❌ "Open game directory" button

---

## Section 5: UI Requirements (What Should Be Fixed)

### Critical UI Improvements Needed

#### 1. Welcome Screen (First Launch)
```
┌─────────────────────────────────────────────────────────────┐
│                    Welcome to ModBuilder                     │
│                                                              │
│  ModBuilder is a build automation tool for C&C Generals mods│
│                                                              │
│  [Create Sample Project]  [Open Existing Project]           │
│                                                              │
│  Recent Projects:                                            │
│  • C:\Mods\MyMod\MyMod.mbproj                               │
│  • C:\Mods\TestMod\TestMod.mbproj                           │
└─────────────────────────────────────────────────────────────┘
```

#### 2. Project Dashboard (After Loading)
```
┌─────────────────────────────────────────────────────────────┐
│  Project: MyMod v1.0.0                                       │
│  Location: C:\Mods\MyMod\                                    │
│                                                              │
│  Quick Actions:                                              │
│  [Open GameFilesEdited Folder]  [Open Game Directory]       │
│  [Edit Configuration]            [View Build Cache]         │
│                                                              │
│  Project Stats:                                              │
│  • Source Files: 45 files (12.3 MB)                         │
│  • Last Build: 2 minutes ago                                │
│  • Build Cache: 43 files cached                             │
└─────────────────────────────────────────────────────────────┘
```

#### 3. Main Build UI (Current + Improvements)
```
┌─────────────────────────────────────────────────────────────┐
│  [New] [Open] [Save] [↻]  Project: MyMod v1.0.0            │
├─────────────────────────────────────────────────────────────┤
│  Bundle Packs          │  Build Output                      │
│  ☑ Core English       │  ┌─────────────────────────────┐  │
│  ☐ Core Arabic        │  │ [INFO] Loading...           │  │
│  ☑ Full English       │  │ [INFO] Processing...        │  │
│                        │  │ [SUCCESS] Complete!         │  │
│  Build Actions         │  └─────────────────────────────┘  │
│  ☑ Clean              │                                     │
│  ☑ Build              │  [Execute Build]  [Abort]          │
│  ☐ Release            │                                     │
│  ☑ Install            │  Progress: 45/100 files (45%)      │
│  ☑ Run Game           │  Stage: Converting textures...     │
│  ☐ Uninstall          │  Time: 3.2s elapsed               │
│                        │                                     │
│  Options               │  Quick Links:                      │
│  ☐ Verbose Logging    │  [Open Source Files]               │
│  ☑ Multi-Processing   │  [Open Build Output]               │
│  ☐ Print Config       │  [Open Game Directory]             │
└─────────────────────────────────────────────────────────────┘
```

#### 4. Error Display (When Crash Occurs)
```
┌─────────────────────────────────────────────────────────────┐
│  ❌ Build Failed                                             │
│                                                              │
│  Error: Configuration file not found                        │
│  File: C:\Mods\MyMod\Configs\ModBundleItems.json           │
│                                                              │
│  Possible Solutions:                                         │
│  • Check that Configs/ directory exists                     │
│  • Verify configuration files are present                   │
│  • Try creating a new project from template                 │
│                                                              │
│  [View Full Log]  [Open Project Folder]  [Close]           │
└─────────────────────────────────────────────────────────────┘
```

### Required UI Features

**1. Project Creation Wizard**
- Template selection (Empty, Basic Mod, Sample Project)
- Game directory selection
- Project name and location
- Auto-create directory structure

**2. File Browser Integration**
- "Open GameFilesEdited folder" button
- "Open .Build folder" button
- "Open game directory" button
- Shows file counts and sizes

**3. Configuration Editor**
- Visual JSON editor for bundle items
- Drag-and-drop file selection
- Conversion parameter UI (DXT format, mipmaps, etc.)

**4. Build Progress**
- Real-time file processing updates
- Progress bar with percentage
- Estimated time remaining
- Current stage indicator

**5. Error Handling**
- Friendly error messages
- Suggested solutions
- Stack trace in collapsible section
- "Report Bug" button

**6. Help System**
- Tooltips on all UI elements
- "What is this?" help buttons
- Link to user guide
- Sample project download

---

## Section 6: Crash Analysis

### Likely Crash Causes

Based on the investigation, the crash is likely caused by:

**1. Missing Configuration Files**
```csharp
// ConfigurationLoaderService.cs
var bundleItemsPath = Path.Combine(projectDir, "Configs", "ModBundleItems.json");
if (!File.Exists(bundleItemsPath))
{
    throw new FileNotFoundException($"Configuration file not found: {bundleItemsPath}");
}
```
**Fix**: Check for file existence and show friendly error

**2. Null Project Reference**
```csharp
// ModBuilderViewModel.cs - ExecuteBuildCommand
if (CurrentProject == null)
{
    // Crash: NullReferenceException
    var config = CurrentProject.Directories.Configs; // BOOM
}
```
**Fix**: Add null checks and disable Execute button when no project loaded

**3. Invalid Game Directory**
```csharp
// BuildEngineService.cs
var gameDir = project.GameDirectory;
if (!Directory.Exists(gameDir))
{
    throw new DirectoryNotFoundException($"Game directory not found: {gameDir}");
}
```
**Fix**: Validate game directory on project load

**4. Missing External Tools**
```csharp
// ImageConversionService.cs
var crunchPath = toolsConfig.Crunch.AbsExe;
if (!File.Exists(crunchPath))
{
    throw new FileNotFoundException($"Crunch tool not found: {crunchPath}");
}
```
**Fix**: Check tool availability and show warning

**5. Unhandled Async Exceptions**
```csharp
// ModBuilderViewModel.cs
[RelayCommand]
private async Task ExecuteBuildAsync()
{
    try
    {
        await _buildEngineService.BuildAsync(...); // Exception here
    }
    catch (Exception ex)
    {
        // NOT CAUGHT - crashes UI thread
        _logger.LogError(ex, "Build failed");
    }
}
```
**Fix**: Add try-catch and display error in UI

### How to Fix the Crash

**Immediate Fixes**:

1. **Add Global Exception Handler**
```csharp
// App.axaml.cs
public override void OnFrameworkInitializationCompleted()
{
    AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    // ...
}

private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
{
    var ex = e.ExceptionObject as Exception;
    _logger.LogCritical(ex, "Unhandled exception");
    ShowErrorDialog(ex);
}
```

2. **Add Null Checks in ViewModel**
```csharp
[RelayCommand(CanExecute = nameof(CanExecuteBuild))]
private async Task ExecuteBuildAsync()
{
    if (CurrentProject == null)
    {
        await ShowErrorAsync("No project loaded. Please open or create a project.");
        return;
    }

    try
    {
        IsBuildRunning = true;
        await _buildEngineService.BuildAsync(CurrentProject, ...);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Build failed");
        await ShowErrorAsync($"Build failed: {ex.Message}");
    }
    finally
    {
        IsBuildRunning = false;
    }
}

private bool CanExecuteBuild() => CurrentProject != null && !IsBuildRunning;
```

3. **Validate Project on Load**
```csharp
private async Task LoadProjectInternalAsync(string projectPath)
{
    var result = await _projectConfigService.LoadProjectAsync(projectPath);

    if (!result.Success)
    {
        await ShowErrorAsync($"Failed to load project: {result.FirstError}");
        return;
    }

    CurrentProject = result.Data;

    // Validate project structure
    var validation = await ValidateProjectAsync(CurrentProject);
    if (!validation.IsValid)
    {
        await ShowWarningAsync($"Project has issues:\n{string.Join("\n", validation.Errors)}");
    }
}
```

4. **Create Sample Project**
```csharp
// ProjectTemplates.cs
public static class ProjectTemplates
{
    public static async Task<ModBuilderProject> CreateSampleProjectAsync(string projectPath)
    {
        var project = new ModBuilderProject
        {
            Name = "SampleMod",
            Version = "1.0.0",
            Description = "A sample mod demonstrating ModBuilder features",
            // ...
        };

        // Create directory structure
        var projectDir = Path.GetDirectoryName(projectPath);
        Directory.CreateDirectory(Path.Combine(projectDir, "Configs"));
        Directory.CreateDirectory(Path.Combine(projectDir, "GameFilesEdited", "Data", "INI"));
        Directory.CreateDirectory(Path.Combine(projectDir, ".Build"));
        Directory.CreateDirectory(Path.Combine(projectDir, ".Release"));

        // Create sample configuration files
        await CreateSampleConfigsAsync(projectDir);

        // Create sample game files
        await CreateSampleGameFilesAsync(projectDir);

        return project;
    }
}
```

---

## Section 7: Summary and Action Plan

### What We Learned

1. **ModBuilder is NOT a file editor** - it's a build automation tool
2. **Users edit files externally** - in Photoshop, Notepad++, etc.
3. **ModBuilder processes and packages** - converts, compresses, archives
4. **Workflow is: Edit → Build → Test → Repeat**
5. **UI should guide this workflow** - not try to be a file manager

### Critical Problems

1. ❌ **No sample project** - User has nothing to test with
2. ❌ **Unclear workflow** - UI doesn't explain what to do
3. ❌ **Crash on execution** - Likely null reference or missing config
4. ❌ **No error handling** - Exceptions crash the app
5. ❌ **Missing quick actions** - Can't easily open folders

### Action Plan

**Phase 1: Fix Crashes (URGENT)**
1. Add global exception handler
2. Add null checks in ViewModel
3. Validate project on load
4. Show friendly error messages
5. Test with missing files/directories

**Phase 2: Add Sample Project (HIGH PRIORITY)**
1. Create ProjectTemplates class
2. Implement CreateSampleProjectAsync
3. Add "Create Sample Project" button
4. Include sample INI, texture, audio files
5. Include working configuration files

**Phase 3: Improve UI (MEDIUM PRIORITY)**
1. Add "Open GameFilesEdited folder" button
2. Add "Open game directory" button
3. Add project dashboard with stats
4. Add welcome screen for first launch
5. Add tooltips and help text

**Phase 4: Add Configuration Editor (LOW PRIORITY)**
1. Visual JSON editor for bundle items
2. Drag-and-drop file selection
3. Conversion parameter UI
4. Bundle pack editor dialog

### Files to Fix

**Immediate**:
- `/z/GeneralsHub/GenHub/GenHub/Features/Tools/ModBuilder/ViewModels/ModBuilderViewModel.cs`
  - Add null checks
  - Add error handling
  - Add CanExecute logic

- `/z/GeneralsHub/GenHub/GenHub/App.axaml.cs`
  - Add global exception handler

- `/z/GeneralsHub/GenHub/GenHub/Features/Tools/ModBuilder/Services/ProjectTemplates.cs` (NEW)
  - Create sample project generator

**Next**:
- `/z/GeneralsHub/GenHub/GenHub/Features/Tools/ModBuilder/Views/ModBuilderView.axaml`
  - Add "Open folder" buttons
  - Add help tooltips
  - Add error display panel

- `/z/GeneralsHub/GenHub/GenHub/Features/Tools/ModBuilder/Views/WelcomeScreen.axaml` (NEW)
  - Create welcome screen

---

## Conclusion

The ModBuilder C# port is **architecturally sound** but has **critical UX issues**:

1. **No sample project** - Users can't test without creating complex configuration
2. **Unclear workflow** - UI doesn't explain the edit-build-test cycle
3. **Poor error handling** - Crashes instead of showing friendly errors
4. **Missing quick actions** - Can't easily navigate to source files

**The fix is NOT to add file editing** - that's external. **The fix is to guide the workflow** and make it obvious where to edit files and how to build.

**Next Steps**: Fix crashes, add sample project, improve UI guidance.
