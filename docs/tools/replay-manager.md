# Replay Manager

The Replay Manager is a built-in tool in GenHub that allows you to manage, import, and share your Command & Conquer: Generals and Zero Hour replay files with ease.

## Features

- **Unified View**: See all your replays for both Generals and Zero Hour in one place.
- **Easy Import**: Import replays directly from URLs or by dragging and dropping files.
- **Cloud Sharing**: Share your best matches instantly via UploadThing.
- **Local Export**: Bundle multiple replays into a ZIP archive for local storage or manual sharing.
- **Conflict Resolution**: Automatically handles duplicate filenames during import.
- **Rename Replays**: Double-click any replay file name to rename it directly in the manager.
- **Multi-Selection**: Select multiple replays using Ctrl+Click or Shift+Click for batch operations.

## Getting Started

To access the Replay Manager:
1. Open GenHub.
2. Navigate to the **TOOLS** tab.
3. Select **Replay Manager** from the sidebar.

## Interface Overview

The Replay Manager interface consists of several key areas:

### Top Toolbar
- **Game Tabs**: Switch between **Generals** and **Zero Hour** replays.
- **URL Import Bar**: Paste a replay URL and click the 📥 button to import.
- **Browse Button**: Click the 📎 button to open a file picker and select replay files.
- **Search Bar**: Filter your replay list by filename.
- **Refresh Button**: Click the ↻ button to reload the replay list.
- **Open Folder Button**: Click the 📁 button to open your replay directory in File Explorer.

### Replay List
- **Thumbnail Column**: Shows a preview image for each replay (if available).
- **Name Column**: Displays the replay filename. **Double-click to rename** the replay.
- **Size Column**: Shows the file size in human-readable format (KB, MB).
- **Modified Column**: Shows the last modified date of the replay.

### Bottom Action Bar
- **Selected Count**: Shows how many replays are currently selected.
- **Delete Button**: Click the 🗑️ button to permanently delete selected replays.
- **Zip Button**: Click the 📦 button to create a ZIP archive of selected replays.
- **Upload Button**: Click the ☁️ button to upload selected replays to the cloud.
- **History Button**: Click the ▼ button to view your upload history.

## Importing Replays

### From URL

You can import replays from various sources by pasting the link into the import bar and clicking the 📥 button:
- **UploadThing**: Direct links from other GenHub users.
- **Generals Online**: Match view URLs.
- **GenTool**: Directory URLs from the GenTool data repository.
- **Direct Links**: Any URL ending in `.rep` or `.zip`.

### Drag and Drop
Simply drag one or more `.rep` or `.zip` files from your computer and drop them anywhere on the Replay Manager window to import them.

### Browse and Import
Click the 📎 button in the toolbar to open a file picker dialog. You can select multiple replay files at once. Supported formats:
- `.rep` - Individual replay files
- `.zip` - ZIP archives containing replays

## Managing Replays

### Renaming Replays
To rename a replay file:
1. Locate the replay in the list.
2. **Double-click** on the replay name in the Name column.
3. Enter the new name and press Enter.
4. The replay file will be renamed in your replay directory.

### Selecting Multiple Replays
- **Ctrl+Click**: Click individual replays while holding Ctrl to select/deselect them.
- **Shift+Click**: Click a replay, then Shift+Click another to select all replays in between.
- **Ctrl+A**: Press Ctrl+A to select all replays in the current view.

### Deleting Replays
1. Select one or more replays from the list.
2. Click the 🗑️ **Delete** button.
3. Confirm the deletion when prompted.
4. Selected replays will be permanently removed from your replay directory.

### Opening Replay Folder
Click the 📁 **Open Folder** button in the toolbar to open your game's replay directory in File Explorer:
- **Generals**: `Documents\Command and Conquer Generals Data\Replays`
- **Zero Hour**: `Documents\Command and Conquer Generals Zero Hour Data\Replays`

## Exporting Replays

### Creating ZIP Archives
1. Select the replays you want to export from the list.
2. Optionally, enter a custom ZIP name in the text box (default: `Replays.zip`).
3. Click the 📦 **Zip** button.
4. A ZIP archive will be created in your replay directory containing the selected replays.
5. File Explorer will open highlighting the created ZIP file.

### Uncompressing ZIP Archives
If you have ZIP archives containing replays:
1. Select the ZIP file(s) from the list.
2. Click the 📦🔓 **Uncompress** button (appears when ZIP files are selected).
3. The replays inside the ZIP will be extracted to your replay directory.

## Sharing Replays

### Uploading to Cloud
1. Select the replays you want to share from the list.
2. Click the ☁️ **Upload** button.
3. Wait for the upload to complete (progress bar will show).
4. Once the upload is complete, the download link will be copied to your clipboard automatically.
5. Share the link with others via Discord, email, or any messaging app.

> [!IMPORTANT]
> **Size Limit**: Each individual replay or ZIP file must be under **1 MB**.
> **Privacy**: Shared replays are maintained for up to 14 days or until storage is full.

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

## Storage Policy

Replays imported or shared via GenHub are subject to the following policies:
- Files are maintained in our cloud storage for **14 days**.
- Older files may be removed automatically to make room for new ones.
- Only `.rep` files are allowed within shared archives.

## Architecture

The Replay Manager is built on a modular service architecture:

- **`IReplayDirectoryService`**: Manages replay directory operations and file system access.
- **`IReplayImportService`**: Handles importing replays from URLs, local files, and ZIP archives.
- **`IReplayExportService`**: Manages exporting and cloud sharing via UploadThing.
- **`IUploadRateLimitService`**: Enforces weekly upload quotas and tracks upload history.
- **`IUrlParserService`**: Identifies and validates replay source URLs *(coming soon)*.

## Upcoming Features

- **Enhanced URL Parser**: Improved support for additional replay sources and better URL validation.
- **Replay Metadata Viewer**: View detailed match information directly in GenHub.
