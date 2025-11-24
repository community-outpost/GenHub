# Release Process

This document describes the complete process for creating and publishing GenHub releases using Velopack for automatic updates.

## Table of Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Version Numbering](#version-numbering)
- [Creating a Release](#creating-a-release)
- [Testing Updates](#testing-updates)
- [Troubleshooting](#troubleshooting)

## Overview

GenHub uses [Velopack](https://github.com/velopack/velopack) for automatic application updates. The update system:

- Checks for updates automatically on startup
- Downloads delta updates (only changed files) for efficiency
- Installs updates and restarts the application
- Uses GitHub Releases as the distribution channel

### Automated vs Manual Releases

**Automated (Recommended):** Push a version tag and let CI/CD handle everything:
```powershell
git tag -a v1.0.0 -m "Release v1.0.0"
git push origin v1.0.0
```

The CI/CD workflow (`.github/workflows/release.yml`) will automatically:
- Build Windows and Linux releases
- Create Velopack packages with all required files
- Verify critical files are present (including `releases.win.json`)
- Publish to GitHub Releases
- Generate release notes

**Manual:** Follow the complete steps in this guide for manual release creation (useful for testing or when CI/CD is unavailable).

## Prerequisites

Before creating a release, ensure you have:

1. **Velopack CLI installed**
   ```powershell
   dotnet tool install -g vpk
   ```

2. **GitHub CLI installed and authenticated**
   ```powershell
   # Install GitHub CLI
   winget install GitHub.cli
   
   # Authenticate
   gh auth login
   ```

3. **Write access to the repository**
   - Must be a member of the Community-Outpost organization
   - Must have push access to the `community-outpost/genhub` repository

4. **Clean working directory**
   ```powershell
   git status  # Should show no uncommitted changes
   ```

## Version Numbering

GenHub uses [Semantic Versioning](https://semver.org/): `MAJOR.MINOR.PATCH[-PRERELEASE]`

- **MAJOR**: Breaking changes or major feature releases (e.g., `2.0.0`)
- **MINOR**: New features, backwards compatible (e.g., `1.1.0`)
- **PATCH**: Bug fixes, backwards compatible (e.g., `1.0.1`)
- **PRERELEASE**: Alpha, beta, or release candidate (e.g., `1.0.0-alpha.1`, `1.0.0-beta.2`, `1.0.0-rc.1`)

### Prerelease Tags

- **alpha**: Early development, may have bugs and incomplete features
- **beta**: Feature complete, but may have bugs
- **rc** (release candidate): Stable, ready for production unless critical bugs are found

## Creating a Release

### Option A: Automated Release (Recommended)

The easiest way to create a release is to push a version tag. The CI/CD workflow will handle everything automatically.

#### Steps:

1. **Update Version in Directory.Build.props (Single Source of Truth):**
   ```xml
   <Version>1.0.0</Version>
   ```
   
   This version is automatically used everywhere:
   - Application code at runtime
   - Assembly metadata
   - Velopack packages
   - GitHub release tags

2. **Commit and Push:**
   ```powershell
   git add GenHub/Directory.Build.props
   git commit -m "chore: bump version to 1.0.0"
   git push origin main  # or your branch name
   ```

3. **Create and Push Tag:**
   ```powershell
   git tag -a v1.0.0 -m "Release v1.0.0"
   git push origin v1.0.0
   ```

4. **Monitor Workflow:**
   - Go to the Actions tab in GitHub
   - Watch the "Build and Release" workflow
   - The workflow extracts version from Directory.Build.props automatically
   - The release will be created with tag `v{version}` when complete

5. **Verify Release:**
   ```powershell
   gh release view v1.0.0 --repo community-outpost/genhub
   ```

**For Prereleases (alpha/beta/rc):**
- Tag with prerelease suffix: `v1.0.0-alpha.1`, `v1.0.0-beta.2`, `v1.0.0-rc.1`
- The workflow will automatically detect and mark as prerelease

### Option B: Manual Release

If you need to create a release manually (for testing or troubleshooting), follow these steps:

### Step 1: Update Version Number

Edit `GenHub/Directory.Build.props` and update the `<Version>` property (this is the **single source of truth** for version):

```xml
<Version>1.0.0</Version>
```

**Important:** This version will be automatically used by:
- Application code (AppConstants.AppVersion)
- .NET assembly metadata
- Velopack package creation
- CI/CD workflow

No need to update version anywhere else!

### Step 2: Commit Version Change

```powershell
git add GenHub/Directory.Build.props
git commit -m "chore: bump version to 1.0.0"
git push origin feat/installer  # Or your branch name
```

### Step 3: Build and Publish

```powershell
# Build the project
dotnet build GenHub/GenHub.sln -c Release

# Publish the Windows application
dotnet publish GenHub/GenHub.Windows/GenHub.Windows.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o publish/win-x64
```

### Step 4: Create Velopack Package

```powershell
# Navigate to publish directory
cd publish

# Create Velopack package (replace 1.0.0 with your version)
vpk pack `
    --packId GenHub `
    --packVersion 1.0.0 `
    --packDir win-x64 `
    --mainExe GenHub.Windows.exe `
    --packTitle "GenHub"

# Return to root directory
cd ..
```

This generates several files in `publish/Releases/`:
- `GenHub-1.0.0-full.nupkg` - Full installer package
- `GenHub-1.0.0-delta.nupkg` - Delta update (only if upgrading from previous version)
- `GenHub-win-Setup.exe` - End-user installer
- `RELEASES` - Velopack manifest file
- `releases.win.json` - Update metadata (critical for updates!)
- `assets.win.json` - Asset metadata

### Step 5: Create Git Tag

```powershell
# Create and push tag (replace 1.0.0 with your version)
git tag -a v1.0.0 -m "Release v1.0.0"
git push origin v1.0.0
```

### Step 6: Create GitHub Release

**For Stable Releases:**

```powershell
gh release create v1.0.0 `
    --repo community-outpost/genhub `
    --title "GenHub v1.0.0" `
    --notes "Release notes here..." `
    publish/Releases/GenHub-1.0.0-full.nupkg `
    publish/Releases/GenHub-1.0.0-delta.nupkg `
    publish/Releases/GenHub-win-Setup.exe `
    publish/Releases/RELEASES `
    publish/Releases/releases.win.json `
    publish/Releases/assets.win.json
```

**For Prerelease (alpha/beta/rc):**

```powershell
gh release create v1.0.0-alpha.1 `
    --repo community-outpost/genhub `
    --title "GenHub v1.0.0-alpha.1" `
    --notes "### New Features`n- Feature 1`n- Feature 2" `
    --prerelease `
    publish/Releases/GenHub-1.0.0-alpha.1-full.nupkg `
    publish/Releases/GenHub-1.0.0-alpha.1-delta.nupkg `
    publish/Releases/GenHub-win-Setup.exe `
    publish/Releases/RELEASES `
    publish/Releases/releases.win.json `
    publish/Releases/assets.win.json
```

### Step 7: Verify Release

1. **Check GitHub release page:**
   ```powershell
   gh release view v1.0.0 --repo community-outpost/genhub
   ```

2. **Verify all required assets are present:**
   - ✅ `GenHub-X.X.X-full.nupkg`
   - ✅ `GenHub-X.X.X-delta.nupkg` (if applicable)
   - ✅ `GenHub-win-Setup.exe`
   - ✅ `RELEASES`
   - ✅ `releases.win.json` (critical!)
   - ✅ `assets.win.json`

## Testing Updates

### First-Time Installation Testing

1. **Download the installer:**
   ```powershell
   gh release download v1.0.0 --repo community-outpost/genhub --pattern "GenHub-win-Setup.exe"
   ```

2. **Run the installer:**
   - Double-click `GenHub-win-Setup.exe`
   - Application installs to `%LOCALAPPDATA%\GenHub`
   - Shortcut created in Start Menu

3. **Launch and verify:**
   - Open GenHub from Start Menu
   - Check version in Settings → About

### Update Testing

1. **Install previous version:**
   - Download and install an older release
   - Verify it launches successfully

2. **Trigger update check:**
   - Open GenHub
   - Go to Settings → Updates
   - Click "Check for Updates"

3. **Verify update detection:**
   - Should show new version (e.g., "v1.0.0")
   - Should show "Install Update" button

4. **Install update:**
   - Click "Install Update"
   - Progress bar should show download
   - Application should restart automatically
   - Verify new version in Settings → About

## Troubleshooting

### "Could not find asset called 'releases.win.json'"

**Problem:** Velopack cannot find the update metadata file.

**Solution:** Ensure `releases.win.json` is uploaded to the GitHub release:

```powershell
gh release upload v1.0.0 `
    publish/Releases/releases.win.json `
    --repo community-outpost/genhub `
    --clobber
```

### "No updates available" when update exists

**Problem:** Version mismatch or missing delta package.

**Solution:**
1. Check the version in `releases.win.json`:
   ```powershell
   Get-Content publish/Releases/releases.win.json | ConvertFrom-Json
   ```

2. Verify the version matches the package filenames

3. If version is wrong, rebuild with correct version:
   ```powershell
   cd publish
   vpk pack --packId GenHub --packVersion 1.0.1 --packDir win-x64 --mainExe GenHub.Windows.exe --packTitle "GenHub"
   ```

### "This operation can not be performed in an application that is not installed"

**Problem:** Running GenHub from build directory instead of installed version.

**Solution:**
- Always test updates with the installed version from `%LOCALAPPDATA%\GenHub`
- Install using `GenHub-win-Setup.exe` first
- Do not test updates by running from `bin/Release/` directory

### Update downloads but fails to install

**Problem:** Corrupted download or permission issues.

**Solution:**
1. Check Velopack logs:
   ```powershell
   Get-Content "$env:LOCALAPPDATA\GenHub\velopack.log" -Tail 50
   ```

2. Verify file integrity on GitHub release

3. Try clean installation:
   ```powershell
   # Uninstall current version
   & "$env:LOCALAPPDATA\GenHub\Update.exe" --uninstall
   
   # Reinstall from Setup.exe
   .\GenHub-win-Setup.exe
   ```

## Quick Reference

### Complete Release Command Sequence

```powershell
# 1. Update version in Directory.Build.props
# 2. Commit and push changes
git add GenHub/Directory.Build.props
git commit -m "chore: bump version to 1.0.0"
git push

# 3. Build and publish
dotnet build GenHub/GenHub.sln -c Release
dotnet publish GenHub/GenHub.Windows/GenHub.Windows.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64

# 4. Create Velopack package
cd publish
vpk pack --packId GenHub --packVersion 1.0.0 --packDir win-x64 --mainExe GenHub.Windows.exe --packTitle "GenHub"
cd ..

# 5. Create and push tag
git tag -a v1.0.0 -m "Release v1.0.0"
git push origin v1.0.0

# 6. Create GitHub release
gh release create v1.0.0 `
    --repo community-outpost/genhub `
    --title "GenHub v1.0.0" `
    --notes "Release notes..." `
    publish/Releases/GenHub-1.0.0-full.nupkg `
    publish/Releases/GenHub-1.0.0-delta.nupkg `
    publish/Releases/GenHub-win-Setup.exe `
    publish/Releases/RELEASES `
    publish/Releases/releases.win.json `
    publish/Releases/assets.win.json

# 7. Verify
gh release view v1.0.0 --repo community-outpost/genhub
```

## Additional Resources

- [Velopack Documentation](https://docs.velopack.io/)
- [Semantic Versioning](https://semver.org/)
- [GitHub CLI Documentation](https://cli.github.com/manual/)
- [GenHub Architecture](./architecture.md)

---

## CI/CD Workflow Details

The automated release workflow (`.github/workflows/release.yml`) provides the following features:

### Triggers

- **Automatic:** Triggered on version tag push (`v*`)
- **Manual:** Can be triggered via GitHub Actions UI with custom version input

### Build Process

1. **Windows Build Job:**
   - Restores dependencies and builds solution
   - Publishes self-contained Windows executable
   - Creates Velopack package with icon and metadata
   - Verifies all required files are present:
     - `GenHub-X.X.X-full.nupkg`
     - `GenHub-X.X.X-delta.nupkg` (if delta available)
     - `GenHub-win-Setup.exe`
     - `RELEASES`
     - `releases.win.json` (critical!)
     - `assets.win.json`
   - Uploads artifacts for release

2. **Linux Build Job:**
   - Same process as Windows but for Linux platform
   - Creates Linux-specific Velopack packages
   - Verifies Linux release files

3. **Create Release Job:**
   - Downloads all build artifacts
   - Detects if version is prerelease (alpha/beta/rc)
   - Generates release notes
   - Creates GitHub Release with all assets
   - Verifies release was created successfully

### Prerelease Detection

The workflow automatically detects prereleases by checking the version string:
- If version contains `alpha`, `beta`, or `rc`, it's marked as prerelease
- Can be manually overridden with `prerelease: true` in workflow dispatch

### Artifact Retention

Build artifacts are retained for 90 days, allowing developers to download and test builds without installing from GitHub Releases.

### Monitoring

- Check the Actions tab for workflow status
- View detailed logs for each build step
- Summary shows which components succeeded/failed

### Troubleshooting CI/CD

**Build fails at "Verify Critical Files" step:**
- Velopack may have failed to generate all files
- Check the "Create Velopack Package" step logs
- Ensure icon file exists at `GenHub/GenHub/Assets/Icons/generalshub.ico`

**Release creation fails:**
- Check GitHub token permissions (requires `contents: write`)
- Verify tag format matches `v*` pattern
- Ensure all build jobs completed successfully

**Delta package not generated:**
- This is normal for first releases (no previous version to compare)
- Delta packages are only created when updating from a previous version
- The workflow handles this gracefully
