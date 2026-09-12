# Map Manager

The Map Manager is a built-in tool in GenHub that allows you to manage, import, and share your Command & Conquer: Generals and Zero Hour custom maps with ease. It also features MapPacks for organizing maps into collections.

## Features

- **Unified View**: See all your maps for both Generals and Zero Hour in one place.
- **Easy Import**: Import maps directly from URLs or by dragging and dropping files.
- **Cloud Sharing**: Share your custom maps instantly via UploadThing.
- **Local Export**: Bundle multiple maps into a ZIP archive for local storage or manual sharing.
- **MapPacks**: Create named collections of maps for easy organization and profile management.
- **Rename Maps**: Double-click any map name to rename it directly in the manager.
- **Multi-Selection**: Select multiple maps using Ctrl+Click or Shift+Click for batch operations.
- **Validation**: Automatically detects missing preview images (TGA) that cause game crashes.

## Getting Started

To access the Map Manager:
1. Open GenHub.
2. Navigate to the **TOOLS** tab.
3. Select **Map Manager** from the sidebar.

## Interface Overview

The Map Manager interface consists of several key areas:

### Top Toolbar
- **Game Tabs**: Switch between **Generals** and **Zero Hour** maps.
- **URL Import Bar**: Paste a map URL and click the 📥 button to import.
- **Browse Button**: Click the 📎 button to open a file picker and select map files.
- **Search Bar**: Filter your map list by filename.
- **Refresh Button**: Click the ↻ button to reload the map list.
- **Open Folder Button**: Click the 📁 button to open your map directory in File Explorer.
- **MapPacks Button**: Click the Pack button to open the MapPack Manager.

### Map List
- **Thumbnail Column**: Shows a preview image for each map (if available).
- **Name Column**: Displays the map filename. **Double-click to rename** the map.
- **Size Column**: Shows the file size in human-readable format (KB, MB).
- **Type Column**: Shows the map type:
  - **Map**: Standard map with assets (Map + Ini + TGA + Txt)
  - **Archive**: ZIP archive containing maps
- **Modified Column**: Shows the last modified date of the map.

### Bottom Action Bar
- **Selected Count**: Shows how many maps are currently selected.
- **Delete Button**: Click the 🗑️ button to permanently delete selected maps.
- **Uncompress Button**: Click the 📦🔓 button to extract maps from selected ZIP archives (appears when ZIP files are selected).
- **ZIP Name**: Enter a custom filename for the ZIP archive (default: `Maps.zip`).
- **Zip Button**: Click the 📦 button to create a ZIP archive of selected maps.
- **Upload Button**: Click the ☁️ button to upload selected maps to the cloud.
- **History Button**: Click the ▼ button to view your upload history.

## Importing Maps

### From URL

You can import maps from various sources by pasting the link into the import bar and clicking the 📥 button:
- **UploadThing**: Direct links from other GenHub users.
- **Direct Links**: Any URL ending in `.map` or `.zip`.

### Drag and Drop
Simply drag one or more `.map` or `.zip` files from your computer and drop them anywhere on the Map Manager window to import them.

### Browse and Import
Click the 📎 button in the toolbar to open a file picker dialog. You can select multiple map files at once. Supported formats:
- `.map` - Individual map files
- `.zip` - ZIP archives containing maps

## Managing Maps

### Renaming Maps
To rename a map file:
1. Locate the map in the list.
2. **Double-click** on the map name in the Name column.
3. Enter the new name and press Enter.
4. The map file or directory will be renamed in your map directory.

### Selecting Multiple Maps
- **Ctrl+Click**: Click individual maps while holding Ctrl to select/deselect them.
- **Shift+Click**: Click a map, then Shift+Click another to select all maps in between.
- **Ctrl+A**: Press Ctrl+A to select all maps in the current view.

### Deleting Maps
1. Select one or more maps from the list.
2. Click the 🗑️ **Delete** button.
3. Confirm the deletion when prompted.
4. Selected maps will be permanently removed from your map directory.

### Opening Map Folder
Click the 📁 **Open Folder** button in the toolbar to open your game's map directory in File Explorer:
- **Generals**: `Documents\Command and Conquer Generals Data\Maps`
- **Zero Hour**: `Documents\Command and Conquer Generals Zero Hour Data\Maps`

## Exporting Maps

### Creating ZIP Archives
1. Select the maps you want to export from the list.
2. Optionally, enter a custom ZIP name in the text box (default: `Maps.zip`).
3. Click the 📦 **Zip** button.
4. A ZIP archive will be created in your map directory containing the selected maps.
5. File Explorer will open highlighting the created ZIP file.

### Uncompressing ZIP Archives
If you have ZIP archives containing maps:
1. Select the ZIP file(s) from the list.
2. Click the 📦🔓 **Uncompress** button (appears when ZIP files are selected).
3. The maps inside the ZIP will be extracted to your map directory.

## Sharing Maps

### Uploading to Cloud
1. Select the maps you want to share from the list.
2. Click the ☁️ **Upload** button.
3. Wait for the upload to complete (progress bar will show).
4. Once the upload is complete, the download link will be copied to your clipboard automatically.
5. Share the link with others via Discord, email, or any messaging app.

> [!IMPORTANT]
> **Size Limit**: Each individual map file must be under **5 MB**.
> **Privacy**: Shared maps are maintained for up to 14 days or until storage is full.

### Upload History
Click the ▼ **History** button to view your upload history:
- **File Name**: Shows the name of the uploaded file.
- **Timestamp**: Shows when the upload was made.
- **Size**: Shows the file size.
- **Status**: Shows if the link is still active (green) or expired (red).
- **Copy Link**: Click the 📋 button to copy the download link.
- **Remove**: Click the 🗑️ button to remove an item from history (this frees up your upload quota).
- **Clear All**: Click the button at the bottom to clear your entire upload history.

> [!NOTE]
> Removing items from your upload history frees up your upload quota immediately, allowing you to upload more files.

## MapPacks

MapPacks allow you to organize maps into named collections. This is especially useful for managing different map sets for different profiles or game modes.

### Creating a MapPack
1. Select the maps you want to include in the pack.
2. Click the **📦 MapPacks** button in the toolbar.
3. Enter a name and optional description.
4. Click **Create MapPack**.

### Managing MapPacks
Click the **📦 MapPacks** button to open the MapPack Manager panel:
- **Existing MapPacks**: Shows all your created MapPacks with:
  - **Name**: The MapPack name
  - **Created Date**: When the MapPack was created
  - **Maps Count**: Number of maps in the pack
  - **Loaded Status**: Shows if the MapPack is currently loaded (green badge)
- **Load MapPack**: Click the Load button to enable a MapPack for a profile.
- **Unload MapPack**: Click the Unload button to disable a MapPack.
- **Delete MapPack**: Click the 🗑️ button to permanently delete a MapPack.

### Using MapPacks
MapPacks are stored as metadata and integrate with GenHub's userdata system. When you load a MapPack for a profile, the maps will be automatically activated by the userdata system when that profile is launched.

> [!NOTE]
> **Integration with Profiles**: MapPacks work seamlessly with GenHub's profile system. Maps from loaded MapPacks are managed by the userdata service, which handles file linking and cleanup automatically.

## Architecture

The Map Manager is built on a modular service architecture:

- **`IMapDirectoryService`**: Manages map directory operations and file system access.
- **`IMapImportService`**: Handles importing maps from URLs, local files, and ZIP archives.
- **`IMapExportService`**: Manages exporting and cloud sharing via UploadThing.
- **`IMapPackService`**: Manages MapPack creation, loading, and storage.

## Map Storage

Maps imported or shared via GenHub are stored in:
- **Generals**: `Documents\Command and Conquer Generals Data\Maps`
- **Zero Hour**: `Documents\Command and Conquer Generals Zero Hour Data\Maps`

These are the standard game directories, ensuring compatibility with the game and other tools.
