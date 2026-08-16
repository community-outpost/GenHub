# Game File Modifications Guide

## Overview
This guide documents the structure and types of game modifications supported by ModBuilder, based on analysis of the ModBuilderSample project's GameFilesEdited folder.

## Directory Structure

The GameFilesEdited folder mirrors the game's internal file structure with three main directories:

```
GameFilesEdited/
├── Art/              # Textures, models, and visual assets
├── Data/             # Game data, audio, and configuration files
└── Window/           # UI definition files
```

## Complete File Inventory

**Total Files:** 75 files across all categories

### File Type Distribution
- **INI files:** 37 (game configuration and data)
- **TIF files:** 13 (texture images)
- **WAV files:** 8 (audio)
- **PSD files:** 7 (Photoshop source textures)
- **WND files:** 3 (UI window definitions)
- **STR files:** 3 (string/localization data)
- **TGA files:** 2 (texture images)
- **BLEND files:** 1 (Blender 3D model)

---

## 1. Art Folder - Visual Assets

### Location
`GameFilesEdited/Art/`

### Purpose
Contains texture files and 3D models for visual modifications to the game.

### Supported File Formats

#### Texture Formats
1. **Adobe Photoshop (PSD)**
   - RGB mode (3 channels)
   - RGBA mode (4 channels with alpha)
   - Support for alpha layers and alpha channels
   - 256x256 resolution standard
   - Examples:
     - `RGB_PSD.psd` - Basic RGB texture
     - `RGB_PSD_WithAlphaChannel.psd` - RGBA with transparency
     - `RGB_PSD_WithAlphaLayer.psd` - RGB with separate alpha layer
     - `RGB_PSD_WithAlphaLayer_WithAlphaChannel.psd` - Combined alpha support

2. **TIFF (TIF)**
   - RGB and RGBA support
   - Multiple compression options:
     - Uncompressed
     - LZW compression with RLE
     - LZW compression with ZIP
   - 256x256 resolution
   - Support for alpha channels and alpha layers
   - Examples:
     - `RGB_TIF_Uncompressed.tif`
     - `RGB_TIF_LZW_RLE.tif`
     - `RGB_TIF_WithAlphaChannel_LZW_ZIP.tif`

3. **Targa (TGA)**
   - RGB 24-bit
   - RGBA 32-bit with 8-bit alpha
   - 256x256 resolution
   - Examples:
     - `RGB_TGA.tga`
     - `RGB_TGA_WithAlphaChannel.tga`

#### 3D Models
- **Blender Files (.blend)**
  - Zstandard compressed format
  - Example: `Art/Models/AVSentry.blend`

### Naming Conventions
Texture files follow descriptive naming patterns indicating:
- Color mode (RGB)
- Alpha support (WithAlphaChannel, WithAlphaLayer, WithAlphaLayers)
- Compression type (LZW_RLE, LZW_ZIP, Uncompressed)

### Organization
- Root level: Texture files
- `Models/` subfolder: 3D model files

---

## 2. Data Folder - Game Data and Configuration

### Location
`GameFilesEdited/Data/`

### Purpose
Contains game logic, configuration, localization, and audio files.

### Structure

#### Audio Files
**Location:** `Data/Audio/Sounds/`

**Format:** WAV (RIFF WAVE audio)
- 16-bit mono at 22050 Hz (voice files)
- IMA ADPCM mono at 44100 Hz (sound effects)

**Organization:**
- `English/` subfolder for language-specific voice files
- Root level for general sound effects

**Examples:**
- `Data/Audio/Sounds/English/ihassea.wav` - Voice line
- `Data/Audio/Sounds/sfrenzya.wav` - Sound effect
- `Data/Audio/Sounds/sleafdro.wav` - Sound effect
- `Data/Audio/Sounds/sscrambl.wav` - Sound effect

#### Localization Files
**Supported Languages:**
- Brazilian
- Chinese (with 9x variants)
- English
- French
- German
- Italian
- Korean
- Polish
- Spanish

**File Types per Language:**
1. **CommandMap.ini** - Keyboard command mappings
2. **HeaderTemplate.ini** - UI header templates
3. **Language.ini** - Font and display settings

**Example Structure:**
```
Data/
├── Brazilian/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   └── Language.ini
├── English/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   └── Language.ini
└── [other languages...]
```

**Language.ini Contents:**
- Unicode font specifications
- Caption speeds and timing
- Font definitions for various UI elements
- Resolution scaling factors

#### String Files (.str)
**Format:** UTF-8 text with CRLF line terminators

**Purpose:** Localized text strings for UI and game messages

**Examples:**
- `Data/Autorun.str` - Autorun messages
- `Data/generals.str` - General game strings
- `Data/generals_de.str` - German language strings

**Structure:**
```
GUI:GameOptions
US: "GAME OPTIONS"
DE: "Spieloptionen"
FR: "Options de jeu"
ES: "Opciones del juego"
```

#### Game Configuration (INI Files)
**Location:** `Data/INI/`

**Core Configuration Files:**
1. **GameData.ini** - Main game configuration
   - Graphics settings (resolution, lighting, terrain)
   - Physics parameters (gravity, stiffness)
   - Camera settings (height, pitch, yaw, scroll speed)
   - Particle system limits
   - Weapon bonuses and damage modifiers
   - Audio settings
   - Network timing parameters
   - Game balance values

2. **GameDataDebug.ini** - Debug configuration variant

3. **GameLOD.ini** - Level of Detail settings

4. **GameLODPresets.ini** - LOD preset configurations

**Object Definitions:**
**Location:** `Data/INI/Object/`

Contains unit and object definitions:
- `FactionUnit.ini` - Faction-specific units
- `NatureUnit.ini` - Environmental objects

**INI File Format:**
- Semicolon (;) for comments
- Block-based structure with End statements
- Key-value pairs with = separator
- Support for nested blocks
- Exclusion markers for conditional content

**Example:**
```ini
;begin-exclusion-marker
GarbageCode
  ShellMapName = Maps\ShellMapMD\ShellMapMD.map
End
;end-exclusion-marker

GameData
  ShellMapName = Maps\ShellMapMD\ShellMapMD.map
  UseTrees = Yes
  FramesPerSecondLimit = 30
End
```

---

## 3. Window Folder - UI Definitions

### Location
`GameFilesEdited/Window/`

### Purpose
Defines in-game user interface windows and menus.

### File Format
**Extension:** .wnd

**Structure:** Custom declarative format with:
- Window hierarchy (parent/child relationships)
- Screen positioning and sizing
- Visual styling (colors, borders, images)
- Font specifications
- Callback functions
- Control types (buttons, text fields, static text)

### Files
1. **InGameChat.wnd** - In-game chat interface
2. **InGamePopupMessage.wnd** - Popup message display
3. **Window/Menus/MainMenu.wnd** - Main menu interface

### WND File Structure

**Header:**
```
FILE_VERSION = 2;
STARTLAYOUTBLOCK
  LAYOUTINIT = [None];
  LAYOUTUPDATE = [None];
  LAYOUTSHUTDOWN = [None];
ENDLAYOUTBLOCK
```

**Window Definition:**
```
WINDOW
  WINDOWTYPE = USER;
  SCREENRECT = UPPERLEFT: 8 376,
               BOTTOMRIGHT: 656 416,
               CREATIONRESOLUTION: 800 600;
  NAME = "InGameChat.wnd:ParentInGameChat";
  STATUS = ENABLED;
  STYLE = USER;
  SYSTEMCALLBACK = "InGameChatSystem";
  INPUTCALLBACK = "InGameChatInput";
  FONT = NAME: "Times New Roman", SIZE: 14, BOLD: 0;
  TEXTCOLOR = ENABLED: 255 255 255 0, ...
  ENABLEDDRAWDATA = IMAGE: NoImage, COLOR: 0 0 0 190, ...
END
```

**Control Types:**
- ENTRYFIELD - Text input fields
- STATICTEXT - Non-editable text labels
- PUSHBUTTON - Clickable buttons
- USER - Custom window types

**Child Elements:**
Windows can contain nested CHILD elements with their own properties.

---

## File Naming Conventions

### General Patterns
1. **Descriptive Names:** Files use clear, descriptive names indicating their purpose
2. **Case Sensitivity:** Mixed case (PascalCase and lowercase)
3. **Language Codes:** Two-letter codes in folder names (EN, DE, FR, etc.)

### Specific Patterns

#### Texture Files
- Format: `[ColorMode]_[FileType]_[AlphaInfo]_[Compression].[ext]`
- Example: `RGB_TIF_WithAlphaChannel_LZW_ZIP.tif`

#### Audio Files
- Voice files: Prefixed with language indicator (e.g., `ihasse[a-d].wav`)
- Sound effects: Descriptive names (e.g., `sfrenzya.wav`, `sleafdro.wav`)

#### Configuration Files
- Language-specific: Same filename across language folders
- Object-specific: Descriptive unit/object names
- System-wide: Generic names (GameData.ini, GameLOD.ini)

---

## Mapping to Game Structure

### How Source Files Map to Game Files

The GameFilesEdited folder structure **directly mirrors** the game's internal file organization:

1. **Art/** → Game's texture and model directories
   - Textures replace or supplement existing game textures
   - Models override default 3D models

2. **Data/** → Game's data directory
   - INI files override game configuration
   - Audio files replace or add sound effects
   - Language folders provide localized content
   - String files modify in-game text

3. **Window/** → Game's UI definition directory
   - WND files modify interface layouts
   - Changes affect in-game menus and HUD elements

### File Processing
ModBuilder processes these files and packages them into the game's format (likely .big archives or similar), maintaining the directory structure so the game engine can locate and load the modified assets.

---

## Typical Mod Content Examples

### 1. Texture Replacement Mod
**Files:**
- `Art/RGB_TGA.tga` - New unit texture
- `Art/RGB_TGA_WithAlphaChannel.tga` - Texture with transparency

**Purpose:** Replace unit or building textures with custom artwork

### 2. Balance Modification Mod
**Files:**
- `Data/INI/GameData.ini` - Modified weapon bonuses
- `Data/INI/Object/FactionUnit.ini` - Adjusted unit stats

**Purpose:** Rebalance game mechanics and unit capabilities

### 3. Localization Mod
**Files:**
- `Data/English/Language.ini` - Font settings
- `Data/English/HeaderTemplate.ini` - UI templates
- `Data/generals.str` - Translated strings

**Purpose:** Add or modify language support

### 4. Audio Replacement Mod
**Files:**
- `Data/Audio/Sounds/English/ihassea.wav` - Custom voice line
- `Data/Audio/Sounds/sfrenzya.wav` - New sound effect

**Purpose:** Replace game audio with custom sounds

### 5. UI Customization Mod
**Files:**
- `Window/InGameChat.wnd` - Modified chat interface
- `Window/Menus/MainMenu.wnd` - Custom main menu

**Purpose:** Redesign user interface elements

### 6. Comprehensive Mod
**Files:** Combination of all above types
**Purpose:** Total conversion or major gameplay overhaul

---

## Technical Specifications

### Image Requirements
- **Resolution:** 256x256 pixels (standard)
- **Color Modes:** RGB (24-bit) or RGBA (32-bit)
- **Alpha Support:** Channel-based or layer-based
- **Compression:** Uncompressed or LZW for TIFF

### Audio Requirements
- **Format:** WAV (RIFF)
- **Voice:** 16-bit mono, 22050 Hz
- **Effects:** IMA ADPCM mono, 44100 Hz

### Text Encoding
- **INI Files:** ASCII or UTF-8
- **STR Files:** UTF-8 with CRLF line endings
- **WND Files:** ASCII with CRLF line endings

### Configuration Syntax
- **Comments:** Semicolon (;) prefix
- **Blocks:** Begin with identifier, end with `End`
- **Values:** Key = Value format
- **Booleans:** Yes/No
- **Colors:** R:### G:### B:### A:### format
- **Coordinates:** X:### Y:### Z:### format

---

## Best Practices

### File Organization
1. Maintain the exact directory structure as shown
2. Use consistent naming conventions
3. Group related modifications together
4. Keep language-specific files in appropriate folders

### Texture Creation
1. Start with PSD or TIFF for maximum quality
2. Include alpha channels for transparency
3. Use appropriate compression for file size
4. Test multiple formats if compatibility issues arise

### Configuration Editing
1. Always comment changes with semicolons
2. Back up original values in comments
3. Use exclusion markers for testing
4. Validate syntax before building

### Localization
1. Provide translations for all supported languages
2. Maintain consistent string identifiers
3. Test font rendering for each language
4. Include fallback fonts for Unicode

### Audio Integration
1. Match sample rates to original files
2. Normalize audio levels
3. Use appropriate compression for file type
4. Test in-game audio mixing

---

## Supported Modification Types

Based on the file inventory, ModBuilder supports:

1. **Visual Modifications**
   - Texture replacement (PSD, TIF, TGA)
   - 3D model replacement (Blender)
   - UI visual customization

2. **Gameplay Modifications**
   - Unit statistics and behavior
   - Weapon damage and bonuses
   - Game balance parameters
   - Physics and camera settings

3. **Audio Modifications**
   - Voice line replacement
   - Sound effect replacement
   - Multi-language audio support

4. **Localization**
   - 9 language support
   - Font customization
   - String translation
   - UI text modification

5. **Interface Modifications**
   - Window layout changes
   - Menu customization
   - HUD modifications
   - Control styling

6. **Configuration**
   - Graphics settings
   - Network parameters
   - Game rules and limits
   - Debug options

---

## File Path Reference

### Art Files
```
GameFilesEdited/Art/
├── Models/
│   └── AVSentry.blend
├── RGB_PSD.psd
├── RGB_PSD_WithAlphaChannel.psd
├── RGB_PSD_WithAlphaLayer.psd
├── RGB_PSD_WithAlphaLayers.psd
├── RGB_PSD_WithAlphaLayer_WithAlphaChannel.psd
├── RGB_TGA.tga
├── RGB_TGA_WithAlphaChannel.tga
├── RGB_TIF_LZW_RLE.tif
├── RGB_TIF_LZW_ZIP.tif
├── RGB_TIF_Uncompressed.tif
├── RGB_TIF_WithAlphaChannel_LZW_RLE.tif
├── RGB_TIF_WithAlphaChannel_LZW_ZIP.tif
├── RGB_TIF_WithAlphaChannel_Uncompressed.tif
├── RGB_TIF_WithAlphaLayers_Uncompressed.tif
├── RGB_TIF_WithAlphaLayer_LZW_RLE.tif
├── RGB_TIF_WithAlphaLayer_LZW_ZIP.tif
├── RGB_TIF_WithAlphaLayer_Uncompressed.tif
├── RGB_TIF_WithAlphaLayer_WithAlphaChannel_LZW_RLE.tif
├── RGB_TIF_WithAlphaLayer_WithAlphaChannel_LZW_ZIP.tif
└── RGB_TIF_WithAlphaLayer_WithAlphaChannel_Uncompressed.tif
```

### Data Files
```
GameFilesEdited/Data/
├── Audio/
│   └── Sounds/
│       ├── English/
│       │   ├── ihassea.wav
│       │   ├── ihasseb.wav
│       │   ├── ihassec.wav
│       │   └── ihassed.wav
│       ├── sfrenzya.wav
│       ├── sleafdro.wav
│       ├── sscrambl.wav
│       └── ssneakat.wav
├── Brazilian/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   └── Language.ini
├── Chinese/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   ├── HeaderTemplate9x.ini
│   ├── Language.ini
│   └── Language9x.ini
├── English/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   └── Language.ini
├── French/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   └── Language.ini
├── German/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   ├── Language.ini
│   └── SCGenChallengeWinLoss512.INI
├── Italian/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   └── Language.ini
├── Korean/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   └── Language.ini
├── Polish/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   └── Language.ini
├── Spanish/
│   ├── CommandMap.ini
│   ├── HeaderTemplate.ini
│   └── Language.ini
├── INI/
│   ├── GameData.ini
│   ├── GameDataDebug.ini
│   ├── GameLOD.ini
│   ├── GameLODPresets.ini
│   └── Object/
│       ├── FactionUnit.ini
│       └── NatureUnit.ini
├── Autorun.str
├── generals.str
└── generals_de.str
```

### Window Files
```
GameFilesEdited/Window/
├── Menus/
│   └── MainMenu.wnd
├── InGameChat.wnd
└── InGamePopupMessage.wnd
```

---

## Summary

ModBuilder supports comprehensive game modifications through a well-organized file structure that mirrors the game's internal organization. The system handles:

- **21 texture files** in multiple formats (PSD, TIF, TGA)
- **1 3D model** (Blender format)
- **8 audio files** (WAV format)
- **37 configuration files** (INI format)
- **3 UI definition files** (WND format)
- **3 string files** (STR format)
- **9 language localizations** with 3 files each

This structure enables modders to create everything from simple texture replacements to complete game overhauls, with full support for localization, audio, gameplay mechanics, and user interface customization.
