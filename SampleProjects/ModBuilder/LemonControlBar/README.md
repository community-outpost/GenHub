# Lemon Edition Control Bar (Multi-Resolution) - Sample Project

This sample project is built from L3-M's Lemon Edition Control Bar Pro (v1.3) for Command & Conquer Generals: Zero Hour.

## Publisher
- **Author**: L3-M (Lemon)
- **Repository**: https://github.com/L3-M/GeneralsControlBar
- **Variants**: 720p (1280x720), 1080p (1920x1080), 1440p (2560x1440), 4K (3840x2160)

## Bundle Architecture
In ModBuilder, creator projects combine bundle items into variant bundle packs:
- **Art (`LemonControlBarArt`)**: Widescreen command bar textures (`AmericaCommandBarPro_4096_1024.dds`, `ChinaCommandBarPro_4096_1024.dds`, `GlaCommandBarPro_4096_1024.dds`, `BlankTexture_16.tga`, etc.)
- **Data (`LemonControlBarData`)**: `ControlBarScheme.ini`, `InGameUI.ini`, mapped images, GenTool fullviewport config, and language headers
- **Window Layouts**: Resolution-specific `.wnd` layouts (`ControlBar.wnd`, `ReplayControl.wnd`, `Diplomacy.wnd`, `Menus/*.wnd`) for 720p, 1080p, 1440p, and 4K

Building any resolution bundle pack produces the respective release `.big` archive ready for Zero Hour.
