# Lemon Edition Control Bar (Multi-Resolution) - Sample Project

This sample project is built from L3-M's Lemon Edition Control Bar Pro (v1.3) for Command & Conquer Generals: Zero Hour.

## Publisher
- **Author**: L3-M (Lemon)
- **Repository**: https://github.com/L3-M/GeneralsControlBar
- **Variants**: 720p (1280x720), 1080p (1920x1080), 1440p (2560x1440), 4K (3840x2160)

## Bundle Architecture
The publisher ships four `.big` archives per resolution zip. ModBuilder mirrors that
layout exactly so every built archive is byte-for-byte identical to the release:

- **Art (`LemonControlBarArt1080`, `LemonControlBarArt2160`)**: Widescreen command bar
  textures (`AmericaCommandBarPro_4096_1024.dds`, `ChinaCommandBarPro_4096_1024.dds`,
  `GlaCommandBarPro_4096_1024.dds`, `BlankTexture_16.tga`, etc.), one generation each
  for the 1080p/720p and 1440p/4K zips.
- **Data (`LemonControlBarData1080`, `LemonControlBarData2160`)**: `ControlBarScheme.ini`,
  `InGameUI.ini`, mapped images, and the data-archive window layouts
  (`ControlBar.wnd`, `ReplayControl.wnd`, `Diplomacy.wnd`, quit menus).
- **Base (`LemonControlBarBase`)**: Shared `ControlBarPro.txt` and GenTool
  fullviewport data, identical across all resolutions.
- **Window Layouts (`LemonControlBarWindows_720p`, `_1080p`, `_1440p`, `_4K`)**:
  Per-resolution locale data (`Data/<Language>/...`) and widescreen `.wnd` menu
  layouts (`Menus/*.wnd`, `MOTD.wnd`).

Building any bundle pack produces the respective release `.big` archive ready for
Zero Hour. Each pack references a layout manifest (`config/*.big.manifest.json`)
pinning the exact entry order, trailer, and SHA-256 of the publisher archive, and
the build verifies the output hash automatically.
