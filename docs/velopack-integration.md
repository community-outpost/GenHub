# Velopack Integration Guide

GenHub uses [Velopack](https://velopack.io/) for application installation and automatic updates across Windows and Linux platforms.

## Overview

Velopack provides:

- **Zero-config installers** - Generate installers automatically from your build output
- **Automatic updates** - Delta updates that only download changed files
- **Cross-platform** - Single solution for Windows and Linux
- **GitHub Releases integration** - Seamless updates from GitHub
- **CI Artifact Updates** - Subscribe to PR builds for testing

## How It Works

### 1. Application Startup

When GenHub starts, `VelopackApp.Build().Run()` is called first in `Program.cs`. This handles:

- First-run initialization
- Update installation on startup
- Install/uninstall hooks
- Update preparation

### 2. Update Checking

GenHub supports two update sources:

#### GitHub Releases (Stable Channel)

The `VelopackUpdateManager` service checks for stable releases:

```csharp
var updateInfo = await _velopackUpdateManager.CheckForUpdatesAsync();
if (updateInfo != null)
{
    // Update available
    await _velopackUpdateManager.DownloadUpdatesAsync(updateInfo, progress);
    _velopackUpdateManager.ApplyUpdatesAndRestart(updateInfo);
}
```

#### PR Artifacts (Testing Channel)

Users can subscribe to specific Pull Request builds for testing:

```csharp
// Subscribe to PR #3
_userSettingsService.Update(s => s.SubscribedPrNumber = 3);

// Check for PR updates
var prInfo = await _velopackUpdateManager.GetSubscribedPrInfoAsync();
if (prInfo?.LatestArtifact != null)
{
    // Install PR build
    await _velopackUpdateManager.InstallPrArtifactAsync(prInfo, progress);
}
```

### 3. Update Flow

1. **Check** - Application queries GitHub Releases or PR artifacts for new versions
2. **Download** - Packages are downloaded (delta updates when possible)
3. **Apply** - Update is staged and applied
4. **Restart** - Application restarts with new version

## Versioning Scheme

GenHub uses a custom versioning scheme optimized for continuous integration:

### Version Format

```
0.0.{RUN_NUMBER}[-pr{PR_NUMBER}][+{BUILD_METADATA}]
```

**Components:**

- `0.0.X` - Base version (0.0 indicates pre-1.0 development)
- `RUN_NUMBER` - GitHub Actions run number (auto-incrementing)
- `-pr{N}` - Optional PR number for pull request builds
- `+{HASH}` - Build metadata (git commit hash, stripped for comparison)

### Examples

```bash
# Main branch build (run #157)
0.0.157

# PR #3 build (run #160)
0.0.160-pr3

# Full version with build metadata
0.0.160-pr3+051baf8.051baf832a894f542e60d3e4c2471c7d4dd753da
```

### Version Comparison

GenHub strips build metadata (everything after `+`) when comparing versions:

```csharp
// These are considered the same version:
"0.0.160-pr3+051baf8..."
"0.0.160-pr3+18d0017..."
// Both compare as: "0.0.160-pr3"
```

This allows users to reinstall the same PR build with different commits without version conflicts.

## Update Channels

GenHub provides three update channels that users can switch between:

### 1. Stable Channel (Default)

- **Source**: GitHub Releases
- **Versions**: `0.0.X` (no branch/PR suffix)
- **Updates**: Only published releases from the main branch
- **Recommended for**: General production use

### 2. PR Artifacts Channel (PR Subscription)

- **Source**: GitHub Actions CI workflow artifacts
- **Versions**: `0.0.X-prY` format
- **Updates**: Specific Pull Request CI builds
- **Recommended for**: Testing specific feature branches or bug fix pull requests
- **Requires**: GitHub Personal Access Token (PAT) with `repo` scope

#### Subscribing to PR Builds

1. Navigate to Settings → Updates
2. Click "Manage Updates & PRs"
3. In the "Browse Builds" tab, select a pull request
4. Click "Subscribe"

The application will automatically query and notify when newer CI builds are published for that PR.

### 3. Branch Artifacts Channel (Branch Subscription)

- **Source**: GitHub Actions CI workflow artifacts on a branch (e.g., `development`, `main`)
- **Versions**: `0.0.X-branchname` format
- **Updates**: Continuous integration builds on the selected branch
- **Recommended for**: Developers and testers wanting bleeding-edge builds

#### Unsubscribing

1. Open "Manage Updates & PRs"
2. Click "Unsubscribe" on the currently subscribed PR or branch
3. The app returns to the stable release channel

### Periodic Background Update Checks

GenHub supports periodic background update checks configured in **Settings**:
- **Automatic Background Checks**: Enable or disable periodic checks
- **Configurable Interval**: Set between 5 minutes and 7 days (default: 30 minutes)
- **Persistent Notifications & Badges**: Prompts users with a non-intrusive one-click "Update" action in the notification feed
- **Duplicate Prevention**: Notification records are uniquely tracked per update identity (`pr:{prNumber}:{version}`, `branch:{branch}:{version}`, or `release:{version}`) to avoid notification spam

## Building Releases

### Prerequisites

- .NET 8 SDK
- Velopack CLI tool: `dotnet tool install -g vpk`

### Version Number Generation

GenHub uses GitHub Actions run numbers for versioning:

```yaml
# CI builds (PRs and main branch)
VERSION: 0.0.${{ github.run_number }}[-pr${{ github.event.pull_request.number }}]

# Example outputs:
# Main branch: 0.0.157
# PR #3: 0.0.160-pr3
```

### Manual Build Process

#### Windows

```powershell
# Set version
$VERSION = "0.0.157"  # Or "0.0.160-pr3" for PR builds

# Publish the application
dotnet publish GenHub/GenHub.Windows/GenHub.Windows.csproj `
  -c Release `
  --self-contained `
  -r win-x64 `
  -o ./publish/windows `
  -p:Version=$VERSION

# Package with Velopack
vpk pack `
  --packId GenHub `
  --packVersion $VERSION `
  --packDir ./publish/windows `
  --mainExe GenHub.Windows.exe `
  --packTitle "GenHub" `
  --packAuthors "Community Outpost" `
  --icon GenHub/GenHub/Assets/Icons/generalshub.ico `
  --outputDir ./releases
```

#### Linux

```bash
# Set version
VERSION="0.0.157"  # Or "0.0.160-pr3" for PR builds

# Publish the application
dotnet publish GenHub/GenHub.Linux/GenHub.Linux.csproj \
  -c Release \
  --self-contained \
  -r linux-x64 \
  -o ./publish/linux \
  -p:Version=$VERSION

# Package with Velopack
vpk pack \
  --packId GenHub \
  --packVersion $VERSION \
  --packDir ./publish/linux \
  --mainExe GenHub.Linux \
  --packTitle "GenHub" \
  --packAuthors "Community Outpost" \
  --icon GenHub/GenHub/Assets/Icons/generalshub.ico \
  --outputDir ./releases
```

### Automated CI/CD

GenHub includes GitHub Actions workflows:

#### `.github/workflows/ci.yml` - Continuous Integration

Runs on every push and PR:

1. Builds for Windows and Linux
2. Packages with Velopack using `0.0.{RUN_NUMBER}[-pr{PR_NUMBER}]` versioning
3. Uploads artifacts to GitHub Actions
4. Generates `releases.win.json` and `releases.linux.json` metadata

**Artifacts uploaded:**

- `genhub-velopack-windows-{VERSION}` - Windows installer and packages
- `genhub-velopack-linux-{VERSION}` - Linux packages
- `genhub-metadata-windows-{VERSION}` - Update metadata (`releases.win.json`)
- `genhub-metadata-linux-{VERSION}` - Update metadata (`releases.linux.json`)

#### `.github/workflows/release.yml` - Stable Releases

Triggered by version tags (`v*`):

1. Builds releases for Windows and Linux
2. Creates GitHub Release
3. Uploads installers and packages
4. Publishes update feed for automatic updates

**To trigger a stable release:**

```bash
git tag v0.0.157
git push origin v0.0.157
```

## Release Artifacts

After packaging, Velopack generates:

### Windows

- **GenHub-{Version}-Setup.exe** - Full installer (installs to `%LOCALAPPDATA%\GenHub`)
- **GenHub-{Version}-full.nupkg** - Full release package
- **GenHub-{Version}-delta.nupkg** - Delta update package (if previous version exists)
- **GenHub-{Version}-Portable.zip** - Portable version (no installation required)
- **releases.win.json** - Update feed manifest (JSON format)

### Linux

- **GenHub-{Version}-linux-x64.AppImage** - AppImage installer
- **GenHub-{Version}-full.nupkg** - Full release package
- **GenHub-{Version}-delta.nupkg** - Delta update package
- **releases.linux.json** - Update feed manifest (JSON format)

**Note**: Velopack v0.0.942+ uses JSON format (`releases.*.json`) instead of the legacy `RELEASES` file.

## Installation Locations

### Windows Installation

- **Installation Directory**: `%LOCALAPPDATA%\GenHub\` (e.g., `C:\Users\YourName\AppData\Local\GenHub\`)
- **User Data**: `%APPDATA%\GenHub\`
- **Update Cache**: `%LOCALAPPDATA%\GenHub\packages\`

**Note**: Velopack uses a "one-click" installer that installs to LocalAppData by default. This location:

- Does not require administrator privileges
- Is standard for modern auto-updating applications (VS Code, Discord, Slack, etc.)
- Enables seamless automatic updates
- Is isolated per-user for better security

### Custom Installation Path (--installto)

Users who wish to install GenHub to a custom drive or directory (e.g. `D:\Games\GenHub`) can use the `--installto` (or short form `-t`) parameter during setup:

```cmd
GenHub-win-Setup.exe --installto "D:\Games\GenHub"
```

Any application arguments must follow a `--` separator if needed. When `--installto` is specified, Velopack installs all binaries, `Update.exe`, and package metadata into the designated target folder instead of `%LOCALAPPDATA%\GenHub\`. Automatic updates continue to work normally within the custom installation directory.

Existing installations on `C:` can also be migrated to a new location at any time using the in-app **Settings → Migrate Installation** workflow.

### Linux Installation

- **Installation Directory**: `~/.local/share/GenHub/`
- **User Data**: `~/.config/GenHub/`
- **Update Cache**: `~/.cache/GenHub/`

The app ID `GenHub` ensures clean, predictable installation paths without vendor prefixes. Note that CI release pipelines currently produce `.nupkg` packages and JSON release-feed manifests for Linux updates; no standalone installer or AppImage picker is distributed at this time.

### Protocol Handler & URI Scheme Registration

On Windows, the `genhub://` custom URI scheme is registered under `HKCU\Software\Classes\genhub` on application startup. If the installation directory is moved or migrated to a different folder or drive, the registration automatically self-heals upon next launch from the new location by updating the command path in the registry. Linux and macOS do not use registry-based protocol handlers, so no re-registration is needed on those platforms.

## Update Features

### Dismiss Updates

Users can dismiss update notifications:

```csharp
// In UpdateNotificationViewModel
private void DismissUpdate()
{
    // Persist dismissed version to prevent showing again
    _userSettingsService.Update(s => s.DismissedUpdateVersion = LatestVersion);
    IsUpdateAvailable = false;
}
```

Dismissed updates won't reappear until a **different version** is available.

### PR Update Priority

When subscribed to a PR:

1. PR updates take priority over stable releases
2. Version comparison ignores build metadata
3. Users can "switch" to any PR build regardless of version number
4. Unsubscribing returns to stable channel

### Build Metadata Handling

Build metadata (commit hashes) is:

- **Included** in the version string for tracking
- **Displayed** in the UI for transparency
- **Stripped** during version comparison to avoid false updates
- **Logged** for debugging purposes

Example:

```
Display: v0.0.160-pr3 (051baf8)
Full version: 0.0.160-pr3+051baf8.051baf832a894f542e60d3e4c2471c7d4dd753da
Comparison: 0.0.160-pr3
```

## Testing Updates Locally

### 1. Create Test Release

```powershell
vpk pack --packId GenHub --packVersion 0.0.100 --packDir ./publish --mainExe GenHub.Windows.exe -o ./test-releases
```

### 2. Create Second Version

Update your code, then:

```powershell
vpk pack --packId GenHub --packVersion 0.0.101 --packDir ./publish --mainExe GenHub.Windows.exe -o ./test-releases --delta ./test-releases/releases.win.json
```

### 3. Test Update

Point UpdateManager to local directory:

```csharp
var source = new SimpleWebSource("file:///C:/path/to/test-releases");
var manager = new UpdateManager(source);
```

## Architecture

### Components

- **VelopackApp** - Handles application lifecycle hooks
- **VelopackUpdateManager** - Service for checking/downloading/applying updates
- **IVelopackUpdateManager** - Interface for update operations
- **UpdateInfo** - Model containing update metadata from Velopack
- **ArtifactUpdateInfo** - Model for PR artifact metadata
- **PullRequestInfo** - Model for PR information and artifacts
- **UpdateProgress** - Progress reporting model

### Integration Points

- `Program.cs` - VelopackApp.Build().Run() initialization
- `AppUpdateModule.cs` - DI registration
- `VelopackUpdateManager.cs` - Update service implementation
- `UpdateNotificationViewModel.cs` - UI logic for update notifications
- `MainViewModel.cs` - Main window update badge logic

### Key Services

```csharp
public interface IVelopackUpdateManager
{
    // Stable channel updates
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task DownloadUpdatesAsync(UpdateInfo updateInfo, IProgress<UpdateProgress>? progress = null);
    void ApplyUpdatesAndRestart(UpdateInfo updateInfo);

    // PR artifact updates
    Task<PullRequestInfo?> GetSubscribedPrInfoAsync(CancellationToken cancellationToken = default);
    Task InstallPrArtifactAsync(PullRequestInfo prInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default);

    // Properties
    bool HasUpdateAvailableFromGitHub { get; }
    string? LatestVersionFromGitHub { get; }
}
```

## Troubleshooting

### Updates Not Working in Development

Velopack only works when the app is installed. When running from Visual Studio or `dotnet run`, updates are disabled. This is expected behavior.

To test updates:

1. Build a Velopack package
2. Install it using the Setup.exe
3. Run the installed app

### Update Check Fails

- **GitHub Releases**: Ensure repository is public or provide authentication
- **PR Artifacts**: Requires GitHub PAT with `repo` scope
- Verify GitHub Releases/Actions contain Velopack packages
- Check network connectivity
- Review logs for specific error messages

### Version Comparison Issues

GenHub strips build metadata before comparison. If updates aren't detected:

1. Check version format: `0.0.X` or `0.0.X-prY`
2. Verify build metadata is after `+` symbol
3. Review logs for version comparison details

### PR Artifact Installation Fails

Common issues:

1. **"No update found from PR artifact"**
   - Current version >= target version
   - Velopack won't "downgrade" by default
   - Solution: Uninstall and run Setup.exe from PR artifact

2. **"Already installed"**
   - Same base version with different build metadata
   - This is expected - versions are identical

3. **Authentication errors**
   - GitHub PAT missing or invalid
   - PAT needs `repo` scope for private repos

### App Doesn't Restart After Update

- Ensure using `ApplyUpdatesAndRestart()` not `ApplyUpdatesAndExit()`
- Check logs for update application errors
- Verify update package integrity

### Custom Installation Directory Updates

When GenHub is installed into a custom directory via `--installto <path>` or relocated using the in-app migration tool:
- Automatic updates continue to operate seamlessly within the custom directory. Velopack automatically resolves the update root relative to the running executable's path (`AppContext.BaseDirectory`).
- New version binaries and packages will be staged and applied directly inside the custom folder without reverting to `%LOCALAPPDATA%\GenHub`.
- Desktop shortcuts and protocol handler registrations automatically point to the new location.

## Security Considerations

### GitHub Personal Access Tokens

- **Storage**: Tokens are stored in Windows Credential Manager (Windows) or Keyring (Linux)
- **Scope**: Only `repo` scope is required
- **Usage**: Only for downloading PR artifacts
- **Rotation**: Users can update/remove tokens at any time

### Update Verification

Velopack verifies package integrity using:

- SHA256 checksums in metadata files
- Package signatures (if configured)
- HTTPS for all downloads

## Additional Resources

- [Velopack Documentation](https://docs.velopack.io/)
- [Velopack GitHub](https://github.com/velopack/velopack)
- [GitHub Releases Guide](https://docs.github.com/en/repositories/releasing-projects-on-github)
- [GitHub Actions Artifacts](https://docs.github.com/en/actions/using-workflows/storing-workflow-data-as-artifacts)
- [Semantic Versioning](https://semver.org/)
