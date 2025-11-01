---
title: Game Installations
description: Automatic detection of Command & Conquer Generals and Zero Hour installations
---

The **Game Installations** feature provides **automatic discovery** of *Command & Conquer: Generals* and *Zero Hour* installations across both **Windows** and **Linux** platforms.

It follows a layered architecture to ensure **platform abstraction**, **consistent results**, and **developer‑friendly APIs**.

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
  - Searches known Wine prefix locations:
    - `~/.wine`
    - `~/.local/share/wineprefixes`
    - `~/.PlayOnLinux/wineprefix`
    - `~/.var/app/com.usebottles.bottles/data/bottles/bottles`
    - `/opt/wine`
  - Validates prefix (`drive_c/windows/system32` must exist)
  - Looks for game directories under `Program Files*/EA Games` and `Program Files*/Command and Conquer`
  - Confirms via `generals.exe` presence

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

| Platform | Steam | EA App | Wine / Proton |
|----------|-------|--------|---------------|
| Windows  | ✅ Registry + vdf | ✅ Registry keys | ❌ (not applicable) |
| Linux    | ✅ vdf scan | ❌ (not available) | ✅ Prefix scanning |

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

- **Cross‑platform detection**: Windows (Steam, EA App) + Linux (Steam Proton, Wine)  
- **Layered architecture** ensures separation of concerns  
- **Structured results** with robust error handling and caching  
- **Extensible design**: new detectors (e.g. Origin, CD installs) can be added without changing service API
