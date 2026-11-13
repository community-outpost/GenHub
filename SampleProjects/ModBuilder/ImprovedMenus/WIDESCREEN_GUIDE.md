# Widescreen (16:9) Menu Adaptation Guide

This guide explains the technical principles and coordinate mathematics used by **ElTioRata/ImprovedMenus** to adapt Command & Conquer: Generals & Zero Hour menus to modern $16:9$ widescreen displays.

---

## The $4:3$ to $16:9$ Aspect Ratio Problem

The original SAGE engine was developed for $4:3$ CRT displays ($800\times600$, $1024\times768$).
- In $4:3$, the aspect ratio is $\frac{4}{3} \approx 1.333$.
- In $16:9$, the aspect ratio is $\frac{16}{9} \approx 1.777$.

When running the game in widescreen resolutions (such as $1920\times1080$), the engine scales the virtual $800\times600$ coordinate canvas horizontally across the wider viewport:
- Horizontal scaling factor: $\frac{16/9}{4/3} = \frac{4}{3} \approx 1.333\times$ wider.
- Without modification, UI elements stretched horizontally, appearing distorted and pushed outward.

---

## Coordinate Calculation Techniques (from ImprovedMenus)

### 1. Horizontal Pillarboxing & Centering
To keep dialog boxes and button panels looking proportional without stretching across the entire widescreen monitor:

$$\text{Effective Center} = \frac{\text{Canvas Width}}{2} = \frac{800}{2} = 400$$

For a dialog box of desired width $W$:
$$\text{Left} = 400 - \frac{W}{2}$$
$$\text{Right} = 400 + \frac{W}{2}$$

#### Examples:
- **Button Panel Width = 250 px**:
  - $\text{Left} = 400 - 125 = 275$
  - $\text{Right} = 400 + 125 = 525$
- **Wide Dialog Width = 500 px**:
  - $\text{Left} = 400 - 250 = 150$
  - $\text{Right} = 400 + 250 = 650$
- **Skirmish Screen Width = 700 px**:
  - $\text{Left} = 400 - 350 = 50$
  - $\text{Right} = 400 + 350 = 750$

---

### 2. High-Definition Texture Coordinates

In original Zero Hour, backdrops were split into quadrants or limited to $512\times512$. In ImprovedMenus:
- Full widescreen backdrops are saved as $1920\times1080$ or $1024\times1024$.
- The `MappedImage` coordinates cover the entire boundary:
  ```ini
  MappedImage MainMenuBackdrop
    Texture = MainMenuBackdrop_16_9.tga
    TextureWidth = 1024
    TextureHeight = 1024
    Coords = Left:0 Top:0 Right:1023 Bottom:1023
    Status = NONE
  End
  ```

---

## Key Menu Windows in Zero Hour

When creating a full menu overhaul, these are the primary `.wnd` files in `window/Menus/`:

| File | Purpose |
|---|---|
| `MainMenu.wnd` | Title screen and primary navigation (Single Player, Multiplayer, Options, Exit) |
| `OptionsMenu.wnd` | Video resolution, audio sliders, graphics presets, and game controls |
| `SkirmishGameOptionsMenu.wnd` | Skirmish setup, player slots, faction selection, and map options |
| `LanLobbyMenu.wnd` | Local network multiplayer lobby and game creation |
| `ScoreScreen.wnd` | Post-game victory/defeat statistics and graphs |
| `LoadScreen.wnd` | Mission and skirmish loading screen with faction art and progress bar |
| `ReplayMenu.wnd` | Match recording browser and playback controls |
| `CreditsMenu.wnd` | Rolling development credits |

---

## Best Practices

1. **Disable Mipmaps on Menu Textures**:
   Always set `"GenerateMipmaps": false` in `ModBundleItems.json` for UI textures. Mipmapping can cause buttons and text to blur when scaled.
2. **Use Alpha Channel Borders**:
   Add a 1-pixel transparent or shaded border in your texture sheets to prevent texture bleeding between adjacent button states.
3. **Preserve System Names**:
   Never alter the `NAME = "MainMenu.wnd:Button..."` identifiers in existing buttons, as C++ callback routines rely on exact name matching.
