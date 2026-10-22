---
title: Game Settings
description: Comprehensive game configuration management for Options.ini settings
---

# Game Settings

GenHub provides comprehensive management of game settings through the `Options.ini` file, supporting all configuration options for Command & Conquer Generals and Zero Hour. Settings are profile-specific, allowing each game profile to have its own custom configuration.

> [!NOTE]
> **Tool Profiles**: For profiles identified as `IsToolProfile`, game settings (`Options.ini`) are neither loaded nor applied, as these profiles launch standalone tools that do not rely on the base game configuration.

## Overview

The game settings system handles:

- **Video Settings**: Resolution, windowed mode, texture quality, anti-aliasing, shadows
- **Audio Settings**: Volume levels for SFX, music, voice, and 3D audio
- **Network Settings**: GameSpy IP address for online/LAN play
- **Profile-Specific Configuration**: Each profile maintains its own settings
- **Validation**: Ensures settings are within valid ranges
- **Preservation**: Unknown settings are preserved for game-specific configurations

## Settings Categories

### Video Settings

Controls visual quality and display options.

```csharp
public class VideoSettings
{
    public int ResolutionWidth { get; set; } = 800;
    public int ResolutionHeight { get; set; } = 600;
    public bool Windowed { get; set; } = false;
    public int TextureReduction { get; set; } = 0;
    public int AntiAliasing { get; set; } = 1;
    public bool UseShadowVolumes { get; set; } = false;
    public bool UseShadowDecals { get; set; } = true;
    public bool ExtraAnimations { get; set; } = true;
    public int Gamma { get; set; } = 100;
}
```

**Key Settings**:

- **Resolution**: Screen width and height in pixels
  - Common values: `800x600`, `1024x768`, `1920x1080`, `2560x1440`
  - Must match supported display modes

- **Windowed Mode**: Run game in a window instead of fullscreen
  - `true`: Windowed mode
  - `false`: Fullscreen mode (default)

- **Texture Reduction**: Controls texture quality (inverse scale)
  - `0`: Highest quality (no reduction)
  - `1`: Medium quality
  - `2`: Low quality
  - `3`: Lowest quality

- **Texture Quality Enum**: GenHub uses a more intuitive enum
  ```csharp
  public enum TextureQuality
  {
      Low = 0,
      Medium = 1,
      High = 2,
      VeryHigh = 3  // TheSuperHackers client only
  }
  ```
  > [!NOTE]
  > The `VeryHigh` texture quality option is only available when using TheSuperHackers game client. Other clients support Low, Medium, and High.

- **Anti-Aliasing**: Smooths jagged edges
  - `0`: Disabled
  - `1`: 2x MSAA (default)
  - `2`: 4x MSAA
  - `4`: 8x MSAA

- **Shadow Volumes**: Enables volumetric shadows (performance impact)
  - `false`: Disabled (default, better performance)
  - `true`: Enabled (higher quality)

- **Shadow Decals**: Enables shadow decals
  - `true`: Enabled (default)
  - `false`: Disabled

- **Extra Animations**: Enables additional visual effects
  - `true`: Enabled (default)
  - `false`: Disabled (better performance)

- **Gamma**: Brightness correction
  - Range: `50` (darker) to `150` (brighter)
  - Default: `100` (neutral)

---

### Audio Settings

Controls sound and music volume levels.

```csharp
public class AudioSettings
{
    public int SFXVolume { get; set; } = 70;
    public int SFX3DVolume { get; set; } = 70;
    public int VoiceVolume { get; set; } = 70;
    public int MusicVolume { get; set; } = 70;
    public bool AudioEnabled { get; set; } = true;
    public int NumSounds { get; set; } = 16;
}
```

**Key Settings**:

- **SFXVolume**: Sound effects volume
  - Range: `0` (muted) to `100` (maximum)
  - Default: `70`

- **SFX3DVolume**: 3D positional sound effects volume
  - Range: `0` to `100`
  - Default: `70`

- **VoiceVolume**: Unit voice lines and dialogue volume
  - Range: `0` to `100`
  - Default: `70`

- **MusicVolume**: Background music volume
  - Range: `0` to `100`
  - Default: `70`

- **AudioEnabled**: Master audio toggle
  - `true`: Audio enabled (default)
  - `false`: All audio disabled

- **NumSounds**: Maximum simultaneous sounds
  - Range: `2` to `32`
  - Default: `16`
  - Higher values improve audio quality but increase CPU usage

---

### Network Settings

Controls online and LAN play configuration.

```csharp
public class NetworkSettings
{
    public string? GameSpyIPAddress { get; set; }
}
```

**Key Settings**:

- **GameSpyIPAddress**: IP address for GameSpy/networking services
  - Used for LAN and online multiplayer
  - Format: IPv4 address (e.g., `192.168.1.100`)
  - Default: `null` (auto-detect)

  **Use Cases**:
  - **LAN Play**: Set to local IP address for LAN games
  - **Online Play**: Set to server IP for custom online services
  - **GenPatcher Integration**: Used by community patches for online functionality

  **Example**:
  ```csharp
  profile.GameSpyIPAddress = "192.168.1.100"; // LAN IP
  profile.GameSpyIPAddress = "server.example.com"; // Online server
  ```

> [!IMPORTANT]
> The `GameSpyIPAddress` setting was added to support community online services and LAN play. It is stored in the `Options.ini` file and persisted per-profile.

---

## Options.ini Structure

The `Options.ini` file is an INI-format configuration file located in the user's Documents folder:

- **Generals**: `Documents\Command and Conquer Generals Data\Options.ini`
- **Zero Hour**: `Documents\Command and Conquer Generals Zero Hour Data\Options.ini`

**Example Options.ini**:

```ini
Resolution = 1920 1080
Windowed = 0
TextureReduction = 0
AntiAliasing = 1
UseShadowVolumes = 0
UseShadowDecals = 1
ExtraAnimations = 1
Gamma = 100

SFXVolume = 70
SFX3DVolume = 70
VoiceVolume = 70
MusicVolume = 70
AudioEnabled = 1
NumSounds = 16

GameSpyIPAddress = 192.168.1.100
```

## Core Services

### GameSettingsService

Handles parsing and serialization of `Options.ini` files.

**Location**: `GenHub.Features.GameSettings.GameSettingsService`

**Key Methods**:

```csharp
public interface IGameSettingsService
{
    // Load settings from Options.ini
    Task<OperationResult<IniOptions>> LoadSettingsAsync(
        GameType gameType,
        CancellationToken cancellationToken = default);

    // Save settings to Options.ini
    Task<OperationResult> SaveSettingsAsync(
        IniOptions options,
        GameType gameType,
        CancellationToken cancellationToken = default);

    // Get default settings
    IniOptions GetDefaultSettings();
}
```

**Responsibilities**:
- Parse INI file format into structured `IniOptions` model
- Serialize `IniOptions` back to INI format
- Preserve unknown settings via `AdditionalProperties`
- Validate setting ranges and formats
- Handle missing or corrupted files

---

### GameSettingsMapper

Maps settings between `GameProfile` and `IniOptions`.

**Location**: `GenHub.Core.Helpers.GameSettingsMapper`

**Purpose**: Ensures profile-specific settings are correctly applied to the game's `Options.ini` file.

```csharp
public static class GameSettingsMapper
{
    public static void MapProfileToIniOptions(GameProfile profile, IniOptions options);
    public static void MapIniOptionsToProfile(IniOptions options, GameProfile profile);
}
```

**Mapping Example**:

```csharp
// Profile → Options.ini
if (profile.GameSpyIPAddress != null)
{
    options.Network.GameSpyIPAddress = profile.GameSpyIPAddress;
}

// Options.ini → Profile
profile.GameSpyIPAddress = options.Network.GameSpyIPAddress;
```

---

### GameSettingsViewModel

Provides UI binding and validation for game settings.

**Location**: `GenHub.Features.GameProfiles.ViewModels.GameSettingsViewModel`

**Key Properties**:

```csharp
[ObservableProperty]
private string? _gameSpyIPAddress;

[ObservableProperty]
private int _resolutionWidth;

[ObservableProperty]
private int _resolutionHeight;

[ObservableProperty]
private bool _windowed;

[ObservableProperty]
private int _sfxVolume;

// ... other settings
```

**Responsibilities**:
- Expose settings as observable properties for UI binding
- Validate user input (e.g., resolution ranges, volume 0-100)
- Load settings from profile or `Options.ini`
- Save settings back to profile and `Options.ini`
- Provide default values

---

## Profile Integration

Game settings are stored at two levels:

1. **Profile-Level**: Settings specific to a game profile
   - Stored in `GameProfile` model
   - Persisted in profile database
   - Applied when profile is launched

2. **Options.ini**: Game's configuration file
   - Updated when profile is launched
   - Read by the game executable
   - Shared across all profiles (overwritten on launch)

### Launch Flow

When a profile is launched:

1. **Load Profile Settings**: Retrieve settings from `GameProfile`
2. **Map to IniOptions**: Convert profile settings to `IniOptions` model
3. **Write Options.ini**: Serialize `IniOptions` to game's `Options.ini` file
4. **Launch Game**: Game reads `Options.ini` on startup

### Settings Persistence

```csharp
// Creating a profile with custom settings
var createRequest = new CreateProfileRequest
{
    Name = "My Profile",
    GameSpyIPAddress = "192.168.1.100",
    // ... other settings
};

// Updating profile settings
var updateRequest = new UpdateProfileRequest
{
    GameSpyIPAddress = "192.168.1.200",
    // ... other settings
};
```

## UI Integration

### GameSettingsView

**Location**: `GenHub.Features.GameProfiles.Views.GameSettingsView.axaml`

Provides UI controls for editing game settings:

- **Video Tab**: Resolution, windowed mode, texture quality, anti-aliasing, shadows
- **Audio Tab**: Volume sliders for SFX, music, voice, 3D audio
- **Network Tab**: GameSpy IP address input field
- **Advanced Tab**: Additional game-specific settings

**Example XAML** (Network Settings):

```xml
<TextBox
    Text="{Binding GameSpyIPAddress}"
    Watermark="192.168.1.100 or server.example.com"
    ToolTip.Tip="IP address for GameSpy/networking services (LAN or online play)" />
```

### Validation

Settings are validated on input:

```csharp
// Resolution validation
if (ResolutionWidth < 640 || ResolutionWidth > 7680)
{
    errors.Add("Resolution width must be between 640 and 7680");
}

// Volume validation
if (SFXVolume < 0 || SFXVolume > 100)
{
    errors.Add("Volume must be between 0 and 100");
}

// IP address validation (optional)
if (!string.IsNullOrEmpty(GameSpyIPAddress) && !IsValidIPAddress(GameSpyIPAddress))
{
    errors.Add("Invalid IP address format");
}
```

## Best Practices

### For Developers

1. **Always Validate Settings**: Check ranges and formats before saving
   ```csharp
   if (volume < 0 || volume > 100)
       throw new ArgumentOutOfRangeException(nameof(volume));
   ```

2. **Preserve Unknown Settings**: Use `AdditionalProperties` to maintain game-specific settings
   ```csharp
   foreach (var kvp in additionalSettings)
   {
       options.Video.AdditionalProperties[kvp.Key] = kvp.Value;
   }
   ```

3. **Handle Missing Files**: Provide sensible defaults if `Options.ini` doesn't exist
   ```csharp
   if (!File.Exists(optionsPath))
   {
       return GetDefaultSettings();
   }
   ```

4. **Log Setting Changes**: Track when settings are modified for debugging
   ```csharp
   logger.LogInformation("Updated GameSpyIPAddress from {Old} to {New}",
       oldValue, newValue);
   ```

### For Users

1. **Test Settings**: Verify settings work before saving
2. **Backup Options.ini**: Keep a backup of working configurations
3. **Use Presets**: Start with default settings and adjust as needed
4. **Profile-Specific Settings**: Create separate profiles for different configurations (e.g., LAN vs Online)

## Future Enhancements

Potential improvements under consideration:

- **Setting Presets**: Pre-configured settings for Low/Medium/High/Ultra quality
- **Auto-Detection**: Automatically detect optimal settings based on hardware
- **Setting Profiles**: Save and load named setting configurations
- **Validation UI**: Real-time validation feedback in settings UI
- **Import/Export**: Share settings between profiles or users
