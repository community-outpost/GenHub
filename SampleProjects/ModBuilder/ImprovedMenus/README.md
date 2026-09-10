# Improved Menus & Widescreen UI Sample Project

This sample project demonstrates how to customize **Menus, User Interface Windows (.wnd), and Widescreen (16:9) Layouts** for **Command & Conquer: Generals & Zero Hour** using **GenHub ModBuilder**.

It is directly inspired by the open-source [ElTioRata/ImprovedMenus](https://github.com/ElTioRata/ImprovedMenus) project, which redesigned Zero Hour's original $4:3$ menus into modern, crisp $16:9$ widescreen layouts.

---

## What This Sample Demonstrates

1. **Window Definition Files (`window/Menus/*.wnd`)**:
   - Understand SAGE engine UI hierarchy: Parent Windows $\rightarrow$ Child Controls $\rightarrow$ Pushbuttons $\rightarrow$ Static Labels.
   - Learn how virtual screen resolution coordinates (`CREATIONRESOLUTION: 800 600`) are positioned and scaled.
   - Included layouts:
     - `MainMenu.wnd`: Main menu layout with centered button column and custom backdrop.
     - `OptionsMenu.wnd`: Clean centered audio/video/controls settings dialog.
     - `SkirmishGameOptionsMenu.wnd`: Skirmish map selection and player configuration screen.

2. **Custom Menu Art & Texture Mapping**:
   - High-definition menu backdrops (`Data/English/Art/Textures/MainMenuBackdrop_16_9.tga`).
   - Custom button state textures (`Data/English/Art/Textures/MenuCustomButtons.tga`): Normal, Hilited (hover), and Pushed states.
   - Coordinate slices defined in `Data/INI/MappedImages/HandCreated/ImprovedMenusMappedImages.ini`.

3. **Widescreen 16:9 Layout Principles**:
   - The SAGE engine originally renders UI in an $800\times600$ ($4:3$) coordinate space, which becomes stretched on modern 1080p, 1440p, and 4K widescreen monitors.
   - Widescreen menu mods reposition elements toward the center or sides with calculated horizontal margins to prevent distortion and provide an expansive modern feel.

---

## Project Structure

```
ImprovedMenus/
├── ImprovedMenus.mbproj                            # ModBuilder project descriptor
├── README.md                                       # Comprehensive workflow guide
├── WIDESCREEN_GUIDE.md                             # Mathematical guide to 16:9 UI coordinates
│
├── config/
│   ├── ModBundleItems.json                         # Build rules for .wnd, .ini, and textures
│   └── ModBundlePacks.json                         # Packages into .Release/!ImprovedMenus.big
│
└── GameFilesEdited/
    ├── window/
    │   └── Menus/
    │       ├── MainMenu.wnd                        # Main menu GUI layout
    │       ├── OptionsMenu.wnd                     # Settings dialog layout
    │       └── SkirmishGameOptionsMenu.wnd         # Skirmish match setup layout
    └── Data/
        ├── English/
        │   └── Art/
        │       └── Textures/
        │           ├── MainMenuBackdrop_16_9.tga   # Widescreen backdrop texture
        │           └── MenuCustomButtons.tga       # Button states (Normal, Hilited, Pushed)
        └── INI/
            └── MappedImages/
                └── HandCreated/
                    └── ImprovedMenusMappedImages.ini # MappedImage coordinates for UI art
```

---

## How SAGE Engine `.wnd` Files Work

Every menu in Generals and Zero Hour is controlled by a `.wnd` layout file in `window/Menus/`. A typical control is defined as:

```ini
WINDOW
  WINDOWTYPE = PUSHBUTTON;
  SCREENRECT = UPPERLEFT: 275 190,
               BOTTOMRIGHT: 525 225,
               CREATIONRESOLUTION: 800 600;
  NAME = "MainMenu.wnd:ButtonSinglePlayer";
  STATUS = ENABLED+BORDER+IMAGE;
  STYLE = USER;
  FONT = NAME: "Arial", SIZE: 12, BOLD: 1;
  TEXTCOLOR = ENABLED: 220 220 220 255, HILITE: 255 255 0 255;
  ENABLEDDRAWDATA = IMAGE: MenuButtonNormal, COLOR: 255 255 255 255, BORDERCOLOR: 0 0 0 255;
  HILITEDRAWDATA  = IMAGE: MenuButtonHilited, COLOR: 255 255 255 255, BORDERCOLOR: 0 0 0 255;
  TEXT = "GUI:SinglePlayer";
END
```

### Key Properties:
- **`WINDOWTYPE`**: `USER` (container/dialog), `PUSHBUTTON` (clickable button), `STATIC_TEXT` (label), `LISTBOX`, `SLIDER`, `CHECKBOX`, `EDITBOX`.
- **`SCREENRECT`**:
  - `UPPERLEFT: X Y`: Top-left corner in virtual coordinates.
  - `BOTTOMRIGHT: X Y`: Bottom-right corner.
  - `CREATIONRESOLUTION: 800 600`: The coordinate grid used for positioning.
- **`NAME`**: Unique identifier used by the engine's C++ UI logic (do not change standard button names like `ButtonSinglePlayer`, `ButtonMultiplayer`, or `ButtonOptions` or the game won't bind click events).
- **`ENABLEDDRAWDATA` / `HILITEDRAWDATA`**: References the `MappedImage` to draw when idle or hovered.
- **`TEXT`**: Key from `Data/English/Generals.str` (e.g., `GUI:SinglePlayer`, `GUI:Options`, `GUI:Exit`).

---

## Step-by-Step Customization Guide

### 1. Replacing Menu Artwork

1. Create your custom background in your favorite graphics editor at **1920×1080** (or **256×256** / **512×512** / **1024×1024** for texture sheets).
2. Save as `GameFilesEdited/Data/English/Art/Textures/MainMenuBackdrop_16_9.tga`.
3. Open `GameFilesEdited/Data/INI/MappedImages/HandCreated/ImprovedMenusMappedImages.ini` and set:
   ```ini
   MappedImage MainMenuBackdrop
     Texture = MainMenuBackdrop_16_9.tga
     TextureWidth = 1024
     TextureHeight = 1024
     Coords = Left:0 Top:0 Right:1023 Bottom:1023
     Status = NONE
   End
   ```

### 2. Repositioning or Resizing Buttons

1. Open `GameFilesEdited/window/Menus/MainMenu.wnd`.
2. Locate the button you want to move (for example, `ButtonSinglePlayer`):
   ```ini
   SCREENRECT = UPPERLEFT: 275 190,
                BOTTOMRIGHT: 525 225,
                CREATIONRESOLUTION: 800 600;
   ```
3. Calculate new coordinates:
   - Width: $525 - 275 = 250$ pixels.
   - Height: $225 - 190 = 35$ pixels.
   - To center horizontally on an $800$-wide grid:
     $$\text{Left} = \frac{800 - 250}{2} = 275, \quad \text{Right} = 275 + 250 = 525$$
4. Adjust `UPPERLEFT` and `BOTTOMRIGHT` to reposition as desired.

---

## Building with ModBuilder

1. Open **GenHub** and navigate to **Tools $\rightarrow$ ModBuilder**.
2. Click **Open Project** and choose `SampleProjects/ModBuilder/ImprovedMenus/ImprovedMenus.mbproj`.
3. Verify that the bundles are loaded:
   - `MenuWindows`: Gathers all `.wnd` layout files from `window/Menus/`.
   - `MenuMappedImages`: Gathers `MappedImages/*.ini`.
   - `MenuTextures`: Converts UI background and button textures without mipmaps (keeping UI crisp).
4. In Build Options, check **Build** and **Release**.
5. Click **Execute Build**.
6. The compiled mod will be generated at `.Release/!ImprovedMenus.big`.

---

## Testing in Zero Hour

1. Copy `.Release/!ImprovedMenus.big` into your Zero Hour root installation directory.
2. Launch Zero Hour at your display's native resolution (e.g., $1920\times1080$, $2560\times1440$, or $3840\times2160$ via GenPatcher / Options.ini).
3. Experience your custom, modern menu layout!
