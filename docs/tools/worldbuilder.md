# WorldBuilder

WorldBuilder is a built-in tool in GenHub for working with Command & Conquer: Generals and Zero Hour map files. It opens, validates, generates, and saves `.map` documents with their `.wak` wave-track and `.tga` preview sidecars, and tidies the `map.ini` companion file.

The on-disk format support (DataChunk framing, RefPack/ZLib envelopes, terrain, object, script, lighting, waypoint, and preview chunks) is covered in detail by the format constants in `WorldBuilderConstants`. This page covers the tool itself.

## Features

- **Open & Save**: Round-trips `.map` files including the `.wak` companion and `.tga` preview sidecar. Drop a `.map` file onto the view to open it.
- **Map Summary**: Dimensions, sides, objects, teams, triggers, script lists, waypoint links, wave tracks, and total world cash.
- **Preview**: Shows the document preview in the floating Minimap radar window and can generate a high-resolution render on demand.
- **Validation**: Reports map issues by severity with error and warning counts.
- **Map Generation**: Procedurally generates a new battlefield from a seed, dimensions, and player count.
- **Team Exchange**: Imports and exports `.teams` files to share scripted teams between maps.
- **map.ini Tidy**: Normalizes the companion `map.ini` file beside the open map.
- **INI Editor Handoff**: Opens the companion `map.ini` in the INI editor tool once it is merged (gracefully inactive until then).
- **Projects**: Creates project folders, opens them, and imports map files from game-data folders.
- **Native Launch**: Finds the original Windows WorldBuilder in an installed game folder and launches it through Wine, optionally with the open map.

## Getting Started

To access WorldBuilder:
1. Open GenHub.
2. Navigate to the **TOOLS** tab.
3. Select **WorldBuilder** from the sidebar.

To open a map from a ModBuilder project, select a `.map` file in the ModBuilder file manager and choose **Edit in WorldBuilder**.

## Interface Overview

### Menu Bar & Command Ribbon

- **Menu Bar**: Native menu hierarchy (File, Edit, View, Map, Tools, Help) providing map document lifecycle actions, `.teams` import/export, undo/redo, zoom, validation, and Wine launcher.
- **Command Ribbon**: Quick access toolbar for Open, Save, Validate, Undo, Redo, Zoom controls, Tool selectors (Select, Mound, Smooth, Plateau, TilePaint, Road, Bridge, Ramp, WaterArea, Ruler), Brush sizes, and View mode toggles (Top-down 2D vs. Isometric 3D, Grid, Heights, Blend, Water, Objects, Waypoints, Triggers, Lighting).

### Interactive 2D & 3D Canvas

- **Interactive Canvas**: High-performance multi-layer 2D/3D render canvas supporting pointer stroke sculpting, live tool preview, mouse drag panning, and scroll wheel zooming.
- **Editing Tools**: Real-time heightfield elevation, smoothing, plateau flattening, texture tile painting, water triggers, and distance measuring.

### Panels & Editors

- **Generation Dialog**: Procedural map generator with seed/size/player controls (project folders and game-directory import live in the ViewModel commands and menu, not a browser panel).
- **Layers & Minimap**: Texture palette, layer visibility toggles, and the floating Minimap radar with the document preview bitmap and live high-resolution generator.
- **Script Editor**: Script tree organized by side and group, script flags (Active, OneShot, Subroutine, Easy/Normal/Hard), condition and action lists, and broken reference diagnostics.
- **Teams & Sides**: Skirmish faction and side management, player team configuration with AI priority and waypoint linking, plus `.teams` file exchange import and export.
- **Environment & Lighting**: Time-of-day selection (Morning, Afternoon, Dusk, Night) with ambient, diffuse, and directional lighting color parameters.
- **Roads & Bridges**: Road curve types, surface types, bridge spans, post rendering, and ramp tools.
- **Validation & Map Info**: Structural issue diagnostics, rule violation warnings, world cash calculation, and `map.ini` sanitization / module tidy.

## Files

A saved map consists of up to four files beside each other:

- `<name>.map`: the compiled map document.
- `<name>.wak`: wave tracks for water and rivers.
- `<name>.tga`: the 128x128 preview image.
- `<name>.ini`: optional map override script, normalized by the Tidy action.
