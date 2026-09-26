# Custom Icons & Legionnaire's Hotkeys Sample Project

This sample project demonstrates how to create **Custom Cameo Icons**, **Legionnaire-Style QWERTY Hotkeys**, and **Hotkey Indicator Overlays** for **Command & Conquer: Generals & Zero Hour** using **GenHub ModBuilder**.

It is modeled after popular tournament and quality-of-life add-ons from the **Community Outpost** provider:
- **`icon` (Icons Pack)**: Modern, sharp unit and structure portraits.
- **`hleg` (Legionnaire's Hotkeys)**: Ergonomic QWERTY-based grid hotkey layout.
- **`hlen` (Hotkeys Indicators)**: Control bar visual badge overlays showing hotkey letters directly over command buttons.

---

## What This Sample Demonstrates

1. **Custom Unit Cameo Portraits** (`Art/Textures/CustomUnitIcons.tga`):
   - How multiple button icons are packed into a single texture atlas (sprite sheet).
   - How `MappedImage` definitions slice icons using pixel coordinates (`Left`, `Top`, `Right`, `Bottom`).

2. **Hotkey Indicator Badges** (`Art/Textures/HotkeyIndicators.tga`):
   - How corner badges (`[Q]`, `[W]`, `[E]`, `[R]`) are defined and positioned over command buttons.

3. **Command Button Hotkey Rebinding** (`Data/INI/CommandButton.ini`):
   - Assigning `KeyBinding = KEY_Q`, `KEY_W`, etc.
   - Binding `ButtonImage = SACrusaderCustom`, `SAPaladinCustom`, etc.
   - Linking `DescriptLabel` to localized tooltips.

4. **Grid Layout Synchronization** (`Data/INI/CommandSet.ini`):
   - Aligning command buttons into the 14-slot command bar grid:
     - **Row 1**: Slots 1–4 $\rightarrow$ `Q`, `W`, `E`, `R`
     - **Row 2**: Slots 5–8 $\rightarrow$ `A`, `S`, `D`, `F`
     - **Row 3**: Slots 9–12 $\rightarrow$ `Z`, `X`, `C`, `V`
     - **Utility Slots**: Slots 13–14 $\rightarrow$ Rally Point, Sell, Power

5. **Localized String Tooltips** (`Data/English/Generals.str`):
   - Updating unit tooltips to display the bound hotkey prominently (e.g., `Construct Crusader Tank [Q]`).

---

## Project Structure

```
Hotkeys/
├── Hotkeys.mbproj                               # ModBuilder project descriptor
├── README.md                                       # Comprehensive workflow guide
├── QUICK_REFERENCE.md                              # Key codes & coordinate cheat-sheet
│
├── config/
│   ├── ModBundleItems.json                         # Defines texture conversion & INI glob rules
│   └── ModBundlePacks.json                         # Packages files into .Release/!HotkeysLegionnaireZH.big
│
└── GameFilesEdited/
    ├── Art/
    │   └── Textures/
    │       ├── CustomUnitIcons.tga                 # Cameo icon sprite sheet (RGBA 32-bit)
    │       └── HotkeyIndicators.tga                # Hotkey badge overlay sheet
    └── Data/
        ├── English/
        │   └── Generals.str                        # Tooltip string overrides with [Q], [W]
        └── INI/
            ├── CommandButton.ini                   # ButtonImage & KeyBinding assignments
            ├── CommandSet.ini                      # 14-slot command grid alignment
            └── MappedImages/
                └── HandMade/
                    ├── CustomIcons.ini             # Slices for unit cameo portraits
                    └── HotkeyIndicators.ini        # Slices for hotkey badge overlays
```

---

## Step-by-Step Customization Guide

### 1. Adding or Replacing Custom Cameo Icons

1. **Prepare your artwork**:
   - In Photoshop, GIMP, or Paint.NET, create a 32-bit RGBA image (with alpha transparency).
   - Standard Generals cameo dimensions are **60×48** pixels (or **120×96** for high-resolution 2x packs).
   - Arrange your icons onto a power-of-2 canvas (e.g., $256\times256$, $512\times512$, or $1024\times1024$).
   - Save as `GameFilesEdited/Art/Textures/CustomUnitIcons.tga`.

2. **Map the coordinates in `CustomIcons.ini`**:
   Open `GameFilesEdited/Data/INI/MappedImages/HandMade/CustomIcons.ini` and add an entry:
   ```ini
   MappedImage SACrusaderCustom
     Texture = CustomUnitIcons.tga
     TextureWidth = 256
     TextureHeight = 256
     Coords = Left:0 Top:0 Right:59 Bottom:47
     Status = NONE
   End
   ```
   > **Note on Coordinates**: Coordinates are 0-indexed inclusive. For a 60-pixel wide icon starting at $x=0$, `Left:0 Right:59`.

3. **Assign the icon to a button in `CommandButton.ini`**:
   ```ini
   CommandButton Command_ConstructAmericaVehicleCrusader
     Command = UNIT_BUILD
     Object = AmericaVehicleCrusader
     TextLabel = CONTROLBAR:ConstructAmericaVehicleCrusader
     ButtonImage = SACrusaderCustom
     ButtonBorderType = ACTION
     DescriptLabel = CONTROLBAR:ToolTipAmericaVehicleCrusaderHotkey
     KeyBinding = KEY_Q
   End
   ```

---

### 2. Customizing Hotkey Layouts (Legionnaire-Style)

The Legionnaire Hotkey philosophy maps buttons directly to physical keyboard positions corresponding to the in-game command grid:

| Grid Position | Command Slot | Default Legionnaire Key | KeyBinding Constant |
|:---:|:---:|:---:|:---:|
| Top Row 1 | Slot 1 | **Q** | `KEY_Q` |
| Top Row 2 | Slot 2 | **W** | `KEY_W` |
| Top Row 3 | Slot 3 | **E** | `KEY_E` |
| Top Row 4 | Slot 4 | **R** | `KEY_R` |
| Mid Row 1 | Slot 5 | **A** | `KEY_A` |
| Mid Row 2 | Slot 6 | **S** | `KEY_S` |
| Mid Row 3 | Slot 7 | **D** | `KEY_D` |
| Mid Row 4 | Slot 8 | **F** | `KEY_F` |
| Bot Row 1 | Slot 9 | **Z** | `KEY_Z` |
| Bot Row 2 | Slot 10 | **X** | `KEY_X` |
| Bot Row 3 | Slot 11 | **C** | `KEY_C` |
| Bot Row 4 | Slot 12 | **V** | `KEY_V` |

To change a unit's hotkey:
1. Open `GameFilesEdited/Data/INI/CommandButton.ini`.
2. Locate the command button (e.g., `Command_ConstructAmericaVehiclePaladin`).
3. Update `KeyBinding = KEY_<LETTER>`.
4. Open `GameFilesEdited/Data/INI/CommandSet.ini` and verify that the button is placed in the matching numeric slot for that building's command set.

---

### 3. Adding Tooltip Hints

Open `GameFilesEdited/Data/English/Generals.str` and append or edit the tooltip:
```ini
CONTROLBAR:ToolTipAmericaVehicleCrusaderHotkey
"Construct Crusader Tank [Q]\nMedium battle tank equipped with a 105mm high-velocity cannon."
End
```

---

## Building with ModBuilder

1. Open **GenHub** and navigate to **Tools $\rightarrow$ ModBuilder**.
2. Click **Open Project** and select `Hotkeys.mbproj` (or select it from the recent projects list).
3. Review the bundles:
   - `CustomIconTextures`: Converts all `.tga` files into `.dds` using **DXT5** compression with mipmaps.
   - `CustomIconINIs`: Gathers mapped images, button definitions, and command sets.
   - `CustomIconStrings`: Gathers localized tooltip strings.
4. Select build options:
   - Check **Build** (compiles textures and prepares assets).
   - Check **Release** (generates `.Release/!HotkeysLegionnaireZH.big`).
5. Click **Execute Build**.
6. The compiled archive will be placed in `.Release/!HotkeysLegionnaireZH.big`.
   - The leading `!` ensures that your mod loads with high priority in Generals / Zero Hour, overriding default game files without modifying original game archives.

---

## Testing in Zero Hour

1. Copy `.Release/!HotkeysLegionnaireZH.big` to your Zero Hour game root folder (e.g. `C:\Games\Command and Conquer Generals Zero Hour\`).
2. Launch Zero Hour.
3. Start a Skirmish match as USA.
4. Build a War Factory and observe:
   - Crusader Tank and Paladin Tank cameos display custom art.
   - Tooltips show `[Q]` and `[W]`.
   - Pressing **Q** or **W** initiates construction instantly!
