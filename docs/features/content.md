---
title: Content Management
description: Handle game content, mods, and community creations
---

# Content Management

The Content Management feature provides comprehensive tools for handling game content, mods, and community creations. Browse, install, and manage all your custom content in one centralized interface.

## Overview

GenHub's content system supports:

- Mod installation and management
- Community content browsing
- Patch and balance mod handling
- Add-on management (Gentool, ControlBar, Hotkeys)
- Content validation and compatibility checking

## Content Types

### Mods

- **ROTR (Rise of the Reds)**: Popular community mod
- **Contra X**: Alternative faction mod
- **Balance Patches**: Gameplay adjustment mods
- **Visual Enhancements**: Graphics and UI improvements

### Add-ons

- **Gentool**: Enhanced game tools and utilities
- **ControlBar**: Improved command & conquer interface
- **Hotkeys**: Customizable keyboard shortcuts
- **Map Editors**: Community-created map creation tools

### Patches

- **Official Patches**: EA-released game updates
- **Community Patches**: Bug fixes and improvements
- **Balance Patches**: Gameplay modifications

### Tools & Executables

- **WorldBuilder**: Official map creation tool
- **Modding Utilities**: Custom executables for game modification and management
- **Tool**: (formerly ModdingTool) Dedicated content type for modding tools
- **Executable**: Generic standalone executable support

### Games

- **Game**: (formerly GameClient) Support for game installations (e.g. generals.exe) imported as content

## Content Discovery

### Browse Content

1. Navigate to Content → Browse
2. Filter by type, popularity, or rating
3. Preview content details and screenshots
4. Check compatibility with your game version

### Search Functionality

- Search by name, author, or tags
- Filter by content type and game version
- Sort by popularity, rating, or date

## Installation Process

### Automatic Installation

1. Select content from browse or search
2. Click "Install"
3. GenHub handles download and file placement
4. Validates installation integrity

### Manual Installation

For custom or local content:

1. Navigate to Content → Import
2. Select content files or folders
3. Specify installation location
4. Configure content settings

## Content Organization

### Organize Content

- Create custom categories and folders
- Tag content for easy searching
- Set content priority and load order
- Manage content dependencies

### Content Profiles

- Save content configurations as profiles
- Switch between different content setups
- Share profiles with the community
- Backup and restore content configurations

### Tool Profiles

Tool Profiles are a specialized classification of `GameProfile` designed for standalone executables (e.g., `WorldBuilder.exe`). Unlike regular game profiles, Tool Profiles:

- **Bypass Game Requirements**: Do not require a base game installation or game client
- **Single Tool Restriction**: Can only contain exactly one content item of type `ModdingTool`
- **Direct Launch**: Launch the tool executable directly, skipping workspace assembly and game-specific preparation

## Compatibility & Validation

### Version Compatibility

- Automatic detection of game version requirements
- Compatibility warnings for mismatched content
- Alternative version suggestions

### Dependency Management

- Track content dependencies
- Automatic dependency resolution
- Conflict detection and resolution

## Content Sources

### Official Sources

- EA Official content and patches
- Verified community repositories
- Trusted mod hosting platforms

### Community Sources

- User-submitted content
- Third-party mod sites
- GitHub repositories
- Discord communities
