# Velopack Integration Guide

GenHub uses [Velopack](https://velopack.io/) for application installation and automatic updates across Windows and Linux platforms.

## Overview

Velopack provides:
- **Zero-config installers** - Generate installers automatically from your build output
- **Automatic updates** - Delta updates that only download changed files
- **Cross-platform** - Single solution for Windows and Linux
- **GitHub Releases integration** - Seamless updates from GitHub

## How It Works

### 1. Application Startup

When GenHub starts, `VelopackApp.Build().Run()` is called first in `Program.cs`. This handles:
- First-run initialization
- Update installation on startup
- Install/uninstall hooks
- Update preparation

### 2. Update Checking

The `VelopackUpdateManager` service checks for updates from GitHub Releases:

```csharp
var updateInfo = await _velopackUpdateManager.CheckForUpdatesAsync();
if (updateInfo != null)
{
    // Update available
    await _velopackUpdateManager.DownloadUpdatesAsync(updateInfo, progress);
    _velopackUpdateManager.ApplyUpdatesAndRestart(updateInfo);
}
```

### 3. Update Flow

1. **Check** - Application queries GitHub Releases for new versions
2. **Download** - Delta packages are downloaded (only changes)
3. **Apply** - Update is staged and applied on next restart
4. **Restart** - Application restarts with new version

## Building Releases

### Prerequisites

- .NET 8 SDK
- Velopack CLI tool: `dotnet tool install -g vpk`

### Manual Build Process

#### Windows

```powershell
# Publish the application
dotnet publish GenHub/GenHub.Windows/GenHub.Windows.csproj `
  -c Release `
  --self-contained `
  -r win-x64 `
  -o ./publish/windows

# Package with Velopack
vpk pack `
  --packId GenHub `
  --packVersion 1.0.0 `
  --packDir ./publish/windows `
  --mainExe GenHub.Windows.exe `
  --packTitle "GenHub" `
  --packAuthors "Community Outpost" `
  --icon GenHub/GenHub/Assets/Icons/generalshub.ico `
  --outputDir ./releases

# For alpha/beta releases, use semantic versioning prerelease identifiers:
# vpk pack ... --packVersion 1.0.0-alpha.1
# vpk pack ... --packVersion 1.0.0-beta.2
# vpk pack ... --packVersion 1.0.0-rc.1
```

#### Linux

```bash
# Publish the application
dotnet publish GenHub/GenHub.Linux/GenHub.Linux.csproj \
  -c Release \
  --self-contained \
  -r linux-x64 \
  -o ./publish/linux

# Package with Velopack
vpk pack \
  --packId GenHub \
  --packVersion 1.0.0 \
  --packDir ./publish/linux \
  --mainExe GenHub.Linux \
  --packTitle "GenHub" \
  --packAuthors "Community Outpost" \
  --icon GenHub/GenHub/Assets/Icons/generalshub.ico \
  --outputDir ./releases

# For alpha/beta releases:
# vpk pack ... --packVersion 1.0.0-alpha.1
# vpk pack ... --packVersion 1.0.0-beta.2
```

### Automated CI/CD

GenHub includes a GitHub Actions workflow (`.github/workflows/release.yml`) that automatically:

1. Builds releases for Windows and Linux
2. Packages them with Velopack
3. Creates a GitHub Release with installers
4. Publishes update feed for automatic updates

**To trigger a release:**

```bash
git tag v1.0.0
git push origin v1.0.0
```

Or manually through GitHub Actions workflow dispatch.

## Release Artifacts

After packaging, Velopack generates:

- **GenHub-win-Setup.exe** (Windows) - Full installer that installs to `%LOCALAPPDATA%\GenHub`
- **GenHub-{Version}-full.nupkg** - Full release package
- **GenHub-{Version}-delta.nupkg** - Delta update package (if previous version exists)
- **GenHub-win-Portable.zip** - Portable version (no installation required)
- **RELEASES** - Update feed manifest

Upload all artifacts to GitHub Releases. Velopack will automatically serve updates.

## Installation Locations

### Windows Installation

- **Installation Directory**: `%LOCALAPPDATA%\GenHub\` (e.g., `C:\Users\YourName\AppData\Local\GenHub\`)
- **User Data**: `%APPDATA%\GenHub\`
- **Update Cache**: `%LOCALAPPDATA%\GenHub\`

**Note**: Velopack uses a "one-click" installer that always installs to LocalAppData. This location:

- Does not require administrator privileges
- Is standard for modern auto-updating applications (VS Code, Discord, Slack, etc.)
- Enables seamless automatic updates
- Is isolated per-user for better security

### Linux Installation

- **Installation Directory**: `/usr/local/bin/GenHub/` or `~/.local/share/GenHub/`
- **User Data**: `~/.config/GenHub/`
- **Update Cache**: `~/.cache/GenHub/`

The app ID `GenHub` ensures clean, predictable installation paths without vendor prefixes.

## Release Channels and Prerelease Versions

Velopack supports **semantic versioning prerelease identifiers** for alpha, beta, and release candidate builds. You can use semver formats without needing separate channels:

### Semantic Versioning Examples

**Version Format:** `MAJOR.MINOR.PATCH[-PRERELEASE]`

```bash
# Stable release
vpk pack ... --packVersion 1.0.0

# Alpha releases (early testing) - increment the number after alpha
vpk pack ... --packVersion 1.0.0-alpha.1
vpk pack ... --packVersion 1.0.0-alpha.2
vpk pack ... --packVersion 1.0.0-alpha.3

# Beta releases (feature complete, testing)
vpk pack ... --packVersion 1.0.0-beta.1
vpk pack ... --packVersion 1.0.0-beta.2

# Release candidates
vpk pack ... --packVersion 1.0.0-rc.1
vpk pack ... --packVersion 1.0.0-rc.2

# Final stable release
vpk pack ... --packVersion 1.0.0

# Next feature version - start over with alpha
vpk pack ... --packVersion 1.1.0-alpha.1
vpk pack ... --packVersion 1.1.0-alpha.2
vpk pack ... --packVersion 1.1.0-beta.1
vpk pack ... --packVersion 1.1.0

# Patch release
vpk pack ... --packVersion 1.1.1

# Major version bump
vpk pack ... --packVersion 2.0.0-alpha.1
```

**Versioning Flow:**

```text
1.0.0-alpha.1 → 1.0.0-alpha.2 → ... → 1.0.0-beta.1 → 1.0.0-beta.2 → 1.0.0
                                                                        ↓
1.1.0-alpha.1 → 1.1.0-alpha.2 → ... → 1.1.0-beta.1 → 1.1.0 ←─────────┘
```

### How Velopack Handles Prereleases

- **Stable users** (installed from a stable version like `1.0.0`) will ONLY receive stable updates
- **Prerelease users** (installed from `1.0.0-beta.1`) will receive ALL updates including prereleases
- Versioning follows [SemVer 2.0](https://semver.org/) rules
- Users on prereleases automatically upgrade to stable when available

### Using Channels for Different Features

If you need separate feature branches (e.g., stable vs experimental features), use the `--channel` parameter:

```bash
# Stable channel (default: "win" for Windows, "linux" for Linux)
vpk pack ... --packVersion 1.0.0

# Beta feature channel
vpk pack ... --packVersion 1.0.0 --channel win-beta

# Experimental channel
vpk pack ... --packVersion 1.0.0 --channel win-experimental
```

**Note**: Once a user installs from a specific channel, they only receive updates from that channel unless they explicitly switch.

### Recommended Strategy

For most use cases, use **prerelease identifiers** instead of channels:

- ✅ Simpler: No channel management needed
- ✅ Automatic graduation: Beta users get stable updates automatically
- ✅ Clear versioning: Everyone sees the same version numbers

Use channels only if you need persistent feature branches (like stable/nightly builds).

## Update Channel Management

By default, GenHub uses the default update channel. You can create multiple channels (stable, beta, etc.):

```bash
vpk pack ... --channel beta
```

Users will automatically receive updates from their installed channel.

## Testing Updates Locally

### 1. Create Test Release

```powershell
vpk pack --packId GenHub --packVersion 1.0.0 --packDir ./publish --mainExe GenHub.Windows.exe -o ./test-releases
```

### 2. Create Second Version

Update your code, then:

```powershell
vpk pack --packId GenHub --packVersion 1.0.1 --packDir ./publish --mainExe GenHub.Windows.exe -o ./test-releases --delta ./test-releases/RELEASES
```

### 3. Test Update

Point UpdateManager to local directory:

```csharp
var manager = new UpdateManager("file:///C:/path/to/test-releases");
```

## Architecture

### Components

- **VelopackApp** - Handles application lifecycle hooks
- **VelopackUpdateManager** - Service for checking/downloading/applying updates
- **IVelopackUpdateManager** - Interface for update operations
- **UpdateInfo** - Model containing update metadata
- **UpdateProgress** - Progress reporting model

### Integration Points

- `Program.cs` - VelopackApp.Build().Run() initialization
- `AppUpdateModule.cs` - DI registration
- `VelopackUpdateManager.cs` - Update service implementation

## Troubleshooting

### Updates Not Working in Development

Velopack only works when the app is installed. When running from Visual Studio or `dotnet run`, updates are disabled. This is expected behavior.

### Update Check Fails

- Ensure GitHub repository is public or provide authentication
- Verify GitHub Releases contain Velopack packages
- Check network connectivity

### Version Comparison Issues

Velopack uses semantic versioning (SemVer). Ensure version numbers follow `Major.Minor.Patch` format.

## Additional Resources

- [Velopack Documentation](https://docs.velopack.io/)
- [Velopack GitHub](https://github.com/velopack/velopack)
- [GitHub Releases Guide](https://docs.github.com/en/repositories/releasing-projects-on-github)
