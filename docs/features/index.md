---
title: Features Overview
description: Comprehensive guide to GeneralsHub's core features and capabilities
---

GeneralsHub provides a comprehensive suite of features designed to enhance your
Command & Conquer: Generals and Zero Hour experience.  
This section covers the core functionality that makes GeneralsHub the ultimate
launcher and content platform for the Generals community.

---

## Core Features

### [App Update & Installer](/velopack-integration)

Professional installer and automatic update system powered by Velopack. Provides
zero-config installers for Windows and Linux with delta updates delivered
from GitHub Releases. See the [Velopack Integration Guide](/velopack-integration) for
details on building releases, versioning, and deployment.

---

### [Content System](./content)

Comprehensive content management for game files, mods, patches, and community
creations. Supports multiple content providers (including GitHub repositories)
and provides discovery, resolution, and delivery pipelines.

---

### [Game Installations](./game-installations)

Automatic detection and management of game installations across multiple platforms.
Scans for Command & Conquer Generals and Zero Hour installations (Steam, EA App, CD/ISO),
validates integrity, and provides seamless integration with launching and content management features.

---

### [Manifest Service](./manifest)

Content manifest generation and validation system that ensures game files are
complete, properly structured, and ready for launching. Manifests describe
installable content packages and their dependencies.

---

### [Storage & CAS](./storage)

Multi-pool Content Addressable Storage (CAS) system for efficient file storage,
deduplication, and integrity verification. Features separate pools for user content
(Primary) and game installations (Installation) to support cross-drive scenarios
and hard-link optimization. Provides atomic operations, garbage collection, and
concurrent access safety.

---

### [Content Reconciliation](./reconciliation)

Unified content reconciliation system for profile updates and CAS lifecycle management.
Enforces correct execution order for content replacement, removal, and garbage collection
operations. Provides atomic operations, event pipeline, and complete audit trail.

---

### [Game Settings](./game-settings)

Comprehensive game configuration management supporting all Options.ini settings
including video, audio, network, and gameplay options. Features profile-specific
settings, validation, and seamless integration with game launches.

---

### [Validation System](./validation)

Multi-level validation system for:

- Game installation integrity  
- Content compatibility  
- Workspace consistency  

Automatically detects and resolves conflicts to ensure stable gameplay.

---

### [Workspace Management](./workspace)

Dynamic workspace management for assembling isolated game environments from
multiple content sources. Supports both **copy** and **symlink** strategies for
flexibility and performance.

---

### [Launching](./launching)

Advanced game launching system with:

- Profile switching  
- **User data safety confirmation** (prevents unintended data loss when switching profiles)
- Mod and add-on loading  
- Process monitoring and error handling  
- Runtime performance tracking  

---

### [User Data Management](./userdata)

Comprehensive system for managing user-generated content (maps, replays, save games) across game profiles. Ensures data integrity and prevents accidental loss when switching between profiles.

**Core Capabilities:**
- **Profile Isolation**: Each profile maintains its own set of active user data
- **Hard Link Efficiency**: Uses hard links to avoid duplicating files on disk
- **Automatic Tracking**: Monitors which files belong to which profiles
- **Smart Switching**: Analyzes data impact before profile switches
- **Data Retention**: Option to transfer user data between profiles

**Profile Lifecycle:**
- **Preparation**: Installs and activates user data when launching a profile
- **Switching**: Deactivates old profile data, activates new profile data
- **Updates**: Adds/removes user data when profile content changes
- **Cleanup**: Removes all user data when a profile is deleted

**Safety Features:**
- **Switch Analysis**: Pre-flight check identifies data that would be removed
- **User Confirmation**: Non-blocking prompt with "Add to Profile" or "Remove" options
- **Visual Feedback**: Shows file count and total size of affected data
- **Large Transfer Warning**: Alerts when linking >100 files (potential performance impact)
- **Semi-transparent Overlay**: Confirmation appears directly on profile card

---

### [Game Profiles](./gameprofiles)

Create and manage **GameProfiles** to launch instances of Generals or Zero Hour
with specific configurations. Profiles support:

- Game version selection  
- Mods, add-ons, and patches  
- Dedicated workspaces for custom strategies  

---

### [Notifications](./notifications)

Modern toast notification system for user feedback and status updates. Provides:

- Four notification types (Info, Success, Warning, Error)
- Auto-dismiss with configurable timeout
- Thread-safe operation from any context
- Smooth fade animations
- Optional action buttons

---

### [Desktop Shortcuts](./desktop-shortcuts)

Create native desktop shortcuts that launch game profiles directly. Features include:

- One-click profile launching from desktop
- Single-instance handling via IPC
- Platform-specific shortcuts (Windows `.lnk`, Linux `.desktop`)

---

## Feature Architecture

Each feature is designed as a **modular component** that can be used
independently or integrated with others. The architecture follows these
principles:

- **Separation of Concerns**: Each feature handles a specific domain  
- **Dependency Injection**: Features are loosely coupled through DI  
- **Result Pattern**: Consistent error handling across all features  
- **Async/Await**: All operations support asynchronous execution  
- **Observable Progress**: Long-running operations provide progress updates  

---

## Integration Points

Features integrate through well-defined interfaces and shared models:

- **Result Pattern**: All features return structured results  
- **Progress Reporting**: `IProgress<T>` for operation status  
- **Cancellation**: `CancellationToken` support for all operations  
- **Logging**: Structured logging with `Microsoft.Extensions.Logging`  
- **Configuration**: Unified configuration system (`IAppConfiguration`,`IUserSettingsService`, `IConfigurationProviderService`)
- **Danger Zone**: Destructive maintenance operations for advanced data management

---

## Feature Categories

- **Installation & Setup**: Game installation detection, validation, and configuration  
- **Content Management**: Mods, patches, add-ons, and community content  
- **Data Maintenance**: [Danger Zone](./danger-zone.md), CAS cleanup, and workspace management
- **Profile Management**: Game profiles, workspaces, and custom configurations  
- **Launching & Execution**: Advanced launch options and runtime monitoring  
- **Maintenance**: Updates, validation, and system health monitoring  

---

## Getting Started

To get started with GeneralsHub features:

1. Review the [Architecture Overview](../architecture.md) for system context  
2. Explore individual feature documentation in this section  
3. Check the [API Reference](../dev/index.md) for implementation details  
4. Review the [System Flowcharts](../FlowCharts/) for operational workflows  

---
