# BasicMod Sample Project

This sample demonstrates the **complete ModBuilder workflow** from raw edited files to a working game mod.

## What This Sample Does

This mod makes the following changes to C&C Generals Zero Hour:

1. **Tank Stats** - American Crusader Tank has doubled health (500 → 1000)
2. **Tank Texture** - Modified tank texture with red color scheme
3. **Tank Sound** - Custom tank movement sound

## Understanding the ModBuilder Workflow

### The Complete Pipeline

```
Raw Edited Files → Build Processing → Bundled Archive → Game Installation
```

1. **GameFilesEdited/** - Your edited game files (raw format)
   - INI files stay as INI
   - TGA textures get converted to DDS
   - WAV sounds stay as WAV

2. **.Build/** - Intermediate build files (created during build)
   - Converted textures (DDS format)
   - Processed files with metadata
   - Build cache for fast rebuilds

3. **.Release/** - Final output (created during build)
   - BasicMod.big (contains all your changes)
   - Ready to install to game

4. **Game Installation** - Where the mod is installed
   - BasicMod.big is copied to game folder
   - Game loads your changes automatically

## How to Use This Sample

### Step 1: Load the Project

1. Open GenHub
2. Navigate to **Tools → ModBuilder**
3. Click **"📦 Load Sample Project"** button
4. Or manually click **"Open Project"** and select `BasicMod.mbproj`

### Step 2: Explore the Files

1. Look at the **File Manager** section
2. On the **left side** (Game Files), you'll see the original game files
3. On the **right side** (Project Files), you'll see your edited files:
   - `Data/INI/Object/AmericaTank.ini` (red = modified)
   - `Art/Textures/sample.tga` (red = modified)
   - `Data/Audio/Sounds/TankMove.wav` (red = modified)

**File Status Colors:**
- 🔴 **Red** - Modified file (different from original game)
- 🟢 **Green** - New file (not in original game)
- ⚪ **Gray** - Unchanged file (identical to original)

### Step 3: View the Configuration

1. Click **"Edit Configuration"** button
2. See the **Bundle Items** (how files are processed):
   - **ModifiedINI** - Processes INI files
   - **ModifiedTextures** - Converts TGA to DDS with DXT5 compression
   - **ModifiedSounds** - Processes WAV audio files

3. See the **Bundle Packs** (how items are combined):
   - **BasicMod** - Combines all items into BasicMod.big

### Step 4: Build the Mod

1. In the **Build Options** section:
   - ✅ Check **"Build"** - Process files and create .big archive
   - ✅ Check **"Release"** - Create final release package
   - ⬜ Uncheck **"Install"** and **"Run Game"** for now

2. Click **"Execute Build"** button

3. Watch the **build output log**:
   ```
   [INFO] Starting build process...
   [INFO] Processing ModifiedINI...
   [INFO] Processing ModifiedTextures...
   [INFO]   Converting sample.tga to DDS (DXT5)...
   [INFO] Processing ModifiedSounds...
   [INFO] Creating BasicMod.big archive...
   [INFO] Build completed successfully!
   ```

4. Check the **`.Release/`** folder:
   - You'll see `BasicMod.big` (your complete mod package)

### Step 5: Test the Mod (Optional)

1. In **Build Options**:
   - ✅ Check **"Build"**
   - ✅ Check **"Release"**
   - ✅ Check **"Install"** - Copy mod to game folder
   - ✅ Check **"Run Game"** - Launch game after install

2. Click **"Execute Build"**

3. The mod is installed to your game and the game launches

4. In the game:
   - Start a skirmish with USA
   - Build an American Crusader Tank
   - Notice the changes:
     - Tank has more health (INI change)
     - Tank has modified texture (texture change)
     - Tank makes different sound (audio change)

## Project Structure

```
BasicMod/
├── BasicMod.mbproj                    # Project configuration
├── README.md                          # This file
│
├── GameFilesEdited/                   # YOUR EDITED FILES (source)
│   ├── Data/
│   │   ├── INI/
│   │   │   └── Object/
│   │   │       └── AmericaTank.ini    # Modified: Health = 1000
│   │   └── Audio/
│   │       └── Sounds/
│   │           └── TankMove.wav       # Modified: Custom sound
│   └── Art/
│       └── Textures/
│           └── sample.tga             # Modified: Red texture
│
├── config/                            # BUILD CONFIGURATION
│   ├── ModBundleItems.json            # Defines how files are processed
│   └── ModBundlePacks.json            # Defines how items are bundled
│
├── .Build/                            # INTERMEDIATE FILES (created on build)
│   ├── Art/
│   │   └── Textures/
│   │       └── sample.dds             # Converted from TGA
│   └── build_cache.msgpack            # Build cache for fast rebuilds
│
└── .Release/                          # FINAL OUTPUT (created on build)
    └── BasicMod.big                   # Complete mod package
```

## Understanding the Configuration

### ModBundleItems.json - Processing Rules

This file defines **how files are processed**:

```json
{
  "BundleItems": [
    {
      "Name": "ModifiedINI",
      "SourceFiles": ["GameFilesEdited/Data/INI/**/*.ini"],
      "OutputFormat": "INI",
      "Description": "Modified tank stats"
    },
    {
      "Name": "ModifiedTextures",
      "SourceFiles": ["GameFilesEdited/Art/Textures/**/*.tga"],
      "OutputFormat": "DDS",
      "Compression": "DXT5",
      "GenerateMipmaps": true,
      "Description": "Red tank texture"
    }
  ]
}
```

**Key concepts:**
- **Name** - Unique identifier for this bundle item
- **SourceFiles** - Glob patterns to match files (supports `**` for recursive)
- **OutputFormat** - Target format (INI, DDS, WAV, etc.)
- **Compression** - For textures: DXT1, DXT5, BC7
- **GenerateMipmaps** - Create mipmaps for textures

### ModBundlePacks.json - Bundling Rules

This file defines **how items are combined into .big archives**:

```json
{
  "BundlePacks": [
    {
      "Name": "BasicMod",
      "Items": ["ModifiedINI", "ModifiedTextures", "ModifiedSounds"],
      "OutputFile": ".Release/BasicMod.big",
      "Description": "Complete BasicMod package"
    }
  ]
}
```

**Key concepts:**
- **Name** - Name of the bundle pack
- **Items** - List of bundle items to include (from ModBundleItems.json)
- **OutputFile** - Where to create the .big archive
- **Description** - Human-readable description

## Modifying This Sample

### Change Tank Health

1. Open `GameFilesEdited/Data/INI/Object/AmericaTank.ini`
2. Find the lines:
   ```ini
   Body = ActiveBody ModuleTag_02
     MaxHealth       = 1000.0    ; MODIFIED
     InitialHealth   = 1000.0    ; MODIFIED
   End
   ```
3. Change to `2000.0` for even more health
4. Save the file
5. Click **"Execute Build"** to rebuild
6. Test in-game

### Change Tank Texture

1. Open `GameFilesEdited/Art/Textures/sample.tga` in Photoshop/GIMP
2. Modify the texture (change colors, add text, etc.)
3. Save the file
4. Click **"Execute Build"** to rebuild
5. The texture will be automatically converted to DDS
6. Test in-game

### Add More Files

1. Use **File Manager** to browse game files
2. Right-click a file and select **"Add to Project"**
3. The file is copied to `GameFilesEdited/` with correct structure
4. Edit the file in your preferred editor
5. The file is automatically included (via `**/*` wildcards in config)
6. Rebuild and test

### Add a New Bundle Item

1. Edit `config/ModBundleItems.json`
2. Add a new item:
   ```json
   {
     "Name": "MyNewItem",
     "SourceFiles": ["GameFilesEdited/Data/Scripts/**/*.scb"],
     "OutputFormat": "SCB",
     "Description": "Custom scripts"
   }
   ```
3. Edit `config/ModBundlePacks.json`
4. Add the item to the pack:
   ```json
   {
     "Name": "BasicMod",
     "Items": ["ModifiedINI", "ModifiedTextures", "ModifiedSounds", "MyNewItem"],
     "OutputFile": ".Release/BasicMod.big"
   }
   ```
5. Rebuild

## Build Performance

### First Build
- Processes all files
- Converts textures
- Creates .big archive
- **Time: ~2-5 seconds**

### Subsequent Builds (with cache)
- Only processes changed files
- Reuses cached conversions
- Updates .big archive
- **Time: ~0.5-1 second**

### Build Cache
- Stored in `.Build/build_cache.msgpack`
- Tracks file hashes and timestamps
- Automatically invalidates when files change
- Delete cache to force full rebuild

## Troubleshooting

### Build processes 0 files

**Problem**: Build completes but no files are processed

**Solutions**:
1. Check that files exist in `GameFilesEdited/`
2. Verify config wildcards match your files:
   - `**/*.ini` matches all INI files recursively
   - `*.ini` matches only INI files in root
3. Check build output for errors
4. Verify JSON syntax in config files

### Build fails with error

**Problem**: Build stops with error message

**Solutions**:
1. Read the error message in build output
2. Check that all source files exist
3. Verify config files are valid JSON
4. Check file permissions (read/write access)
5. Try deleting `.Build/` folder and rebuilding

### Game doesn't show changes

**Problem**: Mod builds successfully but changes don't appear in-game

**Solutions**:
1. Make sure **"Install"** was checked during build
2. Verify `BasicMod.big` was created in `.Release/`
3. Check that game launched from correct installation
4. Verify mod file is in game's data folder
5. Check that game is loading mods (some versions require `-mod` flag)

### Texture doesn't show in-game

**Problem**: Texture was converted but doesn't appear in-game

**Solutions**:
1. Verify texture was converted to DDS (check `.Build/` folder)
2. Check texture name matches game's expected name
3. Verify texture format is correct (DXT5 for alpha, DXT1 for no alpha)
4. Check texture dimensions are power of 2 (256, 512, 1024, etc.)
5. Verify mipmaps were generated if required

### Build is slow

**Problem**: Build takes longer than expected

**Solutions**:
1. Check that build cache is working (`.Build/build_cache.msgpack`)
2. Verify only changed files are being processed
3. Delete cache and rebuild to reset
4. Check disk I/O performance
5. Reduce number of files being processed

## Expected Build Times

| Operation | First Build | Cached Build |
|-----------|-------------|--------------|
| INI files | ~0.1s | ~0.01s |
| Texture conversion | ~1-2s | ~0.1s |
| Audio processing | ~0.5s | ~0.05s |
| Archive creation | ~1s | ~0.5s |
| **Total** | **~2-5s** | **~0.5-1s** |

## Next Steps

Now that you understand the ModBuilder workflow:

1. **Create your own project**
   - Click **"New Project"** in ModBuilder
   - Choose a name and location
   - Set up your project structure

2. **Add your game files**
   - Use File Manager to browse game files
   - Add files you want to modify
   - Edit them in `GameFilesEdited/`

3. **Configure processing**
   - Edit `ModBundleItems.json` to define processing rules
   - Edit `ModBundlePacks.json` to define output archives
   - Use this sample as a reference

4. **Build and test**
   - Build your mod
   - Install to game
   - Test in-game
   - Iterate and improve

5. **Share your mod**
   - Package your `.Release/` folder
   - Share with the community
   - Include installation instructions

## Advanced Topics

### Multiple Bundle Packs

You can create multiple .big files for different purposes:

```json
{
  "BundlePacks": [
    {
      "Name": "BasicMod_Core",
      "Items": ["ModifiedINI"],
      "OutputFile": ".Release/BasicMod_Core.big"
    },
    {
      "Name": "BasicMod_Graphics",
      "Items": ["ModifiedTextures"],
      "OutputFile": ".Release/BasicMod_Graphics.big"
    }
  ]
}
```

### Texture Compression Options

- **DXT1** - No alpha, 4:1 compression, smallest size
- **DXT5** - With alpha, 4:1 compression, medium size
- **BC7** - Best quality, 4:1 compression, largest size
- **Uncompressed** - No compression, largest size, best quality

### Glob Pattern Examples

- `**/*.ini` - All INI files recursively
- `Data/**/*.ini` - All INI files under Data/
- `*.ini` - INI files in root only
- `Data/INI/*.ini` - INI files in Data/INI/ only
- `**/{Object,Weapon}/*.ini` - INI files in Object or Weapon folders

## Support

For help with ModBuilder:
1. Check the GenHub documentation
2. Ask in the community Discord
3. Report bugs on GitHub
4. Check the FAQ section

## Credits

- **ModBuilder** - Part of GenHub by enowX Labs
- **Sample Project** - Demonstrates complete workflow
- **C&C Generals** - Original game by EA Games

---

**Happy Modding!** 🎮
