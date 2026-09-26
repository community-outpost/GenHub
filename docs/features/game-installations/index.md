---
title: Game Installations
description: Automatic detection of Command & Conquer Generals and Zero Hour installations
---

The **Game Installations** feature provides **automatic discovery** of *Command & Conquer: Generals* and *Zero Hour* installations across both **Windows** and **Linux** platforms.

It follows a layered architecture to ensure **platform abstraction**, **consistent results**, and **developer‑friendly APIs**.

---

## Game Directory Constants

When referencing standard game installation directories, use the constants provided in `GameClientConstants`:

- `GameClientConstants.GeneralsDirectoryName` → `"Command and Conquer Generals"`
- `GameClientConstants.ZeroHourDirectoryName` → `"Command and Conquer Generals Zero Hour"`

This ensures consistency across all installation detectors and makes future updates easier.

---

## Architecture Layers

### 1. GameInstallationService

The main **service layer** that exposes the public API and handles in‑memory caching.

- **Caching**:
  Results are detected once and cached using a thread‑safe `SemaphoreSlim`.
- **Error Handling**:
  Validates input parameters and reports descriptive error messages to API consumers.
- **Lazy Loading**:
  Detection occurs only on the *first* request, then cached for reuse.
+- **Granular Manifest Loading**:
+  Attempts to load game clients from existing manifests first. Only installations missing manifests trigger an expensive directory scan, preventing unnecessary rescans of Steam‑integrated and established installations.

---

### 2. GameInstallationDetectionOrchestrator

Coordinates all registered **platform detectors** and aggregates their results.

- **Platform Filtering**: Runs only detectors compatible with the current operating system.
- **Result Aggregation**: Combines results into a single `DetectionResult`.
- **Error Collection**: Accumulates detector errors without stopping the entire process.
- **Performance Monitoring**: Tracks elapsed detection time per run.

---

### 3. Platform Detectors

Platform‑specific modules that actually scan for game installations.

#### WindowsInstallationDetector

- **Steam Detection**
  - Uses registry keys (`SteamPath`/`InstallPath`)
  - Parses `libraryfolders.vdf` to locate all installed Steam libraries
  - Scans `steamapps/common` for Generals & Zero Hour

- **EA App Detection**
  - Checks registry under `SOFTWARE\Electronic Arts\EA Desktop`
  - Reads `InstallSuccessful` and `InstallLocation`
  - Checks `SOFTWARE\WOW6432Node\EA Games\Command and Conquer Generals Zero Hour` for installation paths

- **CD/ISO Detection**
  - Uses registry lookup as a fallback method
  - Checks `SOFTWARE\WOW6432Node\EA Games\Command and Conquer Generals Zero Hour` directly
  - Does not require EA App or Steam to be installed

#### LinuxInstallationDetector

- **Steam Detection (Proton)**
  - Reads `libraryfolders.vdf` in common paths:
    - `~/.local/share/Steam/steamapps/libraryfolders.vdf`
    - `~/.steam/steam/steamapps/libraryfolders.vdf`
    - `/usr/share/steam/steamapps/libraryfolders.vdf`
    - `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`
    - `~/snap/steam/common/.local/share/Steam`
  - For each library, checks under `steamapps/common/`
  - Can detect different packaging types (binary, flatpak, snap)

- **Lutris (EA App)**
  - Uses Lutris cli
  - Can detect different packaging types (binary, flatpak, snap soon)

- **Wine/Proton Prefix Detection**
  - Searches known Wine prefix locations (`~/.wine`, `.local/share/wineprefixes`, PlayOnLinux, Bottles, etc.)
  - Validates prefix (`drive_c/windows/system32` must exist)
  - Looks for game directories under `Program Files*/EA Games` and `Program Files*/Command and Conquer`
  - Confirms via `generals.exe` presence

- **CD/ISO Detection (Wine Registry)**
  - Parses Wine registry files (`system.reg`, `user.reg`) within prefixes
  - Extracts installation paths from `EA Games` registry entries
  - Converts Windows registry paths to Unix-style paths for Linux compatibility

---

## Models and Results

The system returns structured, layered results at each step.

### `OperationResult<T>`

Used by **API/service layer** operations.

```csharp
public class OperationResult<T>
{
    public bool Success { get; }
    public T? Data { get; }
    public IReadOnlyList<string> Errors { get; }

    public static OperationResult<T> CreateSuccess(T data);
    public static OperationResult<T> CreateFailure(string error);
    public static OperationResult<T> CreateFailure(IEnumerable<string> errors);
}
```

Example usage:

- `GetInstallationAsync(string id)` → returns `OperationResult<GameInstallation>`
- `GetAllInstallationsAsync()` → returns `OperationResult<IReadOnlyList<GameInstallation>>`

---

### `DetectionResult<T>`

Used by **detection layer** (per detector).

```csharp
public sealed class DetectionResult<T>
{
    public bool Success { get; }
    public IReadOnlyList<T> Items { get; }
    public IReadOnlyList<string> Errors { get; }
    public TimeSpan Elapsed { get; }

    public static DetectionResult<T> CreateSuccess(IEnumerable<T> items, TimeSpan elapsed);
    public static DetectionResult<T> CreateFailure(string error);
    public static DetectionResult<T> CreateFailure(IEnumerable<string> errors);
}
```

Example usage:

- `WindowsInstallationDetector.DetectInstallationsAsync()`
  → returns `DetectionResult<GameInstallation>` containing **0–N installations**

---

## Error Handling Strategy

Each layer has **clear responsibility**:

1. **Detection Layer**
   - Catches registry/file system exceptions.
   - Converts into `DetectionResult` failures (with errors, not crashes).

2. **Orchestration Layer**
   - Runs all detectors that match the platform.
   - Aggregates results.
   - Collects errors without stopping detection.

3. **Service Layer**
   - Caches results.
   - Validates inputs.
   - Exposes clean, structured `OperationResult` for API/consumer use.

4. **Consumer Layer**
   - Always receives structured success **with valid installations** or structured failure **with descriptive errors**.

---

## Result Flow

```text
Platform Detectors → DetectionResult<T>
    ↓
Orchestrator → Aggregated DetectionResult<T>
    ↓
Service Layer → OperationResult<T>
    ↓
API Consumers → Structured Success/Failure with details
```

---

## Supported Platform Matrix

| Platform | Steam | EA App | CD/ISO | Wine / Proton |
|----------|-------|--------|--------|---------------|
| Windows  | ✅ Registry + vdf | ✅ Registry | ✅ Registry | ❌ (not applicable) |
| Linux    | ✅ vdf scan | ❌ (not available) | ✅ Wine Reg | ✅ Prefix scanning |

---

## Example Usage

```csharp
var service = new GameInstallationService(orchestrator);

// Fetch all installations
var result = await service.GetAllInstallationsAsync();

if (result.Success)
{
    foreach (var install in result.Data!)
    {
        Console.WriteLine($"{install.InstallationType} → {install.InstallationPath}");
    }
}
else
{
    Console.WriteLine("Errors: " + string.Join(", ", result.Errors));
}
```

---

## Summary

- **Cross‑platform detection**: Windows (Steam, EA App, CD/ISO) + Linux (Steam Proton, Lutris, Wine, CD/ISO)
- **Layered architecture** ensures separation of concerns
- **Structured results** with robust error handling and caching
- **Extensible design**: new detectors can be added with minimal changes to the core logic
- **Prioritized detection**: ensures the most reliable installation is used (Steam > EA App > CD/ISO > Retail)
- **Tool Profile Support**: Tool Profiles (standalone executables) bypass the requirement for a physical game installation, allowing them to run independently of the base game.
