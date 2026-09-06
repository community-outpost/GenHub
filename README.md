# GenHub

Launcher for C&C: Generals and Zero Hour with patch management and mod support

## Features

- [ ] Easy launching of both C&C: Generals and Zero Hour
- [ ] Automatic patch management and updates
- [ ] Comprehensive mod support with easy installation
- [ ] Authoritative vanilla game installation validation via [CSV Registry](docs/GameInstallationFilesRegistry/)
- [ ] Multi-language installation detection and verification across 10 official game locales
- [ ] Compatibility fixes for Windows 10/11

## Installing on macOS

GenHub is not signed with an Apple Developer ID, so macOS quarantines it after
download and Gatekeeper refuses to open it. Clear the quarantine attribute once,
before the first launch:

```sh
xattr -dr com.apple.quarantine /Applications/GenHub.app
```

You can instead open **System Settings → Privacy & Security**, find the blocked-app
notice after a failed launch attempt, and choose **Open Anyway**. The Control-click →
*Open* shortcut no longer works for unsigned apps; Apple removed it in macOS 15.

Prefer the command. macOS propagates quarantine from a quarantined application to the
files it writes, and if GenHub is still marked when it first runs, that can reach the
game files it prepares. GenHub clears the attribute from the game executables it
materializes, so the game itself launches either way — but clearing it on the app up
front avoids the situation entirely.

None of this applies to a build you compiled yourself. Quarantine is only attached to
downloaded files.

## Documentation

For detailed documentation and guides, visit our [Wiki](https://generalshub.netlify.app/wiki/).

## Contributing

We welcome all forms of contribution — whether it’s coding, reviewing pull requests, reporting issues, giving feedback, or helping with testing.  
Please read our [CONTRIBUTING](CONTRIBUTING.md) guide for details on how to get involved.

## Contact

Join our Discord server for support, suggestions, and community discussions: [Community Outpost Discord](https://discord.gg/ZGtT3Qwd3Y)

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.
