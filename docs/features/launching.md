---
title: Launching System
description: Process management and Steam integration architecture
---

The **Launching System** is responsible for bootstrapping the game process within the isolated [Workspace](./workspace.md). It handles the complexity of environment setup, argument injection, and platform integration (Steam/EA App).

## Architecture

The system distinguishes between **Standard Launches** (direct process creation) and **Platform Launches** (Steam/EA).

```mermaid
graph TD
    User[User] -->|Click Play| Launcher[GameLauncher]
    Launcher -->|1. Prep| Workspace[WorkspaceManager]
    Launcher -->|2. Check| Steam{Is Steam?}
    Steam -- No -->|Direct| Process[Process.Start]
    Steam -- Yes -->|Proxy| SteamLaunch[SteamLauncher]
    SteamLaunch -->|Swap| ProxyExe[Proxy Launcher]
    SteamLaunch -->|Trigger| SteamAPI[Steam Client]
    SteamAPI -->|Runs| ProxyExe
    ProxyExe -->|Chains| GameExe[Workspace Game Exe]
```

## The "Proxy Dance" (Steam Integration)

To launch a modded game through Steam (tracking hours, overlay, status) while keeping the files isolated in a Workspace, GenHub employs a "Proxy Dance" technique.

### The Problem

Steam will only launch the executable defined in its manifest (e.g., `Command and Conquer Generals Zero Hour\generals.exe`). It does not allow launching an arbitrary `.exe` in a separate `AppData` folder.

### The Solution

1. **Backup**: Rename the real `generals.exe` to `generals.exe.ghbak`.
2. **Deploy Proxy**: Copy `GenHub.ProxyLauncher.exe` to `generals.exe`.
3. **Configure**: Write a `proxy_config.json` file next to it:

   ```json
   {
     "TargetExecutable": "C:\\Users\\User\\.genhub\\workspaces\\profile_123\\generals.exe",
     "WorkingDirectory": "C:\\Users\\User\\.genhub\\workspaces\\profile_123\\"
   }
   ```

4. **Inject Dependencies**: Copy `steam_api.dll` and `steam_appid.txt` to the Workspace so the game can initialize the Steam API.
5. **Launch**: GenHub tells Steam to "Play Game".
6. **Execution Chain**:
   * Steam runs `generals.exe` (Our Proxy).
   * Proxy reads config.
   * Proxy launches the *actual* game in the Workspace.
7. **Cleanup**: When the game closes, GenHub restores the original `generals.exe`.

## Process Monitoring

The **GameProcessManager** tracks the lifecycle of the game.

### Security & Isolation

* **Path Validation**: The launcher strictly validates that the executable being launched resides *within* the authorized Workspace boundary. This prevents "Workspace Escape" attacks.
* **Argument Sanitization**: Command-line arguments are sanitized to block injection attacks (e.g., preventing `; rm -rf /` style chains).

### Lifecycle

1. **Pre-Launch**: `LaunchRegistry` reserves a "Launch Slot" to prevent double-launching the same profile.
2. **Monitoring**: The PID is tracked.
3. **Termination**:
   * **Graceful**: Sends `CloseMainWindow` signal.
   * **Force**: If process hangs >5s, calls `Process.Kill()`.
