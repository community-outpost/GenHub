# WND Editor: Custom Assets

The WND editor renders windows with the same layered files the game uses. This page
explains where art comes from, how to override it for a mod, and how the pieces
flow through ModBuilder into a bundled release.

## Where preview art comes from

For every mapped image the editor resolves, in order:

1. **Linked `.big` archives** (highest priority) and the **linked mod folder**,
   added through the toolbar (Link Mod Folder / Link .BIG Archive).
2. **The open file's ModBuilder project**, detected automatically when the `.wnd`
   lives inside a project (loose `GameFilesEdited` files plus built release
   archives).
3. **The selected game installation** (Zero Hour or Generals).
4. **The fallback installation** (Generals when Zero Hour is selected).

The preview status line reports how many referenced images resolved
(`16 of 16 images`). Anything missing is listed in the tooltip, and the log
records per-image provenance plus a tier summary line:

```text
Preview images: 16/16 (definitions: Expansion=14, BaseGame=2; ...)
```

## Overriding art for a mod

No import step is needed. Place files in the ModBuilder project and the editor
picks them up at mod priority, both in previews and in ModBuilder builds:

1. Drop the texture page (`.tga`) into
   `GameFilesEdited/Art/Textures/`.
2. Declare each crop in a MappedImages `.ini` under
   `GameFilesEdited/Data/INI/MappedImages/` (any `TextureSize_*` or
   `HandCreated` subfolder works):

```ini
MappedImage MyMenuBackdrop
  Texture = MyMenuPage
  Coords = Left:0 Top:0 Right:800 Bottom:600
  Status = NONE
End
```

3. Reference the mapped image name from the window's draw data
   (for example `ENABLEDDRAWDATA = IMAGE: MyMenuBackdrop, ...`).
4. Keep the `.wnd` file inside the same project (or link the project folder),
   then press Reload Assets to refresh the preview.

When ModBuilder bundles the project, `Data/INI/**/*.ini` ships verbatim and
`Art/Textures/**/*.tga` is converted to DXT5 `.dds`; both land in the release
archive at game-relative paths, so the game resolves exactly what the editor
previewed.

## Notes and limits

- Procedural content (minimaps, 3D menu shells, runtime text such as player
  names or challenge biographies) has no static art. The editor shows an
  honest placeholder: map previews render their file fill plus a caption,
  and runtime-populated labels stay blank until selected.
- Hidden windows (popups, alternate option pages) stay invisible on the canvas
  until selected in the tree, matching the game's at-rest layout.
- Zero Hour redefines many shared Generals art names. If a Generals-based
  `.wnd` looks wrong under a Zero Hour installation, confirm which file the
  game actually loads: Zero Hour menus are different files (plus, for the main
  menu, a 3D shell the 2D preview cannot reproduce).
