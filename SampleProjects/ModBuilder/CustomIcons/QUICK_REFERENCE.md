# Quick Reference: SAGE Engine Icons & Hotkeys

This reference cheat-sheet lists the SAGE engine constants, syntax rules, and layout specs used in C&C Generals and Zero Hour.

---

## 1. SAGE Key Binding Constants

Use these constants in `CommandButton.ini` under `KeyBinding = <CONSTANT>`:

| Key | Constant | Key | Constant | Key | Constant |
|---|---|---|---|---|---|
| **A** | `KEY_A` | **N** | `KEY_N` | **0** | `KEY_0` |
| **B** | `KEY_B` | **O** | `KEY_O` | **1** | `KEY_1` |
| **C** | `KEY_C` | **P** | `KEY_P` | **2** | `KEY_2` |
| **D** | `KEY_D` | **Q** | `KEY_Q` | **3** | `KEY_3` |
| **E** | `KEY_E` | **R** | `KEY_R` | **4** | `KEY_4` |
| **F** | `KEY_F` | **S** | `KEY_S` | **5** | `KEY_5` |
| **G** | `KEY_G` | **T** | `KEY_T` | **6** | `KEY_6` |
| **H** | `KEY_H` | **U** | `KEY_U` | **7** | `KEY_7` |
| **I** | `KEY_I` | **V** | `KEY_V` | **8** | `KEY_8` |
| **J** | `KEY_J` | **W** | `KEY_W` | **9** | `KEY_9` |
| **K** | `KEY_K` | **X** | `KEY_X` | **Space** | `KEY_SPACE` |
| **L** | `KEY_L` | **Y** | `KEY_Y` | **Tab** | `KEY_TAB` |
| **M** | `KEY_M` | **Z** | `KEY_Z` | **Escape** | `KEY_ESCAPE` |

---

## 2. CommandBar Grid Layout (14 Buttons)

The in-game control bar command grid is organized as follows:

```
+---------------+---------------+---------------+---------------+
| Slot 1  [ Q ] | Slot 2  [ W ] | Slot 3  [ E ] | Slot 4  [ R ] |
+---------------+---------------+---------------+---------------+
| Slot 5  [ A ] | Slot 6  [ S ] | Slot 7  [ D ] | Slot 8  [ F ] |
+---------------+---------------+---------------+---------------+
| Slot 9  [ Z ] | Slot 10 [ X ] | Slot 11 [ C ] | Slot 12 [ V ] |
+---------------+---------------+---------------+---------------+
| Slot 13 [Rally Point / Special] | Slot 14 [Sell / Power / Exit] |
+-------------------------------+-------------------------------+
```

---

## 3. MappedImage Syntax

```ini
MappedImage <ImageIdentifier>
  Texture = <TextureFileName.tga>
  TextureWidth = <PowerOfTwoWidth>
  TextureHeight = <PowerOfTwoHeight>
  Coords = Left:<X1> Top:<Y1> Right:<X2> Bottom:<Y2>
  Status = NONE
End
```

- **Coordinates**: Left, Top, Right, Bottom are **inclusive** 0-indexed pixel positions.
  $$\text{Width} = (\text{Right} - \text{Left}) + 1$$
  $$\text{Height} = (\text{Bottom} - \text{Top}) + 1$$
- **Texture Dimensions**: Must always be powers of two (e.g. 64, 128, 256, 512, 1024, 2048).
- **Status**: Always `NONE` for static interface icons.

---

## 4. Standard Cameo Dimensions

| Asset Type | Standard Size | High-Res (2x) Size | Format |
|---|---|---|---|
| **Unit / Building Cameo** | $60 \times 48$ px | $120 \times 96$ px | DXT5 (with alpha) |
| **Upgrade / Special Power** | $60 \times 48$ px | $120 \times 96$ px | DXT5 |
| **Hotkey Corner Overlay** | $16 \times 16$ px | $32 \times 32$ px | DXT5 |
| **Rank / General Power Icon** | $64 \times 64$ px | $128 \times 128$ px | DXT5 |
| **Mini-map Radar Icon** | $16 \times 16$ px | $32 \times 32$ px | DXT5 |

---

## 5. Priority Ordering with `.big` Files

Generals & Zero Hour load `.big` files alphabetically. To ensure your custom icons and hotkey overrides take precedence over the stock game:
- Prefix output archives with an exclamation mark: `!CustomIconsAndHotkeys.big`
- This ensures your mod is loaded **after** `INIZH.big` and `TexturesZH.big`, overriding original files non-destructively.
