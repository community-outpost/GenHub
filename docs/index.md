---
# https://vitepress.dev/reference/default-theme-home-page
layout: home

hero:
  name: "GeneralsHub"
  text: "Universal C&C Launcher"
  tagline: Cross-platform launcher for Command & Conquer Generals and Zero Hour
  actions:
    - theme: brand
      text: View Architecture
      link: /architecture
    - theme: alt
      text: View on GitHub
      link: https://github.com/community-outpost/GenHub

features:
  - title: Universal Game Support
    details: Supports multiple game versions, forks, and community builds from Steam, EA App, Origin, and manual installations.
  - title: Content Discovery
    details: Automated discovery and installation of mods, patches, and add-ons from GitHub, ModDB, CNCLabs, and local sources.
  - title: Isolated Workspaces
    details: Each game profile runs in its own isolated workspace, preventing conflicts between different configurations.
  - title: Cross-Platform
    details: Native support for Windows and Linux with platform-specific optimizations.
---

## What is GeneralsHub?

GeneralsHub is a sophisticated, multi-layered launcher designed to solve the fundamental problem of C&C Generals/Zero Hour ecosystem fragmentation. Since the game went open source, the community has released many forks and patched executables. GeneralsHub consolidates these variants into a single, unified interface.

## Key Features

- **Game Profile Management**: Create custom configurations combining base games with mods, patches, and add-ons
- **Three-Tier Content Pipeline**: A robust system for discovering, resolving, acquiring, and assembling content from multiple sources
- **Workspace Strategies**: Multiple file assembly approaches (Full Copy, Symlink, Hybrid, Hard Link) for optimal performance and compatibility
- **Cross-Platform Support**: Native Windows and Linux support with platform-specific optimizations

## Getting Started

1. **Detection**: GeneralsHub automatically detects existing game installations
2. **Profile Creation**: Create game profiles by selecting a base version and desired content
3. **Content Discovery**: Browse and install mods from GitHub, ModDB, and other sources
4. **Launch**: Play your customized game configuration in an isolated environment
