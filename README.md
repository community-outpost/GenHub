# GenHub - Universal C&C Launcher

[![License](https://img.shields.io/github/license/community-outpost/GenHub)](LICENSE)
[![Release](https://img.shields.io/github/v/release/community-outpost/GenHub)](https://github.com/community-outpost/GenHub/releases)
[![Issues](https://img.shields.io/github/issues/community-outpost/GenHub)](https://github.com/community-outpost/GenHub/issues)
[![Discord](https://img.shields.io/discord/1077717467645169724?color=7289da&label=Discord&logo=discord&logoColor=white)](https://discord.gg/ZGtT3Qwd3Y)

The modern, cross-platform launcher and workspace manager for Command & Conquer: Generals and Zero Hour. Manage isolated game workspaces, install community mods and patches with one click, run replays seamlessly, and jump straight into modern multiplayer matches — all without ever breaking your vanilla game installation.

## Key Features

- 🎮 **Universal C&C Workspace Management** - Isolate configurations, mods, and versions with zero cross-contamination.
- ⚡ **One-Click Patch & Mod Installer** - Effortlessly fetch, verify, and maintain popular community distributions including Generals Online and TheSuperHackers releases.
- 🗺️ **Integrated Map & Replay Manager** - Direct integration to import, parse, preview, and share custom maps and competitive match replays.
- 🐧 **Cross-Platform Support** - Support across modern Windows, Linux (via Wine/Proton and Flatpak Steam detection), and macOS.
- 🔄 **Automated App Updates** - Smooth background auto-updates on Windows and Linux, powered by the Velopack runtime.

## Running on macOS

On macOS, binaries downloaded from GitHub releases are quarantined by Gatekeeper. Because
GenHub relies on dynamic code generation (which Avalonia and the runtime JIT use heavily),
quarantined execution will either abort on launch or be killed by `amfid`.

Before running an unpacked release for the first time, strip the quarantine flag:

```bash
xattr -dr com.apple.quarantine GenHub.app
```

or on the standalone executable:

```bash
xattr -d com.apple.quarantine GenHub
```

This applies only to the GenHub launcher itself; it is not required for the underlying
game files it prepares. GenHub clears the attribute from the game executables it
materializes, so the game itself launches either way — but clearing it on the app up
front avoids the situation entirely.

None of this applies to a build you compiled yourself. Quarantine is only attached to
downloaded files.

## Documentation

For detailed documentation and guides, visit our [Wiki](https://wiki.generalshub.com/).

## Contributing

We welcome all forms of contribution — whether it’s coding, reviewing pull requests, reporting issues, giving feedback, or helping with testing.  
Please read our [CONTRIBUTING](CONTRIBUTING.md) guide for details on how to get involved.

## Contact

Join our Discord server for support, suggestions, and community discussions: [Community Outpost Discord](https://discord.gg/ZGtT3Qwd3Y)

## License

This project is licensed under the GPL-3.0 License - see the [LICENSE](LICENSE) file for details.
