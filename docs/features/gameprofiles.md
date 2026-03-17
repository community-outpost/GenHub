---
title: Game Profiles
description: Configuration management and options persistence
---

**Game Profiles** are the user-facing units of configuration in GeneralsHub. A profile encapsulates everything needed to launch a specific game state: which mods are enabled, which game engine to use, and what settings (resolution, detail level) to apply.

## Data Model

Profiles are serialized as JSON documents.

```json
{
  "id": "profile_12345",
  "name": "RotR Competitive",
  "gameInstallationId": "steam_zerohour",
  "gameClient": {
    "gameType": "ZeroHour",
    "executablePath": "generals.exe"
  },
  "enabledContentIds": [
    "1.87.swr.mod.rotr",
    "1.0.community.patch.genpatcher"
  ],
  "videoWidth": 1920,
  "videoHeight": 1080,
  "videoWindowed": true,
  "videoSkipEALogo": true,
  "environmentVariables": {
    "gentool_monitor": "1"
  }
}
```

## Persistence Layer

The `GameProfileRepository` handles storage.

- **Format**: Plain JSON files in the user's data directory.
- **Naming**: `{ProfileId}.json`.
- **Resilience**:
  - Atomic writes (via `File.WriteAllTextAsync`).
  - **Corruption Handling**: If a profile fails to deserialize, it is automatically renamed to `.corrupted` to prevent the app from crashing, and a "Corrupted Profile" warning is logged.

## Options.ini Generation

SAGE engine games rely on a global `Options.ini` file in `Documents\Command and Conquer ...`. This creates a conflict when switching between mods (e.g., Mod A needs 800x600, Mod B needs 1080p).

GeneralsHub solves this with **Dynamic Options Injection** at launch time.

### The Injection Process

Built into `GameLauncher.cs`, this process runs immediately before `generals.exe` starts:

1. **Load Existing**: Reads the current `Options.ini` from disk.
   - *Why?* To preserve settings managed by third-party tools (like GenTool or TheSuperHackers' fixes) that GeneralsHub doesn't explicitly track.
2. **Apply Overrides**: Maps `GameProfile` properties to the INI model.
   - `Profile.VideoWidth` -> `Resolution`
   - `Profile.VideoReview` -> `StaticGameLOD`
3. **Windowed Mode**: If `VideoWindowed` is true, ensures `-win` is added to command arguments (required for the engine to actually respect the windowed flag).
4. **Save**: Writes the merged `Options.ini` back to disk.

### Generals Online Support

For the specialized **Generals Online** client, the system also injects settings into `settings.json`, ensuring that unique features of that community client (like 30FPS vs 60FPS toggles) are respected per-profile.

## Launch Options

Profiles support flexible launch configuration:

- **Command Line Arguments**: Sanitized strings passed to the process (e.g., `-quickstart -nologo`).
- **Environment Variables**: Injected into the game process scope (useful for tools like GenTool that read env vars).
