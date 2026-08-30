# TextureOverhaul Sample Project

A sample ModBuilder project demonstrating asset conversion and texture packaging (TGA to DDS compression with mipmaps) for Command & Conquer: Generals & Zero Hour.

## Structure
- `Configs/ModBundleItems.json`: Defines the `HDTextures` bundle item converting `.tga` source files into compressed `.dds` textures.
- `Configs/ModBundlePacks.json`: Defines the `TextureOverhaul` bundle pack that creates `TextureOverhaul.big`.
- `GameFilesEdited/Art/Textures/`: Contains source `.tga` texture files.

## How to Test
1. Open this project in ModBuilder via `Open Project` -> `SampleProjects/ModBuilder/TextureOverhaul/TextureOverhaul.mbproj`.
2. Check the `Build` action and click `Execute Build`.
3. Observe image conversion processing `.tga` into `.dds` and packing into `TextureOverhaul.big`.
