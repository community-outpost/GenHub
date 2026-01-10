# Game Settings Architecture

This document explains the architecture behind game settings in GenHub, detailing why multiple layers exist and providing a step-by-step guide for adding new settings.

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
- In `GameSettingsMapper`, use `AdditionalProperties.TryGetValue` to read and write these manually.
- This ensures they persist in the `Options.ini` without crashing the game, as the game typically ignores unknown keys.
