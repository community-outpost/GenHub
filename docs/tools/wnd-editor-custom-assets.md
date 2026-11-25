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

Resolution is strictly per-game: a Zero Hour target loads only Zero Hour art
(plus mod and linked layers), and a Generals target loads only Generals art.
Although the retail Windows Zero Hour binary includes a registry-driven fallback
for Generals `.big` archives (`Win32BIGFileSystem::init`), the editor deliberately
enforces strict per-game isolation so previews truthfully represent self-contained
assets in the target install or mod workspace. Under a dual install, any Generals-era
`.wnd` opened under Zero Hour will report missing art for names that Zero Hour itself
does not ship, making asset gaps visible so you can import replacements from the Assets
tab.

The preview status line reports how many referenced images resolved
(`16 of 16 images`). Anything missing is listed in the tooltip and in the
Assets tab, and the log records per-image provenance plus a tier summary line:

```text
Preview images: 16/16 (Expansion=14, BaseGame=2; ...)
```

## Overriding art for a mod

The fastest path is the editor's **Assets** tab (left sidebar): it lists every
image the open `.wnd` references but cannot resolve, with an **Import**
button per name. Picking a texture (`.png`, `.tga`, `.dds`, `.jpg`, `.jpeg`, `.bmp`)
copies it into the mod project and registers a full-page mapped image under
that name, then refreshes the preview. **Import Textures** imports several
files at once, registering each under its file stem. Importing needs a project
context: link the mod folder first, or open a `.wnd` that lives inside one.

The importer writes the same layout you could author by hand, so previews,
ModBuilder builds, and the game all agree:

1. The texture page lands in `GameFilesEdited/Art/Textures/`. Sources other
   than `.dds`/`.tga` are normalized to `.tga`, the loose format the engine
   reads, preserving transparency.
2. A full-page crop is declared in
   `GameFilesEdited/Data/INI/MappedImages/HandCreated/WndEditorImports.ini`:

```ini
MappedImage MyMenuBackdrop
  Texture = MyMenuBackdrop.tga
  Coords = Left:0 Top:0 Right:800 Bottom:600
End
```

3. Reference the mapped image name from the window's draw data
   (for example `ENABLEDDRAWDATA = IMAGE: MyMenuBackdrop, ...`). Three ways:
   select the window and press **Apply** on any row of the Assets tab's art
   library (search filters the list); type in a draw data image field, which
   autocompletes over every mapped image the current asset roots know about;
   or edit the raw property text directly.

When ModBuilder bundles the project, `Data/INI/**/*.ini` is optimized
(comments stripped and line endings normalized via `OptimizeIniFileAsync`)
while preserving semantics, and `Art/Textures/**/*.tga` is converted to DXT5
`.dds`; both land in the release archive at game-relative paths, so the game
resolves exactly what the editor previewed. Hand-authored definitions in the same folders keep working: the
editor merges them at mod priority with no import step required.

## Notes and limits

- Shell-driven presentation is reproduced from game data, not draw data: the
  challenge menu hides its biography panel, play button, and locked general
  tokens at rest (as `ChallengeMenuInit` does), and resolves token medallions
  from `ChallengeMode.ini` personas joined with `PlayerTemplate.ini`
  `MedallionRegular` fields. Mods overriding those INIs change the preview.
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
