# Game Settings Architecture

This document explains the architecture behind game settings in GenHub, detailing why multiple layers exist and providing a step-by-step guide for adding new settings.

## Settings Persistence Strategy

### Profile as Single Source of Truth

When a profile is launched, GenHub applies the profile's settings to `Options.ini` and `settings.json`. The profile is the **single source of truth** for that launch session.

**Key Principle**: Settings changed in-game are preserved in `Options.ini` AdditionalProperties and will persist across launches as long as GenHub doesn't overwrite them.

### AdditionalProperties Preservation (Critical Fix)

Many game settings (like `UseDoubleClickAttackMove`, `ScrollFactor`, `Retaliation`, `StaticGameLOD`) are not explicitly modeled in GenHub but are stored in `Video.AdditionalProperties` or `AdditionalSections["TheSuperHackers"]`.

**The Fix**: When GenHub saves settings via `CreateOptionsFromViewModel()`, it now **updates** existing dictionaries instead of **replacing** them:

```csharp
// BEFORE (WRONG):
var tshDict = new Dictionary<string, string> { ... };
options.AdditionalSections["TheSuperHackers"] = tshDict;  // REPLACES entire section!

// AFTER (CORRECT):
if (!options.AdditionalSections.TryGetValue("TheSuperHackers", out var tshDict))
{
    tshDict = new Dictionary<string, string>();
    options.AdditionalSections["TheSuperHackers"] = tshDict;
}
// Update only managed settings, preserve all others
tshDict["ArchiveReplays"] = BoolToString(TshArchiveReplays);
```

This ensures that settings not in GenHub's UI are preserved when the user saves profile settings.

## Additional Video Settings

The following settings are stored in `Video.AdditionalProperties` and are fully integrated into GenHub:

| Setting | Property Name | Type | Default | Options.ini Key |
|---------|--------------|------|---------|----------------|
| Detail Level | `VideoStaticGameLOD` | string | "High" | `StaticGameLOD` |
| Ideal Detail | `VideoIdealStaticGameLOD` | string | "VeryHigh" | `IdealStaticGameLOD` |
| Double Click Guard | `VideoUseDoubleClickAttackMove` | bool | true | `UseDoubleClickAttackMove` |
| Scroll Speed | `VideoScrollFactor` | int | 50 | `ScrollFactor` |
| Retaliation | `VideoRetaliation` | bool | true | `Retaliation` |
| Dynamic LOD | `VideoDynamicLOD` | bool | false | `DynamicLOD` |
| Max Particles | `VideoMaxParticleCount` | int | 5000 | `MaxParticleCount` |
| Anti-Aliasing | `VideoAntiAliasing` | int | 1 | `AntiAliasing` |

These settings are:
- Stored in `GameProfile` as nullable properties
- Mapped through `UpdateProfileRequest` and `CreateProfileRequest`
- Handled by `GameSettingsViewModel` with appropriate defaults
- Written to `Options.ini` via `AdditionalProperties` by `GameSettingsMapper.ApplyToOptions()`
- Preserved when GenHub saves settings via `CreateOptionsFromViewModel()`

## Troubleshooting

### Settings Reset After Saving Profile

**Symptom**: Settings like Double Click Guard or Scroll Speed reset when you save profile settings in GenHub.

**Cause**: The `CreateOptionsFromViewModel()` method was replacing entire dictionaries instead of updating them.

**Fix**: Implemented in `GameSettingsViewModel.CreateOptionsFromViewModel()` - now preserves existing `AdditionalProperties` and `AdditionalSections`.

### Settings Not Applying from Profile

**Symptom**: Profile settings don't apply when launching the game.

**Cause**: The `ApplyToGeneralsOnlineSettings()` mapper was using `if (HasValue)` checks, skipping null values and leaving constructor defaults.

**Fix**: Changed to use null-coalescing operators with explicit defaults:
```csharp
settings.ShowFps = profile.GoShowFps ?? false;  // Always sets a value
```

## Overview

Adding a single game setting in GenHub involves modifying approximately 7-8 files. While this may seem complex, it adheres to a strict **Separation of Concerns** to ensure robustness, testability, and clear boundaries between data persistence, API contracts, and user interface.

### The 7 Layers of a Setting

Data flows from the disk (`Options.ini`) through the application to the UI (`GameSettingsView.axaml`) and back.

1. **Physical Storage**: `Options.ini` (The raw file on disk)
2. **INI Model**: `IniOptions.cs` / `VideoSettings.cs` (Representation of the file structure)
3. **Domain Entities**: `GameProfile.cs` (Database/Storage model for a profile)
4. **Data Transfer Objects (DTOs)**: `CreateProfileRequest.cs` / `UpdateProfileRequest.cs` (API contracts for moving data)
5. **Mapper**: `GameSettingsMapper.cs` (The "glue" translating between standard INI models and GenHub's internal profiles)
6. **Service Layer**: `GameSettingsService.cs` (Business logic for reading/writing/parsing)
7. **View Model**: `GameSettingsViewModel.cs` (State management for the UI)
8. **View**: `GameSettingsView.axaml` (User Interface)

---

## Why so many layers?

### 1. Persistence != Transport

The format used to save data to the database (or JSON profile file) in `GameProfile.cs` is often different from how we want to receive updates from the UI (`UpdateProfileRequest.cs`). Separation allows us to change the API without breaking the database, or vice versa.

### 2. Domain != INI Format

`Options.ini` is a legacy format with specific quirks (e.g., "yes"/"no" strings, flat structures). Our Domain Model (`GameProfile`) should use clean C# types (`bool`, `int`). The **Mapper** layer handles this translation so the rest of the app doesn't have to deal with parsing strings.

### 3. Separation of UI and Logic

The **ViewModel** decouples the UI from the business logic. We can test `GameSettingsViewModel` without launching the app window. It also handles formatting (e.g., converting a backend boolean to a checkbox state).

---

## How to Add a New Setting

Follow this checklist to add a new setting (e.g., `NewFeature`).

### 1. Core Models (The Data)

- [ ] **`GenHub.Core\Models\GameSettings\VideoSettings.cs`** (or `Audio`, etc.)
  - Add the property matching the `Options.ini` key.
  - *Example:* `public bool NewFeature { get; set; }`
- [ ] **`GenHub.Core\Models\GameProfile\GameProfile.cs`**
  - Add a nullable property to store this in the profile. Use a clear prefix (e.g., `Video...`).
  - *Example:* `public bool? VideoNewFeature { get; set; }`
- [ ] **`GenHub.Core\Models\GameProfile\CreateProfileRequest.cs`**
  - Add the property to allow setting it during creation.
- [ ] **`GenHub.Core\Models\GameProfile\UpdateProfileRequest.cs`**
  - Add the property to allow updating it.

### 2. Business Logic (The Glue)

- [ ] **`GenHub.Core\Helpers\GameSettingsMapper.cs`**
  - Update **6 methods**:
    - `ApplyFromOptions`: `profile.VideoNewFeature = options.Video.NewFeature;`
    - `ApplyToOptions`: `options.Video.NewFeature = profile.VideoNewFeature ?? default;`
    - `PopulateGameProfile`: Map request -> profile.
    - `PatchGameProfile`: Map request -> profile (for updates).
    - `UpdateFromRequest`: Map request -> profile.
    - `PopulateRequest`: Map profile -> request.
- [ ] **`GenHub\Features\GameSettings\GameSettingsService.cs`**
  - **Parsing**: Update `ParseVideoSection` (or relevant section) to read the key from the INI file.
  - **Serialization**: Update `SerializeOptionsIni` to write the key back to the file.
  - **Categorization**: Add the key to `videoKeys` or relevant list in `CategorizeRootSettings` to ensure it's not treated as an "unknown" setting.

### 3. User Interface (The Visuals)

- [ ] **`GenHub\Features\GameProfiles\ViewModels\GameSettingsViewModel.cs`**
  - Add `[ObservableProperty] private bool _newFeature;`
  - Update `LoadSettingsFromProfile`: `if (profile.VideoNewFeature.HasValue) NewFeature = profile.VideoNewFeature.Value;`
  - Update `GetProfileSettings`: `VideoNewFeature = NewFeature,`
  - Update `ApplyOptionsToViewModel`: `NewFeature = options.Video.NewFeature;`
  - Update `CreateOptionsFromViewModel`: `options.Video.NewFeature = NewFeature;`
- [ ] **`GenHub\Features\GameProfiles\Views\GameSettingsView.axaml`**
  - Add the control (e.g., `<CheckBox Content="New Feature" IsChecked="{Binding NewFeature}" />`).

## Custom GenHub Settings

Sometimes we need to save settings that **don't exist** in the standard `Options.ini` (e.g., `BuildingAnimations`).

- We store these in `AdditionalProperties` with a `GenHub` prefix (e.g., `GenHubBuildingAnimations`).

## Technical Implementation Reference

This section documents the specific classes and files involved in the GeneralsOnline settings pipeline.

### Core Files & Responsibilities

There are 7 key files that handle the lifecycle of a GeneralsOnline setting.

| Component | File Path | Class Name | Responsibility |
| :--- | :--- | :--- | :--- |
| **DTO (Request)** | `GenHub.Core\Models\GameProfile\UpdateProfileRequest.cs` | `UpdateProfileRequest` | Carries user input from UI. Has nullable fields (e.g., `GoShowFps`, `TshArchiveReplays`). |
| **Mapper** | `GenHub.Core\Helpers\GameSettingsMapper.cs` | `GameSettingsMapper` | Moves data from DTO -> Profile, and Profile -> INI/JSON Models. |
| **Model (DB)** | `GenHub.Core\Models\GameProfile\GameProfile.cs` | `GameProfile` | Stores the "Source of Truth". Contains persistent properties for all settings. |
| **Model (JSON)** | `GenHub.Core\Models\GameSettings\GeneralsOnlineSettings.cs` | `GeneralsOnlineSettings` | The exact structure serialized to `settings.json`. Inherits `TheSuperHackersSettings`. |
| **Model (INI)** | `GenHub.Core\Models\GameSettings\IniOptions.cs` | `IniOptions` | The structure serialized to `Options.ini`. Stores TSH settings in `AdditionalSections`. |
| **IO Service** | `GenHub\Features\GameSettings\GameSettingsService.cs` | `GameSettingsService` | Handles physical file writes. Methods: `SaveOptionsAsync` and `SaveGeneralsOnlineSettingsAsync`. |
| **Orchestrator** | `GenHub\Features\Launching\GameLauncher.cs` | `GameLauncher` | Triggers the write operation immediately before game start. |

### Data Flow Pipeline

Tracing a setting change (e.g., "Show FPS") from User to Disk:

1. **UI Request**: The frontend sends an `UpdateProfileRequest` containing `GoShowFps = true`.
2. **Mapping to Profile**:
    - `GameProfileManager` calls `GameSettingsMapper.PopulateGameProfile(profile, request)`.
    - Code: `profile.GoShowFps = request.GoShowFps ?? profile.GoShowFps;`
    - Result: database now stores the user's preference.

3. **Launch Sequence**:
    - User clicks "Launch".
    - `GameLauncher.cs` executes two parallel operations:

    **Path A: To Options.ini (Legacy/TSH)**
    - Calls `ApplyProfileSettingsToIniOptionsAsync`.
    - `GameSettingsMapper.ApplyToOptions` maps `profile.Tsh...` properties into `IniOptions.AdditionalSections["TheSuperHackers"]`.
    - `GameSettingsService` writes `Options.ini`. *Note: It manually adds the `[TheSuperHackers]` header.*

    **Path B: To settings.json (GeneralsOnline)**
    - Calls `ApplyGeneralsOnlineSettingsAsync`.
    - Instantiates new `GeneralsOnlineSettings`.
    - Manually maps properties: `settings.ShowFps = profile.GoShowFps.Value;`
    - `GameSettingsService` writes `settings.json` using `System.Text.Json`.

### Inheritance Detail

`GeneralsOnlineSettings.cs` inherits from `TheSuperHackersSettings.cs`.

```csharp
public class GeneralsOnlineSettings : TheSuperHackersSettings
{
    public bool ShowFps { get; set; }
    // ... other GO settings
}
```

This inheritance explains why `settings.json` contains keys like `ArchiveReplays` (a TSH setting). The `GameLauncher` maps TSH properties from the profile into the `GeneralsOnlineSettings` object before saving, effectively duplicating them for the GO client.
