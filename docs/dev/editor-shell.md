# Editor Shell Standard

The WND, Texture, and future INI editors share one shell so canvas behavior is
implemented once and every editor feels familiar. New editors must reuse these
pieces instead of reimplementing pan, zoom, tabs, or menus.

## Shared pieces (`GenHub.Common.Editors`)

- `EditorToolViewModelBase`: zoom (`Zoom`, `ZoomIn/Out/ResetZoom`), dirty
  tracking, busy operations with cancellation, clipboard verbs, and the
  open folder / open file / save / save-as command surface.
- `EditorCanvasControl`: pan and zoom canvas host. Middle-drag always pans,
  left-drag pans while `IsPanMode` is set, and Ctrl+mouse wheel zooms anchored
  at the cursor. Bind `Zoom` and `IsPanMode` two-way; set `MaxZoom` when a tool
  needs a tighter limit (bind the tool ViewModel's zoom maximum). Call
  `FrameTo(offset)` to scroll to a content offset after layout.
- `FileExplorerViewModel` + `EditorFileExplorerControl`: project folder tree
  with `BrowseFolderAsync` and the `DirectoryAdoptedAsync` hook. Hosts set the
  hook to scan and open a default file so header Open folder and the Files tab
  Browse button share one flow.
- `EditorShellStyles.axaml` (merged in `App.axaml`): the canvas control
  template and the `compact-tabs` sidebar tab style. Never copy these styles
  into a tool view.

## Shell chrome conventions

- Header: leading context or hamburger menu, centered document title, zoom
  cluster (pan toggle, zoom out, percentage, zoom in, reset) on the right.
  Keep at most the primary open action and save visible; everything else goes
  in the hamburger `MenuFlyout`.
- Sidebars: `CardBackground` cards with `BorderBrush`, `CornerRadius 10`,
  `Padding 8,6`, and `compact-tabs` tab strips.
- Empty state: Open folder (primary) plus Open file (secondary) buttons.
- Feedback: `INotificationService` toasts only, never status labels.

## Constants (`EditorConstants`)

Zoom defaults, limits, the additive zoom step, the wheel zoom factor, and the
file explorer depth cap. Tool-specific limits (for example WND canvas sizes)
stay in the tool's own constants.
