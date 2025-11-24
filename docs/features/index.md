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
Scans for Command & Conquer Generals and Zero Hour installations, validates integrity,
and provides seamless integration with launching and content management features.

---

### [Manifest Service](./manifest)

Content manifest generation and validation system that ensures game files are
complete, properly structured, and ready for launching. Manifests describe
installable content packages and their dependencies.

---

### [Storage & CAS](./storage)

Content Addressable Storage (CAS) system for efficient file storage,
deduplication, and integrity verification. Provides atomic operations, garbage
collection, and concurrent access safety.

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
- Mod and add-on loading  
- Process monitoring and error handling  
- Runtime performance tracking  

---

### [Game Profiles](./gameprofiles)

Create and manage **GameProfiles** to launch instances of Generals or Zero Hour
with specific configurations. Profiles support:

- Game version selection  
- Mods, add-ons, and patches  
- Dedicated workspaces for custom strategies  

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

---

## Feature Categories

- **Installation & Setup**: Game installation detection, validation, and configuration  
- **Content Management**: Mods, patches, add-ons, and community content  
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
