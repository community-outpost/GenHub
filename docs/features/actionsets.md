# ActionSet Fixes

This document provides comprehensive documentation for all ActionSet fixes available in GenHub for Command & Conquer: Generals and Zero Hour.

## Overview

ActionSets are automated fixes that resolve common issues with Command & Conquer: Generals and Zero Hour on modern Windows systems. Each fix addresses specific compatibility, performance, or functionality problems.

## Critical Fixes

These fixes are essential for the games to run properly on modern Windows systems.

### BrowserEngineFix

**Purpose**: Fixes in-game browser compatibility issues by disabling the problematic BrowserEngine.dll.

**What It Does**:

- Renames `BrowserEngine.dll` to `BrowserEngine.dll.bak` in game directories
- Prevents crashes and errors caused by outdated browser components
- Applies to both Generals and Zero Hour

**How It Works**:

1. Checks if `BrowserEngine.dll` exists in game installation directories
2. If found, renames it to `.bak` extension to disable it
3. The game will run without the browser engine (which is rarely used)

**Files Modified**:

- `{GeneralsPath}\BrowserEngine.dll` → `BrowserEngine.dll.bak`
- `{ZeroHourPath}\BrowserEngine.dll` → `BrowserEngine.dll.bak`

**Reversible**: Yes - can restore by renaming `.bak` back to `.dll`

---

### DbgHelpFix

**Purpose**: Replaces outdated `dbghelp.dll` files that can cause crashes and debugging issues.

**What It Does**:

- Replaces `dbghelp.dll` in both Generals and Zero Hour directories
- Uses a modern version compatible with Windows 10/11
- Prevents crashes during error reporting and debugging

**How It Works**:

1. Checks for existing `dbghelp.dll` in game directories
2. Backs up original file to `.bak`
3. Copies a modern `dbghelp.dll` from embedded resources
4. Verifies the replacement was successful

**Files Modified**:

- `{GeneralsPath}\dbghelp.dll` (replaced, original backed up)
- `{ZeroHourPath}\dbghelp.dll` (replaced, original backed up)

**Reversible**: Yes - can restore from `.bak` backup

---

### EAAppRegistryFix

**Purpose**: Ensures EA App can properly detect game installations.

**What It Does**:

- Creates or updates registry entries for EA App detection
- Sets correct installation paths for Generals and Zero Hour
- Enables EA App integration features

**How It Works**:

1. Checks if EA App is installed
2. Creates registry keys under `HKLM\SOFTWARE\EA Games\`
3. Sets `InstallPath` values for both games
4. Sets version information for proper detection

**Registry Keys Created/Modified**:

- `HKLM\SOFTWARE\EA Games\Command and Conquer Generals\InstallPath`
- `HKLM\SOFTWARE\EA Games\Command and Conquer Generals Zero Hour\InstallPath`

**Reversible**: Yes - registry keys can be deleted

---

### MyDocumentsPathCompatibility

**Purpose**: Ensures game data folders exist in Documents directory, even with non-English characters in path.

**What It Does**:

- Creates required game data folders in Documents
- Handles paths with Unicode/non-English characters
- Ensures proper folder structure for saves and settings

**How It Works**:

1. Locates Documents folder using Windows API
2. Creates `Command and Conquer Generals Data` folder if missing
3. Creates `Command and Conquer Generals Zero Hour Data` folder if missing
4. Creates subdirectories for saves, replays, and maps

**Folders Created**:

- `{Documents}\Command and Conquer Generals Data\`
- `{Documents}\Command and Conquer Generals Zero Hour Data\`
- Subdirectories: `Save`, `Replays`, `Maps`

**Reversible**: No - folders are created but not deleted

---

### VCRedist2010Fix

**Purpose**: Installs Visual C++ 2010 Redistributable required by the game.

**What It Does**:

- Downloads and installs Visual C++ 2010 Redistributable
- Ensures required runtime libraries are present
- Fixes "MSVCR100.dll missing" errors

**How It Works**:

1. Checks if VC++ 2010 Redistributable is already installed
2. If not installed, downloads installer from Microsoft
3. Runs installer silently with administrator privileges
4. Verifies installation by checking for required DLLs

**Files Installed**:

- `msvcr100.dll`, `msvcp100.dll` (and variants)
- Installed to System32 and SysWOW64 directories

**Reversible**: No - can be uninstalled through Windows Programs & Features

---

### RemoveReadOnlyFix

**Purpose**: Removes the read-only attribute from game files and ensures they are not "Pinned" in OneDrive.
**What It Does**:

- Iterates through all files in game installation directories
- Removes read-only attribute using Windows API
- Applies OneDrive "Pinned" attribute to prevent syncing
- Ensures game files can be modified and saved properly

**How It Works**:

1. Iterates through all files in game installation directories
2. Removes read-only attribute using Windows API
3. Checks if files are in a OneDrive-managed folder
4. If so, applies `FILE_ATTRIBUTE_PINNED` using `SetFileAttributes`
5. This forces OneDrive to keep a local copy and allow game access

Processes both Generals and Zero Hour installations

**Files Modified**:

- All files in `{GeneralsPath}` (read-only attribute removed)
- All files in `{GeneralsPath}` and `{ZeroHourPath}`
**Reversible**: Partially - read-only attributes are removed, but "Pinned" state remains

---

### AppCompatConfigurationsFix

**Purpose**: Sets Windows compatibility flags and adds Windows Defender exclusions for better performance.

**What It Does**:

- Enables High DPI awareness for proper scaling on modern displays
- Sets Run as Administrator compatibility for non-Steam installations
- Adds Windows Defender exclusions to prevent scanning interference
- Improves game performance and stability

**How It Works**:

1. Checks if game is installed via Steam
2. For Steam: Sets `~ HIGHDPIAWARE` compatibility flag
3. For other installations: Sets `~ RUNASADMIN HIGHDPIAWARE` flags
4. Adds game directories to Windows Defender exclusion list
5. Uses PowerShell `Add-MpPreference` command

**Registry Keys Created/Modified**:

- `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers\{GameExePath}`

**Windows Defender Exclusions Added**:

- `{GeneralsPath}` (directory exclusion)
- `{ZeroHourPath}` (directory exclusion)

**Reversible**: Yes - registry keys and exclusions can be removed

---

### DirectXRuntimeFix

**Purpose**: Installs DirectX 8.1 and 9.0c runtime components required by the game.

**What It Does**:

- Downloads DirectX runtime installer
- Installs missing DirectX components
- Ensures proper 3D rendering and graphics functionality

**How It Works**:

1. Downloads DirectX runtime from official source
2. Extracts to temporary directory
3. Runs `DXSETUP.exe /silent` with administrator privileges
4. Verifies installation by checking for `D3DX9_43.dll` in SysWOW64

**Files Installed**:

- DirectX 8.1 and 9.0c runtime components
- Installed to System32 and SysWOW64 directories

**Reversible**: No - DirectX components can be uninstalled through Windows Features

---

### Patch104Fix

**Purpose**: Installs Zero Hour 1.04 official patch.

**What It Does**:

- Downloads Zero Hour 1.04 patch
- Applies patch to Zero Hour installation
- Updates game to latest official version

**How It Works**:

1. Downloads patch from official source
2. Extracts patch files to temporary directory
3. Copies files to Zero Hour installation directory
4. Verifies installation by checking `game.exe` version (should start with "1.4")

**Files Modified**:

- All files in `{ZeroHourPath}` updated to 1.04 versions
- `game.exe` version updated to 1.04

**Reversible**: No - official patch cannot be easily reverted

---

### Patch108Fix

**Purpose**: Installs Generals 1.08 official patch.

**What It Does**:

- Downloads Generals 1.08 patch
- Applies patch to Generals installation
- Updates game to latest official version

**How It Works**:

1. Downloads patch from official source
2. Extracts patch files to temporary directory
3. Copies files to Generals installation directory
4. Verifies installation by checking `generals.exe` version (should start with "1.8")

**Files Modified**:

- All files in `{GeneralsPath}` updated to 1.08 versions
- `generals.exe` version updated to 1.08

**Reversible**: No - official patch cannot be easily reverted

---

### OptionsINIFix

**Purpose**: Ensures optimal game settings in Options.ini for better performance and compatibility.

**What It Does**:

- Applies optimal settings to Options.ini files
- Improves performance and visual quality
- Ensures proper resolution and graphics settings

**How It Works**:

1. Loads Options.ini from game data folder in Documents
2. Applies optimal settings if not already set
3. Saves modified Options.ini
4. Works for both Generals and Zero Hour

**Settings Applied**:

- `DynamicLOD = no` (disables dynamic level of detail)
- `ExtraAnimations = yes` (enables extra animations)
- `HeatEffects = no` (disables heat effects for performance)
- `MaxParticleCount = 1000` (sets maximum particle count)
- `SendDelay = no` (disables send delay for better multiplayer)
- `ShowSoftWaterEdge = yes` (enables soft water edges)
- `ShowTrees = yes` (enables tree rendering)
- Resolution set to optimal value (avoids low resolutions)

**Files Modified**:

- `{Documents}\Command and Conquer Generals Data\Options.ini`
- `{Documents}\Command and Conquer Generals Zero Hour Data\Options.ini`

**Reversible**: Yes - original values can be restored from backup

---

### VanillaExecutableFix

**Purpose**: Verifies that Generals 1.08 patch is properly applied.

**What It Does**:

- Checks `generals.exe` file version
- Confirms 1.08 patch is installed
- Provides status information

**How It Works**:

1. Uses `FileVersionInfo.GetVersionInfo()` to read executable version
2. Checks if version starts with "1.8" (indicating 1.08)
3. Returns status indicating if patch is applied
4. Only applicable for Generals installations

**Files Checked**:

- `{GeneralsPath}\generals.exe` (version check only)

**Reversible**: N/A - informational check only

---

### ZeroHourExecutableFix

**Purpose**: Verifies that Zero Hour 1.04 patch is properly applied.

**What It Does**:

- Checks `game.exe` file version
- Confirms 1.04 patch is installed
- Provides status information

**How It Works**:

1. Uses `FileVersionInfo.GetVersionInfo()` to read executable version
2. Checks if version starts with "1.4" (indicating 1.04)
3. Returns status indicating if patch is applied
4. Only applicable for Zero Hour installations

**Files Checked**:

- `{ZeroHourPath}\game.exe` (version check only)

**Reversible**: N/A - informational check only

---

## Important Compatibility Fixes

These fixes improve compatibility with Windows features and third-party software.

### OneDriveFix

**Purpose**: Prevents OneDrive from syncing game folders to avoid conflicts and performance issues.

**What It Does**:

- Creates `desktop.ini` files with `ThisPCPolicy=DisableCloudSync`
- Marks folders to prevent OneDrive synchronization
- Ensures game files remain local

**How It Works**:

1. Creates `desktop.ini` in game installation and user data folders
2. Sets `ThisPCPolicy=DisableCloudSync` to disable OneDrive sync
3. Marks `desktop.ini` as hidden and system file
4. Marks folder as system folder (read-only bit indicates system folder)
5. Processes both Generals and Zero Hour installations

**Files Created**:

- `{GeneralsPath}\desktop.ini`
- `{ZeroHourPath}\desktop.ini`
- `{Documents}\Command and Conquer Generals Data\desktop.ini`
- `{Documents}\Command and Conquer Generals Zero Hour Data\desktop.ini`

**Reversible**: Yes - `desktop.ini` files can be deleted

---

### EdgeScrollerFix

**Purpose**: Improves edge scrolling for modern high-resolution displays.

**What It Does**:

- Adjusts edge scrolling sensitivity in Options.ini
- Makes edge scrolling more responsive on large monitors
- Improves gameplay experience with modern displays

**How It Works**:

1. Loads Options.ini from game data folder
2. Sets optimal edge scrolling values if not already configured
3. Saves modified Options.ini
4. Works for both Generals and Zero Hour

**Settings Applied**:

- `ScrollEdgeZone = 0.1` (edge detection zone size, range: 0.05-0.15)
- `ScrollEdgeSpeed = 1.5` (scrolling speed, range: 1.0-2.0)
- `ScrollEdgeAcceleration = 1.0` (scrolling acceleration)

**Files Modified**:

- `{Documents}\Command and Conquer Generals Data\Options.ini`
- `{Documents}\Command and Conquer Generals Zero Hour Data\Options.ini`

**Reversible**: Yes - original values can be restored

---

### TheFirstDecadeRegistryFix

**Purpose**: Creates registry entries for The First Decade (TFD) version detection.

**What It Does**:

- Enables proper detection of TFD installations
- Sets TFD registry keys with correct paths
- Ensures compatibility with TFD version of the games

**How It Works**:

1. Detects TFD installation path by examining directory structure
2. Navigates up from game installation to find TFD root directory
3. Creates registry entries in `HKLM\SOFTWARE\EA Games\Command & Conquer The First Decade`
4. Sets `InstallPath` to TFD base directory
5. Sets `Version` to "1.03"

**Registry Keys Created**:

- `HKLM\SOFTWARE\EA Games\Command & Conquer The First Decade\InstallPath`
- `HKLM\SOFTWARE\EA Games\Command & Conquer The First Decade\Version`

**Reversible**: Yes - registry keys can be deleted

---

### CNCOnlineRegistryFix

**Purpose**: Creates registry entries for C&C Online (Revora) multiplayer service.

**What It Does**:

- Enables proper detection and connection to C&C Online servers
- Creates game-specific registry entries
- Supports multiplayer functionality through C&C Online

**How It Works**:

1. Creates registry entries in `HKLM\SOFTWARE\Revora\CNCOnline`
2. Creates game-specific entries for Generals and Zero Hour
3. Sets `InstallPath` for each game installation
4. Sets `Version` (1.08 for Generals, 1.04 for Zero Hour)
5. Creates main C&C Online entry with base installation path

**Registry Keys Created**:

- `HKLM\SOFTWARE\Revora\CNCOnline\InstallPath`
- `HKLM\SOFTWARE\Revora\CNCOnline\Generals\InstallPath`
- `HKLM\SOFTWARE\Revora\CNCOnline\Generals\Version`
- `HKLM\SOFTWARE\Revora\CNCOnline\ZeroHour\InstallPath`
- `HKLM\SOFTWARE\Revora\CNCOnline\ZeroHour\Version`

**Reversible**: Yes - registry keys can be deleted

---

## Optional Enhancement Fixes

These fixes provide additional improvements and guidance but are not essential for basic functionality.

### MalwarebytesFix

**Purpose**: Provides Malwarebytes compatibility guidance to prevent interference with game execution.

**What It Does**:

- Checks for Malwarebytes installation
- Provides step-by-step instructions to add game folders to exclusions
- Lists all game installation paths that should be excluded

**How It Works**:

1. Checks registry and file system for Malwarebytes installation
2. If installed, provides detailed instructions for adding exclusions
3. Lists all game installation paths to exclude
4. Explains how to access Malwarebytes exclusion settings

**User Action Required**:

- Open Malwarebytes
- Go to Settings > Exclusions
- Add game installation folders to exclusions list

**Reversible**: N/A - informational fix only

---

### D3D8XDLLCheck

**Purpose**: Checks for DirectX 8 DLLs required by the game and provides guidance if missing.

**What It Does**:

- Verifies presence of required DirectX 8 DLLs
- Lists any missing DLLs
- Provides guidance to install missing components

**How It Works**:

1. Checks System32 and SysWOW64 directories for required DLLs
2. Lists all missing DLLs if any are not found
3. Provides guidance to run DirectXRuntimeFix
4. Checks for critical DLLs: d3d8.dll, d3dx8d.dll, d3dx9_43.dll, etc.

**DLLs Checked**:

- `d3d8.dll`
- `d3dx8d.dll`
- `d3dx9_43.dll`
- Other DirectX 8/9 runtime DLLs

**User Action Required**:

- Run DirectXRuntimeFix if DLLs are missing
- Or manually install DirectX runtime

**Reversible**: N/A - informational fix only

---

### NahimicFix

**Purpose**: Provides Nahimic audio compatibility guidance to prevent audio issues.

**What It Does**:

- Checks for Nahimic audio driver installation
- Provides instructions to disable Nahimic service
- Explains potential audio conflicts

**How It Works**:

1. Checks registry and running processes for Nahimic
2. If installed, provides step-by-step instructions
3. Lists multiple methods to disable the service
4. Explains that Nahimic can cause audio issues with older games

**User Action Required**:

- Disable Nahimic service via Task Manager or Services
- Or uninstall Nahimic audio driver

**Reversible**: N/A - informational fix only

---

### DisableOriginInGame

**Purpose**: Disables Origin in-game overlay to prevent performance issues and conflicts.

**What It Does**:

- Checks for Origin installation
- Checks Origin configuration for overlay status
- Provides instructions to disable overlay

**How It Works**:

1. Checks registry and processes for Origin installation
2. Checks Origin.ini configuration file for overlay setting
3. Provides step-by-step instructions to disable overlay
4. Explains how to disable overlay per-game

**User Action Required**:

- Open Origin client
- Go to Application Settings > Origin In-Game
- Uncheck "Enable Origin In-Game"
- Or disable per-game in game properties

**Reversible**: N/A - informational fix only

---

### GenArial

**Purpose**: Ensures Arial font is available for proper text rendering in the game.

**What It Does**:

- Checks for Arial font files in Windows Fonts directory
- Checks for Arial font entries in Windows registry
- Provides instructions to install Arial font if missing

**How It Works**:

1. Checks `C:\Windows\Fonts\` for Arial font files
2. Checks Windows registry for Arial font entries
3. If missing, provides installation instructions
4. Lists multiple installation methods

**User Action Required**:

- Install Arial font via Windows Store
- Or copy from another PC
- Or download from Microsoft website

**Reversible**: N/A - informational fix only

---

### HDIconsFix

**Purpose**: Provides information about high-definition icons for Generals and Zero Hour.

**What It Does**:

- Checks for HD icon files in game directories
- Provides information about HD icon availability
- Explains that HD icons are provided by GenHub's Content system

**How It Works**:

1. Checks game directories for HD icon files
2. Provides information about HD icon availability
3. Explains that HD icons are typically provided by mods or community content
4. References GenHub's Content system for icon downloads

**User Action Required**:

- Download HD icons through GenHub's Content system
- Or install mods that include HD icons

**Reversible**: N/A - informational fix only

---

### WindowsMediaFeaturePack

**Purpose**: Checks for Windows Media Feature Pack installation required for some media playback features.

**What It Does**:

- Checks for Media Feature Pack in Windows registry
- Checks for Windows Media Player installation
- Provides instructions to install Media Feature Pack if missing

**How It Works**:

1. Checks Windows registry for Media Feature Pack entries
2. Checks for Windows Media Player executable
3. If missing, provides installation instructions
4. Only applicable for Windows 10 and later

**User Action Required**:

- Open Windows Settings > Apps > Optional features
- Click "Add a feature"
- Search for "Media Feature Pack"
- Click "Install"

**Reversible**: N/A - informational fix only

---

### GameRangerRunAsAdmin

**Purpose**: Provides GameRanger compatibility guidance to ensure games run as administrator.

**What It Does**:

- Checks for GameRanger installation
- Checks if game executables have admin compatibility flags
- Provides instructions to configure GameRanger

**How It Works**:

1. Checks registry and processes for GameRanger installation
2. Checks if game executables have admin compatibility flags
3. Provides step-by-step instructions to configure GameRanger
4. Lists multiple methods to enable run as administrator

**User Action Required**:

- Open GameRanger
- Go to Edit > Game Settings
- Select Generals or Zero Hour
- Check "Run as Administrator" option
- Or set compatibility flags on game executables

**Reversible**: N/A - informational fix only

---

### ExpandedLANLobbyMenu

**Purpose**: Provides guidance for expanded LAN lobby menu features in Generals and Zero Hour.

**What It Does**:

- Explains built-in LAN support in Generals and Zero Hour
- Provides step-by-step instructions for LAN play
- Lists best practices for LAN gaming
- Explains network requirements and firewall settings

**How It Works**:

1. Explains that LAN lobby menu is built into the game
2. Provides instructions for accessing LAN features
3. Lists network requirements
4. Provides troubleshooting tips

**User Action Required**:

- Ensure all players are on same network
- Launch game and go to Multiplayer > Network > LAN
- Create or join LAN game

**Reversible**: N/A - informational fix only

---

### ProxyLauncher

**Purpose**: Provides information about GenHub's proxy-based launching system.

**What It Does**:

- Explains GenHub's proxy launcher architecture
- Lists benefits of proxy launcher
- Explains integration with ActionSet framework
- Explains that proxy launcher is automatically used

**How It Works**:

1. Explains proxy launcher architecture
2. Lists benefits: compatibility, isolation, error handling
3. Explains integration with ActionSet framework
4. Explains automatic usage when launching through GenHub

**Benefits**:

- Improved compatibility with modern Windows versions
- Better process isolation
- Enhanced error handling and logging
- Support for custom launch parameters
- Integration with GenHub's ActionSet framework

**Reversible**: N/A - informational fix only

---

### StartMenuFix

**Purpose**: Creates or fixes start menu shortcuts for Generals and Zero Hour.

**What It Does**:

- Checks for existing shortcuts in Windows Start Menu
- Provides instructions to create shortcuts manually
- Explains how to create shortcuts through GenHub

**How It Works**:

1. Checks for shortcuts in Start Menu > Programs
2. Provides step-by-step instructions for manual creation
3. Explains how to create shortcuts through GenHub UI
4. Lists common shortcut names for both games

**User Action Required**:

- Right-click on game executable
- Select "Show more options" > "Create shortcut"
- Move shortcut to desired location
- Or use GenHub to create shortcuts

**Reversible**: N/A - informational fix only

---

### IntelGfxDriverCompatibility

**Purpose**: Provides Intel graphics driver compatibility guidance to prevent graphics issues.

**What It Does**:

- Checks for Intel graphics via registry and WMI
- Checks for Intel Driver & Support Assistant installation
- Provides instructions to update Intel drivers
- Lists multiple methods to obtain latest drivers

**How It Works**:

1. Checks Windows registry for Intel graphics entries
2. Uses WMI to query video controllers
3. Checks for Intel Driver & Support Assistant
4. Provides step-by-step update instructions
5. Explains post-update steps

**User Action Required**:

- Open Intel Driver & Support Assistant
- Go to Drivers tab
- Click "Check for updates"
- Follow prompts to install latest driver
- Restart computer after update

**Reversible**: N/A - informational fix only

---

## Fix Categories

### Automated Fixes (21)

These fixes automatically apply changes without user intervention:

1. BrowserEngineFix
2. DbgHelpFix
3. EAAppRegistryFix
4. MyDocumentsPathCompatibility
5. VCRedist2010Fix
6. RemoveReadOnlyFix
7. AppCompatConfigurationsFix
8. DirectXRuntimeFix
9. Patch104Fix
10. Patch108Fix
11. OptionsINIFix
12. OneDriveFix
13. EdgeScrollerFix
14. TheFirstDecadeRegistryFix
15. CNCOnlineRegistryFix
16. NetworkPrivateProfileFix
17. PreferIPv4Fix
18. FirewallExceptionFix
19. SerialKeyFix
20. CncOnlineLauncherFix
21. Patch104Fix (Official)
22. Patch108Fix (Official)

### Network Optimization Fixes (3)

These fixes optimize network settings for better LAN and online multiplayer:

1. NetworkPrivateProfileFix
2. PreferIPv4Fix
3. FirewallExceptionFix

### Informational Fixes (14)

These fixes provide guidance and require manual user action:

1. VanillaExecutableFix
2. ZeroHourExecutableFix
3. MalwarebytesFix
4. D3D8XDLLCheck
5. NahimicFix
6. DisableOriginInGame
7. GenArial
8. HDIconsFix
9. WindowsMediaFeaturePack
10. GameRangerRunAsAdmin
11. ExpandedLANLobbyMenu
12. ProxyLauncher
13. StartMenuFix
14. IntelGfxDriverCompatibility

---

## Execution Order

Fixes are applied in the following recommended order for optimal results:

1. **Critical Fixes** (must be applied first):
   - RemoveReadOnlyFix
   - MyDocumentsPathCompatibility
   - VCRedist2010Fix
   - DirectXRuntimeFix
   - Patch108Fix (Generals only)
   - Patch104Fix (Zero Hour only)
   - OptionsINIFix

2. **Compatibility Fixes** (apply after critical fixes):
   - OneDriveFix
   - AppCompatConfigurationsFix
   - EdgeScrollerFix
   - TheFirstDecadeRegistryFix
   - CNCOnlineRegistryFix
   - EAAppRegistryFix

3. **Network Optimization Fixes** (apply for better multiplayer):
   - NetworkPrivateProfileFix
   - PreferIPv4Fix
   - FirewallExceptionFix

4. **Optional Fixes** (apply as needed):
   - BrowserEngineFix
   - DbgHelpFix
   - VanillaExecutableFix
   - ZeroHourExecutableFix
   - MalwarebytesFix
   - D3D8XDLLCheck
   - NahimicFix
   - DisableOriginInGame
   - GenArial
   - HDIconsFix
   - WindowsMediaFeaturePack
   - GameRangerRunAsAdmin
   - ExpandedLANLobbyMenu
   - ProxyLauncher
   - StartMenuFix
   - IntelGfxDriverCompatibility

---

## Technical Details

### ActionSet Framework

All fixes implement the `IActionSet` interface and inherit from `BaseActionSet`:

```csharp
public interface IActionSet
{
    string Id { get; }
    string Title { get; }
    string Description { get; }
    bool IsCoreFix { get; }
    bool IsCrucialFix { get; }

    Task<bool> IsApplicableAsync(GameInstallation installation);
    Task<bool> IsAppliedAsync(GameInstallation installation);
    Task<ActionSetResult> ApplyAsync(GameInstallation installation, IProgress<double>? progress, CancellationToken ct);
    Task<ActionSetResult> UndoAsync(GameInstallation installation, IProgress<double>? progress, CancellationToken ct);
}
```

### Result Pattern

All fixes return `ActionSetResult` with the following structure:

```csharp
public record ActionSetResult(bool Success, string? ErrorMessage = null);
```

- `Success`: Indicates whether the fix was applied successfully
- `ErrorMessage`: Optional error message if the fix failed

### Dependency Injection

All fixes are registered as singletons in the DI container:

```csharp
services.AddSingleton<IActionSet, BrowserEngineFix>();
services.AddSingleton<IActionSet, DbgHelpFix>();
// ... etc
```

### Game Installation Model

Fixes receive a `GameInstallation` object containing:

```csharp
public class GameInstallation
{
    public bool HasGenerals { get; }
    public bool HasZeroHour { get; }
    public string GeneralsPath { get; }
    public string ZeroHourPath { get; }
    // ... other properties
}
```

---

## Common Patterns

### File Replacement Pattern

Used by fixes that replace files (e.g., DbgHelpFix):

1. Check if target file exists
2. Backup original file to `.bak`
3. Copy/extract new file
4. Verify new file exists
5. For undo: restore from backup

### Registry Fix Pattern

Used by fixes that modify registry (e.g., EAAppRegistryFix):

1. Check if key/value exists
2. Read current value (for undo)
3. Write new value
4. Verify write succeeded
5. Store original value for undo

### INI File Pattern

Used by fixes that modify INI files (e.g., OptionsINIFix):

1. Load INI file using `IGameSettingsService`
2. Apply optimal settings
3. Save modified INI file
4. For undo: restore original values

### Download and Install Pattern

Used by fixes that download and install software (e.g., VCRedist2010Fix):

1. Check if software is already installed
2. Download installer to temp directory
3. Execute with silent flags
4. Wait for completion
5. Verify installation
6. Clean up temp files

---

## Troubleshooting

### Fix Not Applying

If a fix fails to apply:

1. Check the logs for detailed error messages
2. Ensure you have administrator privileges
3. Verify game installation paths are correct
4. Check that required dependencies are installed
5. Try running the fix again

### Fix Cannot Be Undone

Some fixes cannot be undone:

- Official patches (Patch104Fix, Patch108Fix)
- Software installations (VCRedist2010Fix, DirectXRuntimeFix)
- Folder creation (MyDocumentsPathCompatibility)

### Informational Fixes

Informational fixes provide guidance but don't make changes:

- Check the logs for detailed instructions
- Follow the step-by-step guidance provided
- Some fixes require manual configuration in third-party software

---

## References

- [ActionSet Framework Documentation](../dev/result-pattern.md)
- [Game Settings Documentation](game-settings.md)
- [Content System Documentation](content.md)
- [Coding Style Guide](../dev/coding-style.md)
