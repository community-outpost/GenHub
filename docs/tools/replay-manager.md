# Replay Manager

The Replay Manager is a built-in tool in GenHub that allows you to manage, import, and share your Command & Conquer: Generals and Zero Hour replay files with ease.

## Features

- **Unified View**: See all your replays for both Generals and Zero Hour in one place.
- **Easy Import**: Import replays directly from URLs or by dragging and dropping files.
- **Cloud Sharing**: Share your best matches instantly via UploadThing.
- **Local Export**: Bundle multiple replays into a ZIP archive for local storage or manual sharing.
- **Conflict Resolution**: Automatically handles duplicate filenames during import.

## Getting Started

To access the Replay Manager:

1. Open GenHub.

2. Navigate to the **TOOLS** tab.
3. Select **Replay Manager** from the sidebar.

## Importing Replays

### From URL


You can import replays from various sources by pasting the link into the import bar:

- **UploadThing**: Direct links from other GenHub users.

- **Generals Online**: Match view URLs.
- **GenTool**: Directory URLs from the GenTool data repository.
- **Direct Links**: Any URL ending in `.rep` or `.zip`.

### Drag and Drop


Simply drag one or more `.rep` or `.zip` files from your computer and drop them anywhere on the Replay Manager window to import them.

## Sharing Replays

1. Select the replays you want to share from the list.
2. Click **Upload & Share**.
3. Once the upload is complete, the download link will be copied to your clipboard automatically.

> [!IMPORTANT]
> **Size Limit**: Each individual replay or ZIP file must be under **1 MB**.
> **Privacy**: Shared replays are maintained for up to 14 days or until storage is full.

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
