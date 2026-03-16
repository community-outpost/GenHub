# Steam Proxy Launcher

## Overview

The Steam Proxy Launcher is a mechanism that enables GenHub to provide full Steam integration (overlay, playtime tracking) for modded game profiles while maintaining workspace isolation.

## How It Works

### Reserved Executable Files

`generals.exe` is **reserved exclusively for proxy launcher use**.

- These files are **never detected as game clients** during installation scans
- They serve as the "trampoline" that Steam launches, which then launches your actual game client
- Game detection uses `game.dat` and other files to identify installations
- This prevents conflicts and duplicates when switching between Steam and non-Steam profiles

### The Problem

Steam expects to launch a specific executable (e.g., `generals.exe`) from the game's installation directory. However, GenHub uses isolated workspaces to manage different mod configurations. We need Steam to launch our workspace-isolated game while still thinking it's launching the original game.

### The Solution

The Proxy Launcher acts as a "middleman" that Steam launches instead of the real game:

1. **Deployment**: When preparing a Steam launch, GenHub:
   - Backs up the original game executable (e.g., `generals.exe` → `generals.exe.ghbak`)
   - Replaces it with the Proxy Launcher binary
   - Creates a configuration file (`proxy_config.json`) telling the proxy which workspace executable to launch

2. **Launch**: When Steam launches the game:
   - Steam runs what it thinks is `generals.exe` (actually our proxy)
   - The proxy reads `proxy_config.json`
   - The proxy launches the actual workspace executable
   - **Crucially**, if the game launcher exits immediately (spawning another process), the proxy **detects this child process** and stays alive until the child exits. This ensures Steam continues to track playtime and the overlay remains active.

3. **Cleanup**: When switching profiles or closing:
   - GenHub restores the original executable from `.ghbak`
   - Removes the proxy configuration file
   - The game directory returns to its original state

## File Swapping Mechanism

### Deployment

```text
Before:
  generals.exe          (original game)

After:
  generals.exe          (proxy launcher)
  generals.exe.ghbak    (original game backup)
  proxy_config.json     (proxy configuration)
```

### Restoration

```text
Before:
  generals.exe          (proxy launcher)
  generals.exe.ghbak    (original game backup)
  proxy_config.json     (proxy configuration)

After:
  generals.exe          (original game - restored)
```

## Configuration File

The `proxy_config.json` file tells the proxy what to launch:

```json
{
  "TargetExecutable": "Z:\\GenHubMain\\.genhub-workspace\\profile-id\\generalszh.exe",
  "WorkingDirectory": "Z:\\GenHubMain\\.genhub-workspace\\profile-id",
  "Arguments": ["-quickstart"],
  "SteamAppId": "9880"
}
```

## Profile Switching

When switching between profiles:

1. **From Steam Profile to Non-Steam Profile**:
   - Cleanup is called automatically before workspace preparation
   - Original executable is restored from `.ghbak`
   - Proxy config is removed
   - Normal workspace launch proceeds

2. **From Steam Profile to Another Steam Profile**:
   - Cleanup restores original executable
   - New deployment replaces it with proxy again
   - New proxy config points to new workspace

3. **From Non-Steam Profile to Steam Profile**:
   - No cleanup needed (no proxy was deployed)
   - Deployment proceeds normally

## Game Detection

The `GameClientDetector` is aware of the proxy mechanism:

- **Backup Detection**: When detecting game versions, it checks for `.ghbak` files first
- **Proxy Exclusion**: `GenHub.ProxyLauncher.exe` is explicitly excluded from game client scans
- **Version Detection**: Uses the backup file for version detection when present, ensuring accurate version identification even when proxy is deployed

## Advanced Features

### Process Keep-Alive for Steam Tracking

Some mod launchers (like Community Patch) start the game and then immediately exit. This would normally cause Steam to stop tracking usage. The Proxy Launcher handles this by:

1. Detecting if the launched process exits quickly (< 30 seconds).
2. Scanning for a "spawned" child process (e.g., the actual game window) that started around the same time.
3. **Waiting for that child process** to exit before the proxy itself exits.

### Steam Environment Injection

Even if the game is launched directly (not via Steam UI), the proxy attempts to ensure Steam integration works by:

- Injecting Steam environment variables (`SteamAppId`, `SteamClientLaunch`, etc.)
- Ensuring `steam_appid.txt` exists in the working directory
This allows "Play" in GenHub to potentially trigger Steam integration features even without a direct `steam://` URL launch (though `steam://` is preferred).

## Troubleshooting

### Proxy Not Updating

**Symptom**: Old version of proxy continues to run even after code changes.

**Cause**: The proxy executable was locked by a running process.

**Solution**: The deployment logic now automatically:

1. Detects running processes with the same name
2. Kills matching processes
3. Waits for file lock to release
4. Deploys the new proxy

### Game Won't Launch

**Symptom**: Steam launches but nothing happens.

**Cause**: Proxy config might be missing or invalid.

**Solution**: Check `debug.log` for proxy deployment messages. Ensure:

- `proxy_config.json` exists in game directory
- Target executable path in config is valid
- Workspace was prepared successfully

### Original Game Missing

**Symptom**: After cleanup, the game executable is missing.

**Cause**: Backup file was not created or was deleted.

**Solution**:

- Check for `.ghbak` file in game directory
- If missing, verify game files through Steam
- GenHub will recreate backup on next Steam launch

### Infinite Loop

**Symptom**: Game launches repeatedly or crashes immediately.

**Cause**: Workspace contains the proxy instead of the real game.

**Solution**: This is prevented by:

- Forcing workspace recreation for Steam launches (`ForceRecreate = true`)
- Pre-launch cleanup to ensure original executables are present before workspace preparation

## Implementation Details

### Key Files

- **`SteamLauncher.cs`**: Handles proxy deployment and cleanup
- **`GameClientDetector.cs`**: Excludes proxy from detection, uses backups for version detection
- **`GameLauncher.cs`**: Calls cleanup before workspace preparation for Steam launches
- **`GenHub.ProxyLauncher/Program.cs`**: The proxy executable itself

### Deployment Logic

```csharp
// 1. Backup original if not already backed up
if (!File.Exists(backupPath) && File.Exists(targetExePath))
{
    File.Copy(targetExePath, backupPath, overwrite: false);
}

// 2. Kill any running instances to release file lock
var processes = Process.GetProcessesByName(processName);
foreach (var process in processes)
{
    if (process.MainModule?.FileName == targetExePath)
    {
        process.Kill();
        process.WaitForExit(1000);
    }
}

// 3. Deploy proxy (always overwrite)
File.Copy(proxySourcePath, targetExePath, overwrite: true);
```

### Cleanup Logic

```csharp
// 1. Remove proxy config
if (File.Exists(proxyConfigPath))
{
    File.Delete(proxyConfigPath);
}

// 2. Restore original executable
if (File.Exists(backupPath))
{
    if (File.Exists(targetExePath))
    {
        File.Delete(targetExePath); // Remove proxy
    }
    File.Move(backupPath, targetExePath); // Restore original
}
```

## Best Practices

1. **Always Cleanup Before Workspace Prep**: Ensures workspace doesn't copy the proxy as the game
2. **Force Workspace Recreation**: Prevents cached workspaces with proxies from being reused
3. **Check for Backups**: Use `.ghbak` files for version detection when present
4. **Handle File Locks**: Kill processes before deployment to ensure updates succeed
5. **Log Everything**: Comprehensive logging helps diagnose deployment and cleanup issues

## Future Improvements

- **Signature Verification**: Verify proxy binary signature before deployment
- **Rollback Mechanism**: If deployment fails, automatically restore from backup
- **Health Checks**: Verify proxy config validity before launch
- **Multi-Game Support**: Extend mechanism to support other Steam games beyond C&C Generals
