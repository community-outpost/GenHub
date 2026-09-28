# Texture Editor

The Texture Editor is a built-in tool in GenHub for working with SAGE engine texture atlases and `MappedImage` entries. It replaces blind manual coordinate math with a visual slicing canvas, live validation, and one-click INI export.

## Features

- **Visual atlas canvas**: Open TGA, DDS, or PNG atlases with pan, zoom, and pixel-accurate slice overlays.
- **Project file explorer**: Browse a project folder for textures and `MappedImages` INI files, sharing the same explorer as the WND editor.
- **Draggable slices**: Move slices directly on the canvas or fine-tune coordinates in the inspector.
- **SAGE validation**: Live bounds checks, 1px alpha guard-border checks, and engine-accurate coordinate math (`Width = Right - Left`, exclusive edges).
- **Size presets**: One-click 64x64 large cameos, 60x48 small cameos, and 32x32 HUD buttons.
- **MappedImages library**: Scan any folder for `MappedImages` INI files and browse entries in the shared picker with live thumbnails.
- **Auto-pack**: Turn a folder of loose icons into a power-of-two atlas sheet plus matching INI entries.
- **Save and export**: Save slices next to the atlas as a sibling INI, or export to explicit INI and TGA/PNG paths.
- **Clipboard verbs**: Copy, cut, paste, and duplicate slices with standard shortcuts, shared with the WND editor.

## Getting Started

To access the Texture Editor:

1. Open GenHub.
2. Navigate to the **TOOLS** tab.
3. Select **Texture Editor** from the sidebar.

## Interface Overview

The Texture Editor interface consists of three columns:

### Left: Slices, Files, and Library

- **Slices tab**: All slices of the open atlas with thumbnails, dimensions, and origin coordinates.
- **Files tab**: The shared project explorer. Pick a folder, open a texture to edit it, or open an INI to import its entries into the library.
- **Library tab**: The shared `MappedImagePickerControl` browsing registry entries scanned from `MappedImages` folders. Use **Edit in Texture Editor** to load an entry as a slice.
- **Add / Duplicate / Delete**: Create a centered slice, duplicate the selected slice, or remove the selected slice.

### Center: Canvas

- Scroll to pan and use the zoom controls (or Ctrl + mouse wheel) to inspect pixels.
- Click an overlay rectangle to select its slice, then drag it to reposition.
- Selected slices use the accent border; out-of-bounds slices use the error border.

### Right: Inspector

- Edit the slice name and `Left`, `Top`, `Right`, `Bottom` coordinates with 1px precision.
- Validation rows confirm texture bounds and the 1px alpha guard border.
- Size presets resize the selected slice without moving its origin.

## Shared Editor Shell

The Texture, WND, and INI editors share one editor shell in `GenHub.Common.Editors`, so common behavior is implemented once and propagates to every present and future editor:

- `EditorToolViewModelBase`: Standard document verbs (new, open, save, save-as, undo, redo, copy, cut, paste, duplicate, delete), canvas zoom, busy tracking with cancellation, and discard confirmation. Editors enable verbs by overriding the matching members.
- `FileExplorerViewModel` and `EditorFileExplorerControl`: Shared project folder browser with file tree, current-file tracking, and per-editor file patterns.
- `AtomicFile`: Temp-file-plus-move writes shared by saves, packs, and exports so failed writes never truncate destinations.

## Shared Services

The Texture Editor is built on reusable core services in `GenHub.Core` so other tools never duplicate texture logic:

- `ISageMappedImageParser`: Parses and serializes SAGE `MappedImage` INI blocks. Also used by the GenHotkeys packager.
- `IMappedImageRegistry`: Case-insensitive in-memory catalog with SAGE load-order semantics (alphabetical, `HandCreated` last).
- `ISageTextureCodec`: Decodes 24/32-bit TGA (raw and RLE), uncompressed DDS, and DXT1 DDS into portable RGBA pixels; encodes 32-bit TGA.
- `IAtlasPackingService`: Packs sprites into power-of-two sheets and executes `TextureAtlasBuildRequest` rules.

## ModBuilder Integration

ModBuilder projects can synthesize atlases at build time through `TextureAtlasBuildRequest`, without shelling out to the UI tool:

```json
{
  "ItemType": "TextureAtlas",
  "SourceDirectory": "RawAssets/Cameos",
  "TargetTexture": "Art/Textures/CustomCameos_1024.tga",
  "TargetIni": "Data/INI/MappedImages/CustomCameos.ini",
  "GenerateMipmaps": false,
  "Padding": 1
}
```

A future build pipeline step can call `IAtlasPackingService.BuildAtlasAsync` with a host `ITextureImageLoader`, then pack the returned TGA bytes and INI content into the mod archive. SAGE 2D UI textures must keep `GenerateMipmaps` disabled to avoid blurred buttons and text.
