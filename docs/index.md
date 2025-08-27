---
# https://vitepress.dev/reference/default-theme-home-page
layout: home

hero:
  name: "GeneralsHub"
  text: "C&C Launcher"
  tagline: Cross-platform launcher for Command & Conquer Generals and Zero Hour
  image:
    src: /assets/icon.png
    alt: GeneralsHub Icon
  actions:
    - theme: brand
      text: Get Started
      link: /onboarding
    - theme: alt
      text: View Architecture
      link: /architecture
    - theme: alt
      text: View on GitHub
      link: https://github.com/community-outpost/GenHub

features:
  - title: Universal Game Support
    details: Supports multiple game versions, forks, and community builds from Steam, EA App, Origin, and manual installations.
    icon: 🎮
  - title: Content Discovery
    details: Automated discovery and installation of mods, patches, and add-ons from GitHub, ModDB, CNCLabs, and local sources.
    icon: 🔍
  - title: Isolated Workspaces
    details: Each game profile runs in its own isolated workspace, preventing conflicts between different configurations.
    icon: 📁
  - title: Cross-Platform
    details: Native support for Windows and Linux with platform-specific optimizations.
    icon: 🌐
  - title: Three-Tier Architecture
    details: Sophisticated content pipeline with orchestrator, providers, and specialized pipeline components.
    icon: 🏗️
  - title: Developer Friendly
    details: Clean architecture, comprehensive testing, and extensive documentation for contributors.
    icon: 👥
---

## What is GeneralsHub?

GeneralsHub is a sophisticated, multi-layered launcher designed to solve the fundamental problem of C&C Generals/Zero Hour ecosystem fragmentation. Since the game went open source, the community has released many forks and patched executables. GeneralsHub consolidates these variants into a single, unified interface.

## Key Features

- **Game Profile Management**: Create custom configurations combining base games with mods, patches, and add-ons
- **Three-Tier Content Pipeline**: A robust system for discovering, resolving, acquiring, and assembling content from multiple sources
- **Workspace Strategies**: Multiple file assembly approaches (Full Copy, Symlink, Hybrid, Hard Link) for optimal performance and compatibility
- **Cross-Platform Support**: Native Windows and Linux support with platform-specific optimizations

## Getting Started

<div class="tip custom-block" style="padding-top: 8px">

Ready to contribute to GeneralsHub? Check out our [Developer Onboarding Guide](./onboarding.md) to get up to speed with the project architecture, development workflow, and contribution standards.

</div>

1. **Detection**: GeneralsHub automatically detects existing game installations
2. **Profile Creation**: Create game profiles by selecting a base version and desired content
3. **Content Discovery**: Browse and install mods from GitHub, ModDB, and other sources
4. **Launch**: Play your customized game configuration in an isolated environment

## Community & Support

- **Discord**: Join our community for real-time discussion and support
- **GitHub**: Contribute code, report issues, and track development progress
- **Documentation**: Comprehensive guides and API documentation
