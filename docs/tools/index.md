# GenHub Tools Overview

GenHub provides a suite of integrated tools designed to enhance your Command & Conquer: Generals and Zero Hour experience. These tools streamline content management, sharing, and organization, making it easier to manage replays, maps, and game modifications.

## Available Tools

GenHub currently offers two fully-featured tools with a third in development:

1. **Replay Manager** - Manage, import, and share replay files
2. **Map Manager** - Manage, import, and share custom maps with MapPack support
3. **Publisher Studio** (Future) - Create and distribute custom content catalogs

All tools are accessible from the **TOOLS** tab in the GenHub interface and share common features like cloud uploading, import/export capabilities, and seamless integration with game profiles.

---

## Replay Manager

The Replay Manager provides a centralized interface for managing your Command & Conquer replay files across both Generals and Zero Hour.

### Key Features

- **Unified replay library** for both Generals and Zero Hour
- **Multi-source import** from URLs (UploadThing, Generals Online, GenTool, direct links)
- **Drag-and-drop support** for `.rep` and `.zip` files
- **Cloud sharing** via UploadThing with automatic link copying
- **Batch operations** with multi-selection support (Ctrl+Click, Shift+Click)
- **In-place renaming** by double-clicking replay names
- **ZIP archive creation** for local backup and manual sharing
- **Upload history tracking** with quota management
- **Conflict resolution** for duplicate filenames during import
- **Quick access** to replay directories via File Explorer integration

### Storage Locations

- **Generals**: `Documents\Command and Conquer Generals Data\Replays`
- **Zero Hour**: `Documents\Command and Conquer Generals Zero Hour Data\Replays`

### Upload Limits

- **File size**: Maximum 1 MB per replay or ZIP file
- **Retention**: Files maintained for up to 14 days
- **Quota management**: Remove items from upload history to free up quota

### Use Cases

- Share competitive matches with friends or community members
- Import tournament replays for analysis
- Organize and backup your best gameplay moments
- Batch export replays for archival purposes

[View Full Replay Manager Documentation](./replay-manager.md)

---

## Map Manager

The Map Manager extends the replay management concept to custom maps, with additional features like MapPacks for organizing map collections.

### Key Features

- **Unified map library** for both Generals and Zero Hour
- **Multi-source import** from URLs (UploadThing, direct links)
- **Drag-and-drop support** for `.map` and `.zip` files
- **Cloud sharing** via UploadThing with automatic link copying
- **MapPacks system** for organizing maps into named collections
- **Batch operations** with multi-selection support
- **In-place renaming** by double-clicking map names
- **ZIP archive creation** for local backup and manual sharing
- **Upload history tracking** with quota management
- **Map validation** to detect missing preview images (TGA files)
- **Quick access** to map directories via File Explorer integration

### MapPacks Feature

MapPacks are a unique feature that allows you to create named collections of maps for different purposes:

- **Organize maps** by game mode, theme, or tournament
- **Profile integration** - Load specific MapPacks for different game profiles
- **Metadata-based** - MapPacks store references, not duplicate files
- **Userdata integration** - Automatically managed by GenHub's userdata system
- **Easy switching** - Load/unload MapPacks with a single click

### Storage Locations

- **Generals**: `Documents\Command and Conquer Generals Data\Maps`
- **Zero Hour**: `Documents\Command and Conquer Generals Zero Hour Data\Maps`

### Upload Limits

- **File size**: Maximum 5 MB per map file
- **Retention**: Files maintained for up to 14 days
- **Quota management**: Remove items from upload history to free up quota

### Use Cases

- Share custom maps with the community
- Create tournament map packs for competitive play
- Organize maps by theme or game mode
- Manage different map sets for different profiles
- Validate maps before distribution to prevent crashes

[View Full Map Manager Documentation](./map-manager.md)

---

## Publisher Studio (Future)

Publisher Studio is an upcoming tool that will enable content creators to publish and distribute custom content through GenHub's catalog system.

### Planned Features

- **Publisher registration** with support for multiple hosting platforms:
  - Google Drive
  - GitHub Releases
  - ModDB
  - Direct CDN links
- **Catalog management** for organizing releases and versions
- **Content definitions** with file filtering and dependency management
- **Variant support** for multiple builds (resolution variants, language packs, etc.)
- **Dependency specifications** with version constraints
- **Release management** with changelog and version tracking
- **Automated distribution** through GenHub's content acquisition system

### Architecture Highlights

Publisher Studio will follow the same pattern as the existing GeneralsOnline integration:

1. **Provider Definition** - Publisher metadata (static configuration)
2. **Catalog** - Content listings (dynamic, updated by publisher)
3. **Release-based distribution** - One download per release
4. **Post-extraction splitting** - Multiple manifests from single download
5. **Data-driven content definitions** - Configurable file filtering and dependencies

### Use Cases

- Distribute custom mods through GenHub
- Publish map packs with automatic updates
- Manage multiple versions of content
- Create addon content with dependency management
- Provide variant builds for different configurations

### Current Status

Publisher Studio is in the design phase. The architecture document is available at `PUBLISHER_STUDIO_ARCHITECTURE.md` in the repository root. The system will build upon the existing GeneralsOnline integration pattern, extending it with data-driven content definitions and multi-release support.

---

## Feature Comparison

| Feature | Replay Manager | Map Manager |
|---------|---------------|-------------|
| **Content Type** | Replay files (.rep) | Map files (.map + assets) |
| **Game Support** | Generals, Zero Hour | Generals, Zero Hour |
| **Import Sources** | UploadThing, Generals Online, GenTool, Direct URLs | UploadThing, Direct URLs |
| **File Formats** | .rep, .zip | .map, .zip |
| **Cloud Upload** | ✅ (1 MB limit) | ✅ (5 MB limit) |
| **Drag & Drop** | ✅ | ✅ |
| **Multi-Selection** | ✅ | ✅ |
| **In-Place Rename** | ✅ | ✅ |
| **ZIP Export** | ✅ | ✅ |
| **ZIP Import** | ✅ | ✅ |
| **Upload History** | ✅ | ✅ |
| **Quota Management** | ✅ | ✅ |
| **Collections** | ❌ | ✅ (MapPacks) |
| **Validation** | ❌ | ✅ (TGA detection) |
| **Profile Integration** | ❌ | ✅ (via MapPacks) |
| **Userdata Integration** | ❌ | ✅ (via MapPacks) |

---

## Common Features

All GenHub tools share a consistent set of features and behaviors:

### Import/Export

- **URL Import**: Paste links from supported sources and import with one click
- **Drag & Drop**: Drop files directly onto the tool interface
- **File Browser**: Use the native file picker for traditional file selection
- **ZIP Support**: Import and export ZIP archives containing multiple files
- **Conflict Resolution**: Automatic handling of duplicate filenames

### Sharing

- **Cloud Upload**: Share files via UploadThing with automatic link generation
- **Link Copying**: Download links automatically copied to clipboard
- **Upload History**: Track all uploads with status indicators
- **Quota Management**: Remove old uploads to free up space
- **Retention Policy**: Files maintained for up to 14 days

### Cloud Upload (UploadThing)

GenHub uses UploadThing as its cloud storage provider for sharing content:

- **Automatic uploads** with progress tracking
- **Link generation** with clipboard integration
- **Upload history** with status tracking (active/expired)
- **Quota management** - Remove items to free up space
- **Privacy-focused** - Files maintained for 14 days or until storage is full

### Validation

- **File format checking** to ensure compatibility
- **Size limit enforcement** before upload
- **Integrity verification** for imported files
- **Map-specific validation** (Map Manager only) for missing assets

---

## Getting Started

### Accessing Tools

1. Launch GenHub
2. Navigate to the **TOOLS** tab in the main interface
3. Select the desired tool from the sidebar:
   - **Replay Manager**
   - **Map Manager**

### Basic Workflow

1. **Import Content**
   - Paste a URL and click the import button
   - Drag and drop files onto the interface
   - Use the browse button to select files

2. **Manage Content**
   - Use the search bar to filter items
   - Select items using Ctrl+Click or Shift+Click
   - Double-click names to rename
   - Click the folder button to open the directory

3. **Export/Share Content**
   - Select items to export
   - Click ZIP to create a local archive
   - Click Upload to share via cloud
   - View upload history for shared links

### Tips and Tricks

- **Keyboard Shortcuts**:
  - `Ctrl+A` - Select all items
  - `Ctrl+Click` - Toggle individual selection
  - `Shift+Click` - Select range
  - `Double-Click` - Rename item

- **Batch Operations**:
  - Select multiple items for bulk delete, ZIP, or upload
  - Use search to filter before selecting all
  - Check the selected count in the bottom bar

- **Upload Management**:
  - Remove old uploads from history to free quota
  - Check status indicators (green = active, red = expired)
  - Copy links directly from upload history

- **Organization**:
  - Use descriptive filenames for easier searching
  - Create MapPacks for different game modes or profiles
  - Export important content to ZIP for backup

---

## Integration

### Game Profile Integration

GenHub tools integrate seamlessly with the game profile system:

- **Replay Manager**: Replays stored in standard game directories for automatic detection
- **Map Manager**: Maps stored in standard game directories with MapPack support
- **MapPacks**: Load specific map collections per profile via userdata system

### Content Management

All tools follow GenHub's content management principles:

- **Standard directories**: Use official game directories for compatibility
- **Non-destructive operations**: Original files preserved during operations
- **Conflict resolution**: Automatic handling of duplicate filenames
- **Metadata tracking**: Upload history and MapPack definitions stored separately

### Storage System

GenHub tools use a consistent storage approach:

- **Local Storage**: Files stored in standard game directories
- **Cloud Storage**: UploadThing for temporary sharing (14-day retention)
- **Metadata Storage**: Tool-specific data stored in GenHub's userdata system
- **Archive Support**: ZIP files for bundling and distribution

### Userdata System Integration

The Map Manager's MapPack feature integrates with GenHub's userdata system:

- **Profile-specific maps**: Load different MapPacks for different profiles
- **Automatic management**: Userdata service handles file linking and cleanup
- **Metadata-based**: MapPacks store references, not duplicate files
- **Seamless switching**: Load/unload MapPacks without manual file management

---

## Architecture

### Service-Based Design

All GenHub tools follow a modular service architecture:

#### Replay Manager Services

- **`IReplayDirectoryService`**: Directory operations and file system access
- **`IReplayImportService`**: Import from URLs, files, and archives
- **`IReplayExportService`**: Export and cloud sharing
- **`IUploadRateLimitService`**: Upload quota and history tracking
- **`IUrlParserService`**: URL validation and source identification

#### Map Manager Services

- **`IMapDirectoryService`**: Directory operations and file system access
- **`IMapImportService`**: Import from URLs, files, and archives
- **`IMapExportService`**: Export and cloud sharing
- **`IMapPackService`**: MapPack creation, loading, and storage

### Common Patterns

All tools share common architectural patterns:

- **Service interfaces** for dependency injection and testability
- **Operation results** for consistent error handling
- **Progress reporting** for long-running operations
- **Cancellation support** for user-initiated cancellations
- **Event-driven updates** for UI synchronization

### Future Extensibility

The architecture supports future enhancements:

- **Plugin system** for custom import sources
- **Enhanced validation** with detailed error reporting
- **Metadata extraction** for replays and maps
- **Advanced search** with filtering and sorting
- **Cloud sync** for cross-device content management

---

## Troubleshooting

### Common Issues

**Import fails from URL**

- Verify the URL is accessible and points to a valid file
- Check your internet connection
- Ensure the source supports direct downloads

**Upload fails**

- Check file size limits (1 MB for replays, 5 MB for maps)
- Verify you haven't exceeded your upload quota
- Remove old uploads from history to free space

**Files not appearing in game**

- Click the refresh button to reload the file list
- Verify files are in the correct game directory
- Check that file extensions are correct (.rep for replays, .map for maps)

**MapPack not loading**

- Ensure the MapPack is marked as loaded (green badge)
- Verify the maps in the MapPack still exist
- Check that the profile is configured correctly

### Getting Help

If you encounter issues with GenHub tools:

1. Check the tool-specific documentation for detailed guidance
2. Visit the GenHub Discord for community support
3. Report bugs on the GitHub repository
4. Check the upload history for failed uploads

---

## Future Development

### Planned Enhancements

**Replay Manager**

- Enhanced URL parser with more source support
- Replay metadata viewer for match details
- Advanced search and filtering
- Replay analysis integration

**Map Manager**

- Enhanced map validation with detailed reports
- Map metadata extraction (player count, size, etc.)
- MapPack sharing via cloud
- Thumbnail generation for maps without previews

**Publisher Studio**

- Full catalog management interface
- Multi-platform hosting support
- Automated release workflows
- Dependency resolution and validation

### Community Feedback

GenHub tools are continuously improved based on community feedback. Suggestions and feature requests are welcome through:

- GitHub Issues
- Discord community
- In-app feedback system

---

## Summary

GenHub's tool suite provides comprehensive content management for Command & Conquer: Generals and Zero Hour. The Replay Manager and Map Manager offer powerful features for importing, organizing, and sharing game content, while the upcoming Publisher Studio will enable content creators to distribute custom modifications through GenHub's integrated catalog system.

All tools share a consistent interface, common features like cloud uploading and batch operations, and seamless integration with GenHub's profile and userdata systems. Whether you're managing replays, organizing maps, or preparing to distribute custom content, GenHub tools provide the functionality you need with a streamlined, user-friendly experience.
