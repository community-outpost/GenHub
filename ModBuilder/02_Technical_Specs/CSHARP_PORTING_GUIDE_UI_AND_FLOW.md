# ModBuilder C# Porting Guide: User Interfaces and Program Flow

## Overview
This document provides a comprehensive analysis of the ModBuilder Python application's user-facing interfaces, entry points, and program flow for C# porting purposes. ModBuilder is a build automation tool for Command & Conquer Generals mods.

**Version**: 2.3
**Original Language**: Python 3
**Target Language**: C#

---

## 1. Application Entry Points

### 1.1 Main Entry Point: `buildproject.py`
**Location**: `Z:\ModBuilder\ModBuilder\buildproject.py`

**Purpose**: Build script for creating distributable packages using PyInstaller. This is NOT the main application entry point for end users.

**Key Functions**:
- Creates virtual environments for build process
- Installs Python packages (Poetry, PyInstaller)
- Runs PyInstaller to create executable
- Generates release archives (.7z, .zip)
- Generates hash files (MD5, SHA256, size)

**Command-Line Arguments**:
```
-b, --build-definition-file <path>  : Path to build definition JSON file
```

**C# Porting Notes**:
- This is a build/packaging script, not part of the core application
- May not need direct porting if using different packaging approach for C#
- Consider MSBuild, dotnet publish, or similar C# build tools
- Archive generation logic should be preserved

---

### 1.2 Application Entry Point: `generalsmodbuilder\main.py`
**Location**: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\main.py`

**Purpose**: Primary entry point for the ModBuilder application. Handles both CLI and GUI modes.

**Entry Function**: `Main(args=None)`

**Program Flow**:
```
Main()
  ├─> Parse command-line arguments
  ├─> Check for file hash registry generation (special mode)
  ├─> Validate that at least one action is specified
  ├─> Load configuration files (JSON)
  ├─> Branch: GUI mode or CLI mode
  │   ├─> GUI Mode: Create Gui instance and call RunWithConfig()
  │   └─> CLI Mode: Call RunWithConfig() directly
  └─> Exception handling (with user prompt on error)
```

---

## 2. Command-Line Interface (CLI)

### 2.1 Complete Argument List

#### Configuration Arguments
```
-c, --config <path>              : Path to configuration file (JSON). Can specify multiple times.
-l, --config-list <path> ...     : Paths to multiple configuration files (JSON).
--load-default-runner            : Load built-in runner JSON configuration.
--load-default-tools             : Load built-in tools JSON configuration.
--tools-root-dir <path>          : Root directory for tools.
```

#### Action Arguments (Build Pipeline)
```
-a, --clean                      : Clean build artifacts.
-b, --build                      : Build the mod.
-z, --release                    : Build release packages.
-i, --install [pack_name]        : Install specified bundle pack. Can specify multiple times.
-o, --install-list <names> ...   : Install multiple bundle packs by name.
-u, --uninstall                  : Uninstall the mod.
-r, --run                        : Run the game.
--build-pack [pack_name]         : Build only specified bundle pack. Can specify multiple times.
--build-pack-list <names> ...    : Build multiple bundle packs by name.
--make-change-log                : Generate change log documents.
```

#### Utility Arguments
```
--file-hash-registry-input <path>   : Path to generate file hash registry from. Can specify multiple times.
--file-hash-registry-output <path>  : Path to save file hash registry to.
--file-hash-registry-name <name>    : Name of the file hash registry (default: "FileHashRegistry").
```

#### Mode Arguments
```
-g, --gui                        : Launch GUI mode.
--debug                          : Enable debug mode (no exception catching).
--print-config                   : Print loaded configuration.
--verbose-logging                : Enable verbose logging output.
--multi-processing               : Enable multi-processing for parallel builds.
```

### 2.2 Argument Processing Logic

**Configuration Loading Order**:
1. Default runner configuration (if `--load-default-runner`)
2. Default tools configuration (if `--load-default-tools`)
3. Custom configurations from `--config-list`
4. Custom configurations from `--config` (multiple)

**Pack Name Lists**:
- Install list: Combines `--install-list` and `--install` arguments
- Build list: Combines `--build-pack-list` and `--build-pack` arguments
- Special value `"_default_"` used when no pack name specified

**Action Validation**:
- If no actions specified (build, release, install, uninstall, run, makeChangeLog), prints help and exits
- At least one action must be specified for the program to proceed

### 2.3 CLI Execution Flow

```
CLI Mode Execution:
  ├─> Wrap RunWithConfig() in exception handler (unless --debug)
  ├─> On exception:
  │   ├─> Print "ERROR CALLSTACK"
  │   ├─> Print stack trace
  │   └─> Wait for user input ("Press any key to continue...")
  └─> Exit
```

---

## 3. Graphical User Interface (GUI)

### 3.1 GUI Architecture
**Location**: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\gui\gui.py`

**Framework**: Tkinter (Python's standard GUI library)

**Threading Model**:
- Main thread: GUI event loop
- Work thread: Executes build operations
- Abort thread: Monitors abort state and updates UI

**Thread Synchronization**:
- `buildEngineLock`: Protects BuildEngine instance
- `mainWindowLock`: Protects GUI element access

### 3.2 GUI Window Specifications

**Window Properties**:
- Title: "Generals Mod Builder v{VERSION} by The Super Hackers"
- Size: 660x270 pixels
- Resizable: No
- Icon: `gui/icon.png`

**Layout**: 4-column grid layout
```
┌─────────────────────────────────────────────────────────────┐
│  Bundle Pack List  │  Sequence Execution  │  Single Actions  │  Options  │
└─────────────────────────────────────────────────────────────┘
```

### 3.3 GUI Components

#### Column 1: Bundle Pack List
```
- Label: "Bundle Pack list"
- Listbox: Multiple selection, 21 characters wide
  - Populated from configuration files
  - Pre-selects packs from command-line arguments
- Button: "Refresh" - Repopulates the bundle pack list
```

#### Column 2: Sequence Execution
```
- Label: "Sequence execution"
- Checkboxes (18 characters wide):
  ☐ Make Change Log
  ☐ Clean
  ☐ Build
  ☐ Build Release
  ☐ Install
  ☐ Run Game
  ☐ Uninstall
- Button: "Execute" (20 characters wide)
  - Executes all checked actions in sequence
```

#### Column 3: Single Actions
```
- Label: "Single actions"
- Buttons (20 characters wide):
  - "Make Change Log"
  - "Clean"
  - "Build"
  - "Build Release"
  - "Install"
  - "Run Game"
  - "Uninstall"
  - "Abort"
```

#### Column 4: Options
```
- Label: "Options"
- Checkboxes (18 characters wide):
  ☑ Auto Clear Console (default: checked)
  ☐ Print Config
  ☐ Verbose Logging
  ☐ Multi Processing
```

### 3.4 GUI State Management

**Button States**:
- Job buttons (Execute, Make Change Log, Clean, Build, etc.): Disabled during execution
- Abort button: Enabled only when BuildEngine.CanAbort() returns true
- Refresh button: Disabled during execution

**State Transitions**:
```
Idle State:
  - All job buttons: enabled
  - Abort button: disabled

Work Begin:
  - Clear console (if Auto Clear Console checked)
  - Disable all job buttons
  - Start abort monitoring thread
  - Create BuildEngine instance

Work End:
  - Shutdown BuildEngine
  - Enable all job buttons
  - Disable abort button
  - Join abort thread
```

### 3.5 GUI Threading Details

**Work Thread**:
- Created for each operation (Execute, Clean, Build, etc.)
- Calls `RunWithConfig()` with appropriate parameters
- Exception handling (unless debug mode)
- Joined when GUI window closes

**Abort Thread**:
- Polls BuildEngine.CanAbort() every 0.1 seconds
- Updates abort button state based on result
- Terminates when BuildEngine is set to None

### 3.6 GUI-to-Core Integration

**Data Flow**:
```
GUI → Core:
  - Selected bundle packs from listbox
  - Checkbox states (clean, build, release, etc.)
  - Option flags (printConfig, verboseLogging, multiProcessing)
  - Configuration paths (from initialization)

Core → GUI:
  - Console output (via print statements)
  - Abort capability status (via BuildEngine.CanAbort())
```

---

## 4. Core Build Functions

### 4.1 Main Build Function: `RunWithConfig()`
**Location**: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\buildfunctions.py`

**Signature**:
```python
def RunWithConfig(
    configPaths: list[str] = list[str](),
    installList: list[str] = list[str](),
    buildList: list[str] = list[str](),
    makeChangeLog: bool = False,
    clean: bool = False,
    build: bool = False,
    release: bool = False,
    install: bool = False,
    uninstall: bool = False,
    run: bool = False,
    printConfig: bool = False,
    verboseLogging: bool = False,
    multiProcessing: bool = False,
    toolsRootDir: str = None,
    engine: BuildEngine = None
) -> None
```

**Execution Flow**:
```
RunWithConfig()
  ├─> Start timer
  ├─> Reset file hash count
  ├─> Load JSON configuration files
  ├─> Create BuildStep flags from action parameters
  │
  ├─> If makeChangeLog:
  │   ├─> Load change configuration
  │   ├─> Parse change log
  │   ├─> Filter and sort changes
  │   └─> Generate change log documents
  │
  └─> If buildStep != Zero:
      ├─> Load folders configuration
      ├─> Load runner configuration (if install/uninstall/run)
      ├─> Load bundles configuration
      ├─> Load tools configuration
      ├─> Install tools
      ├─> Patch bundle install/build flags based on lists
      ├─> Create BuildSetup
      ├─> Create or use provided BuildEngine
      └─> Execute BuildEngine.Run(setup)
```

### 4.2 BuildStep Enumeration

**Location**: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\build\setup.py`

```python
class BuildStep(Flag):
    Zero = 0
    PreBuild = auto()
    Clean = auto()
    Build = auto()
    PostBuild = auto()
    Release = auto()
    Install = auto()
    Run = auto()
    Uninstall = auto()
```

**Usage**: Bitwise flags combined to specify build pipeline stages.

### 4.3 BuildSetup Data Class

**Fields**:
- `step`: BuildStep flags
- `folders`: Folders configuration
- `runner`: Runner configuration
- `bundles`: Bundles configuration
- `tools`: Tools dictionary
- `printConfig`: Print configuration flag
- `verboseLogging`: Verbose logging flag
- `multiProcessing`: Multi-processing flag

---

## 5. Configuration System

### 5.1 Configuration File Types

**JSON Files**:
- Primary configuration format
- Loaded via `util.JsonFile` class
- Validated for type correctness

**YAML Files** (optional):
- Supported via `util.YamlFile` class
- Requires PyYAML library

### 5.2 Configuration Categories

1. **Build Files** (`buildfiles.py`):
   - Additional configuration files to load
   - Recursive loading support

2. **Bundles** (`bundles.py`):
   - Bundle packs (collections of items)
   - Bundle items (collections of files)
   - Bundle files (individual files to build)
   - Bundle events (pre/post build actions)
   - Registry definitions

3. **Folders** (`folders.py`):
   - Source directories
   - Build directories
   - Output directories

4. **Runner** (`runner.py`):
   - Game executable path
   - Launch parameters

5. **Tools** (`tools.py`):
   - External tool definitions (crunch, gametextcompiler, etc.)
   - Tool installation instructions
   - Tool call parameters

6. **Change Config** (`changeconfig.py`):
   - Change log generation settings
   - Sorting and filtering rules

### 5.3 Default Configurations

**Location**: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\config\`

- `DefaultRunner.json`: Default game runner configuration
- `DefaultTools.json`: Default tool definitions

**Loading**: Enabled via `--load-default-runner` and `--load-default-tools` flags.

---

## 6. Logging and User Feedback

### 6.1 Console Output

**Standard Output**:
- All user feedback via `print()` statements
- No logging framework used
- Direct console output

**Output Categories**:
1. **Informational**: Operation progress, file operations
2. **Warnings**: Missing tool definitions, configuration issues
3. **Errors**: Exception stack traces
4. **Performance**: Operation timing (if > 0.01 seconds)

### 6.2 Logging Patterns

**File Operations**:
```python
print(f"Read json {path} ...")
print(f"Write pickle {path} ...")
print(f"Delete '{file}'")
print(f"chdir '{dir}'")
```

**Build Operations**:
```python
print(f"Run Build Job ...")
print(f"Build Job completed in {elapsed} s")
print(f"Hashed ({count}) {path} as {hash} in {elapsed} s")
```

**Registry Operations**:
```python
print(f"Get registry key {path} : {name} as '{value}'")
print(f"Set registry key {path} : {name} to '{value}'")
```

### 6.3 Verbose Logging

**Control**: `--verbose-logging` flag or GUI checkbox

**Effect**: Enables additional logging in BuildEngine (details in engine.py)

### 6.4 Performance Timing

**Threshold**: `PERFORMANCE_TIMER_THRESHOLD = 0.01` seconds

**Usage**: Operations exceeding threshold print elapsed time.

---

## 7. Error Handling

### 7.1 Exception Handling Strategy

**CLI Mode (Non-Debug)**:
```python
try:
    RunWithConfig(...)
except Exception:
    print("ERROR CALLSTACK")
    traceback.print_exc()
    input("Press any key to continue...")
```

**GUI Mode (Non-Debug)**:
```python
try:
    function()
except Exception:
    print("ERROR CALLSTACK")
    traceback.print_exc()
```

**Debug Mode**:
- No exception catching
- Allows debugger to catch exceptions

### 7.2 Validation Functions

**Location**: `util.py`

```python
def Verify(condition: bool, message: str = "") -> None:
    """Raises AssertionError if condition is False"""

def VerifyType(obj: object, expectedType: type, objName: str) -> None:
    """Raises AssertionError if obj is not of expectedType"""
```

**Usage**: Extensive type and value validation throughout codebase.

---

## 8. Utility Functions

### 8.1 Version Management
**Location**: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\__version__.py`

```python
VERSION = (2, 3)
VERSIONSTR = '.'.join(map(str, VERSION))
```

**Usage**: Displayed in window titles, console output, archive names.

### 8.2 File Operations (`util.py`)

**Path Operations**:
- `GetAbsFileDir(file)`: Get absolute directory of file
- `GetAbsSmartFileDir(file)`: Handle frozen (PyInstaller) paths
- `GetFileName(filepath)`: Extract filename
- `GetFileNameNoExt(filepath)`: Extract filename without extension
- `GetFileExt(filepath)`: Extract file extension
- `HasFileExt(file, ext)`: Check file extension

**File System Operations**:
- `DeleteFile(path)`: Delete file or symlink
- `DeleteFileOrDir(path)`: Delete file, symlink, or directory tree
- `DeleteDir(path)`: Delete directory tree
- `DeleteEmptyDir(path)`: Delete empty directory
- `MakeDirsForFile(file)`: Create parent directories

**File Hashing**:
- `GetFileMd5(path)`: Calculate MD5 hash
- `GetFileSha256(path)`: Calculate SHA256 hash
- `GetFileSize(path)`: Get file size
- `GetFileModifiedTime(path)`: Get modification timestamp

**Serialization**:
- `LoadPickle(path)`: Load Python pickle file
- `SavePickle(path, data)`: Save Python pickle file
- `ReadJson(path)`: Load JSON file
- `ReadYaml(path)`: Load YAML file

### 8.3 Registry Operations (Windows Only)

```python
def GetRegKeyValue(path, root=winreg.HKEY_LOCAL_MACHINE) -> Union[int, str, None]
def SetRegKeyValue(path: str, value: Union[int, str], root=..., regtype=...) -> bool
```

**Usage**: Read/write Windows registry for game installation paths.

### 8.4 Timer Class

```python
class Timer:
    def Start(self) -> None
    def Finish(self) -> None
    def GetElapsedSeconds(self) -> float
    def GetElapsedSecondsString(self) -> str
```

**Usage**: Performance measurement throughout application.

---

## 9. Build Engine Overview

### 9.1 BuildEngine Class
**Location**: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\build\engine.py`

**Key Methods**:
- `Run(setup: BuildSetup)`: Execute build pipeline
- `CanAbort() -> bool`: Check if abort is possible
- `Abort()`: Request abort
- `Shutdown()`: Clean shutdown

**Threading**:
- Supports multi-processing via ProcessPoolExecutor
- Abort monitoring via threading

### 9.2 Build Pipeline Stages

**Execution Order**:
1. PreBuild
2. Clean
3. Build
4. PostBuild
5. Release
6. Install
7. Run
8. Uninstall

**Stage Control**: Via BuildStep flags in BuildSetup.

---

## 10. C# Porting Recommendations

### 10.1 Entry Point Structure

**Recommended Approach**:
```csharp
// Program.cs
class Program
{
    static void Main(string[] args)
    {
        var version = new Version(2, 3);
        Console.WriteLine($"Generals Mod Builder v{version} by The Super Hackers");

        var parser = new CommandLineParser();
        var options = parser.Parse(args);

        if (options.FileHashRegistryMode)
        {
            BuildFileHashRegistry(options);
            return;
        }

        if (options.UseGui)
        {
            var gui = new ModBuilderGui();
            gui.RunWithConfig(options);
        }
        else
        {
            RunWithConfig(options);
        }
    }
}
```

### 10.2 GUI Framework Options

**Recommended**: WPF (Windows Presentation Foundation)
- Native Windows look and feel
- MVVM pattern support
- Better threading model than WinForms

**Alternative**: Windows Forms
- Closer to Tkinter structure
- Simpler porting
- Less modern UI capabilities

**Cross-Platform**: Avalonia UI
- If cross-platform support needed
- XAML-based like WPF

### 10.3 Command-Line Parsing

**Recommended Library**: CommandLineParser (NuGet)
```csharp
using CommandLine;

[Verb("build", HelpText = "Build the mod")]
class BuildOptions
{
    [Option('c', "config", Required = false, HelpText = "Configuration files")]
    public IEnumerable<string> ConfigFiles { get; set; }

    [Option('b', "build", Required = false, HelpText = "Build the mod")]
    public bool Build { get; set; }

    // ... other options
}
```

### 10.4 Configuration System

**Recommended**: System.Text.Json or Newtonsoft.Json
```csharp
using System.Text.Json;

public class JsonFile
{
    public string Path { get; set; }
    public Dictionary<string, object> Data { get; set; }

    public JsonFile(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        var json = File.ReadAllText(path);
        Data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
    }
}
```

### 10.5 Logging System

**Recommended**: Serilog or NLog
```csharp
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

Log.Information("Read json {Path} ...", path);
```

### 10.6 Threading Model

**GUI Threading**:
```csharp
// WPF approach
private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
{
    DisableJobButtons();

    await Task.Run(() => {
        RunWithConfig(options);
    });

    EnableJobButtons();
}
```

**Abort Monitoring**:
```csharp
private CancellationTokenSource _abortTokenSource;

private async Task MonitorAbortState()
{
    while (!_abortTokenSource.Token.IsCancellationRequested)
    {
        var canAbort = _buildEngine?.CanAbort() ?? false;
        Dispatcher.Invoke(() => AbortButton.IsEnabled = canAbort);
        await Task.Delay(100);
    }
}
```

### 10.7 File Operations

**Use System.IO**:
```csharp
public static string GetFileMd5(string path)
{
    using var md5 = MD5.Create();
    using var stream = File.OpenRead(path);
    var hash = md5.ComputeHash(stream);
    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
}
```

### 10.8 Error Handling

**Structured Exception Handling**:
```csharp
try
{
    RunWithConfig(options);
}
catch (Exception ex)
{
    Console.WriteLine("ERROR CALLSTACK");
    Console.WriteLine(ex.ToString());

    if (!options.Debug)
    {
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();
    }
}
```

---

## 11. Key Differences: CLI vs GUI Mode

### 11.1 Initialization

**CLI Mode**:
- Direct parameter passing
- Immediate execution
- Single-threaded (unless multi-processing enabled)

**GUI Mode**:
- Parameters stored in GUI instance
- User-triggered execution
- Multi-threaded (work thread + abort thread)

### 11.2 User Interaction

**CLI Mode**:
- No interaction during execution
- Error prompt at end (if exception)
- Exit after completion

**GUI Mode**:
- Bundle pack selection via listbox
- Action selection via checkboxes/buttons
- Abort capability during execution
- Window remains open for multiple operations

### 11.3 Output

**CLI Mode**:
- Console output only
- No console clearing

**GUI Mode**:
- Console output (same as CLI)
- Optional auto-clear console
- UI state updates (button enable/disable)

### 11.4 Exception Handling

**CLI Mode**:
- Catches exceptions (unless debug)
- Prints stack trace
- Waits for user input
- Exits

**GUI Mode**:
- Catches exceptions (unless debug)
- Prints stack trace
- Returns to idle state
- Window remains open

---

## 12. Integration Points

### 12.1 UI → Core

**Data Passed to Core**:
1. Configuration file paths
2. Bundle pack names (install/build lists)
3. Action flags (clean, build, release, etc.)
4. Option flags (printConfig, verboseLogging, multiProcessing)
5. Tool root directory
6. BuildEngine instance (GUI mode only)

### 12.2 Core → UI

**Feedback to UI**:
1. Console output (via print statements)
2. Abort capability status (via BuildEngine.CanAbort())
3. Completion status (via exception or normal return)

### 12.3 Shared State

**BuildEngine**:
- Created by GUI, passed to core
- Allows abort from GUI thread
- Shared via lock (buildEngineLock)

**Bundle Pack List**:
- Populated from configuration
- Selected by user (GUI) or command-line (CLI)
- Passed to core for processing

---

## 13. Special Modes

### 13.1 File Hash Registry Generation

**Trigger**: `--file-hash-registry-input` and `--file-hash-registry-output` specified

**Behavior**:
- Bypasses normal build pipeline
- Generates hash registry from input paths
- Saves to output path
- Exits immediately

**Function**: `BuildFileHashRegistry()`

### 13.2 Debug Mode

**Trigger**: `--debug` flag

**Behavior**:
- Disables exception catching
- Allows debugger to catch exceptions
- No user prompt on error

### 13.3 Change Log Generation

**Trigger**: `--make-change-log` flag

**Behavior**:
- Loads change configuration
- Parses change log sources
- Filters and sorts changes
- Generates output documents
- Can run independently or with build pipeline

---

## 14. Application Lifecycle

### 14.1 CLI Mode Lifecycle

```
Start
  ├─> Parse arguments
  ├─> Validate arguments
  ├─> Load configurations
  ├─> Execute RunWithConfig()
  ├─> Print completion message
  └─> Exit
```

### 14.2 GUI Mode Lifecycle

```
Start
  ├─> Parse arguments
  ├─> Create GUI window
  ├─> Initialize GUI elements
  ├─> Populate bundle pack list (work thread)
  ├─> Enter event loop
  │   ├─> User interaction
  │   ├─> Execute operations (work thread)
  │   └─> Monitor abort state (abort thread)
  ├─> User closes window
  ├─> Join work thread
  └─> Exit
```

### 14.3 Work Thread Lifecycle (GUI)

```
Work Thread Start
  ├─> OnWorkBegin()
  │   ├─> Create BuildEngine
  │   ├─> Get selected bundle packs
  │   ├─> Clear console (if enabled)
  │   ├─> Disable job buttons
  │   └─> Start abort thread
  ├─> Execute operation
  │   └─> Call RunWithConfig()
  └─> OnWorkEnd()
      ├─> Shutdown BuildEngine
      ├─> Join abort thread
      └─> Enable job buttons
```

---

## 15. Summary for C# Developers

### 15.1 Core Architecture

**Pattern**: Command-line tool with optional GUI wrapper
- CLI: Direct execution, single-threaded
- GUI: Event-driven, multi-threaded

**Configuration**: JSON-based, hierarchical loading
- Default configurations
- Custom configurations
- Merge and override semantics

**Build Pipeline**: Flag-based stage execution
- Bitwise flags for stage selection
- Sequential execution
- Abort capability

### 15.2 Key Classes to Port

1. **Main Entry**: `main.py` → `Program.cs`
2. **GUI**: `gui.py` → `MainWindow.xaml` + `MainWindow.xaml.cs`
3. **Build Functions**: `buildfunctions.py` → `BuildFunctions.cs`
4. **Build Engine**: `engine.py` → `BuildEngine.cs`
5. **Utilities**: `util.py` → `Utilities.cs`
6. **Version**: `__version__.py` → `Version.cs` or `AssemblyInfo.cs`

### 15.3 Technology Mapping

| Python | C# |
|--------|-----|
| argparse | CommandLineParser (NuGet) |
| tkinter | WPF / Windows Forms |
| threading | System.Threading.Tasks |
| json | System.Text.Json |
| pickle | BinaryFormatter / Protobuf |
| hashlib | System.Security.Cryptography |
| subprocess | System.Diagnostics.Process |
| winreg | Microsoft.Win32.Registry |

### 15.4 Critical Considerations

1. **Threading**: Python GIL vs C# true multithreading
2. **Exception Handling**: Python's broad exceptions vs C#'s typed exceptions
3. **Path Handling**: Python's os.path vs C#'s System.IO.Path
4. **Type System**: Python's dynamic typing vs C#'s static typing
5. **GUI Threading**: Tkinter's simplicity vs WPF's Dispatcher model

---

## 16. Appendix: File Locations

### 16.1 Core Files
- Entry point: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\main.py`
- GUI: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\gui\gui.py`
- Build functions: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\buildfunctions.py`
- Utilities: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\util.py`
- Version: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\__version__.py`

### 16.2 Build System
- Engine: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\build\engine.py`
- Setup: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\build\setup.py`
- Copy: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\build\copy.py`
- Thing: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\build\thing.py`
- File hash registry: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\build\filehashregistry.py`

### 16.3 Data Structures
- Bundles: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\data\bundles.py`
- Folders: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\data\folders.py`
- Runner: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\data\runner.py`
- Tools: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\data\tools.py`
- Build files: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\data\buildfiles.py`
- Change config: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\data\changeconfig.py`

### 16.4 Configuration
- Default runner: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\config\DefaultRunner.json`
- Default tools: `Z:\ModBuilder\ModBuilder\generalsmodbuilder\config\DefaultTools.json`
t
