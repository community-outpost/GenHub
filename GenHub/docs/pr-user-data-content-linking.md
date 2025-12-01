# Pull Request: User Data Content Linking System

## PR Title
**feat: Add dynamic user data content linking for maps, missions, and replays**

---

## Description

This PR introduces a comprehensive user data content linking system that dynamically manages content like maps, missions, and replays by linking them to the user's Documents folder (user data directory) instead of embedding them in the workspace.

### Problem Statement

Previously, all content including maps, missions, and replays were copied directly into the workspace directory. This approach had several issues:

1. **Disk Space Waste**: Maps and missions were duplicated across multiple profiles and workspaces
2. **No Profile Isolation**: All profiles shared the same maps, making it impossible to have profile-specific map configurations
3. **No Cleanup**: When content was uninstalled, files remained in the Documents folder
4. **No Conflict Detection**: Multiple mods could overwrite each other's maps without warning

### Solution

The new User Data Content Linking system:

1. **Uses Hard Links**: Content is stored once in the Content-Addressable Storage (CAS) and hard-linked to the user's Documents folder, saving disk space
2. **Profile-Scoped Installations**: Each profile can have its own set of maps and missions
3. **Dynamic Activation/Deactivation**: When switching profiles, the previous profile's content is unlinked and the new profile's content is linked
4. **Conflict Detection**: The system tracks which installation owns each file and prevents overwrites
5. **Backup & Restore**: User's existing files are backed up before being replaced and restored on uninstall

---

## Architecture

### New Components

#### `IUserDataTracker` / `UserDataTrackerService`
Low-level service for tracking and managing user data files:
- Installs content files to user data directories using hard links from CAS
- Tracks all installed files with metadata (hash, size, backup path)
- Supports activate/deactivate operations for profile switching
- Maintains an index for quick conflict detection

#### `IProfileContentLinker` / `ProfileContentLinkerService`
High-level orchestrator for profile-based content management:
- Coordinates user data installation when content is added to a profile
- Handles profile switching by deactivating old profile's content and activating new profile's content
- Cleans up all user data when a profile is deleted

### Data Models

#### `UserDataManifest`
Tracks all files installed for a specific content manifest + profile combination:
```csharp
public class UserDataManifest
{
    public string ManifestId { get; set; }
    public string ProfileId { get; set; }
    public GameType TargetGame { get; set; }
    public List<UserDataFileEntry> InstalledFiles { get; set; }
    public bool IsActive { get; set; }
    // ...
}
```

#### `UserDataFileEntry`
Represents a single installed file:
```csharp
public class UserDataFileEntry
{
    public string RelativePath { get; set; }
    public string AbsolutePath { get; set; }
    public string SourceHash { get; set; }
    public bool IsHardLink { get; set; }
    public string? BackupPath { get; set; }
    // ...
}
```

#### `UserDataIndex`
Global index for quick lookups:
```csharp
public class UserDataIndex
{
    public Dictionary<string, string> FileToInstallationMap { get; set; }
    public Dictionary<string, List<string>> ProfileInstallations { get; set; }
    public Dictionary<string, List<string>> ManifestInstallations { get; set; }
}
```

---

## Content Install Targets

Content manifests now support new install targets:

| Target | Description | Example Path |
|--------|-------------|--------------|
| `Workspace` | Traditional workspace installation | `{WorkspaceDir}/Maps/...` |
| `UserDataDirectory` | Root of user data folder | `Documents/{GameData}/...` |
| `UserMapsDirectory` | User maps folder | `Documents/{GameData}/Maps/...` |
| `UserReplaysDirectory` | User replays folder | `Documents/{GameData}/Replays/...` |
| `UserScreenshotsDirectory` | User screenshots folder | `Documents/{GameData}/Screenshots/...` |

---

## Launch Flow Integration

The `GameLauncher` now includes a user data preparation phase:

```
1. ValidatingProfile
2. ResolvingContent
3. PreparingWorkspace
4. PreparingUserData  ← NEW: Links maps/missions for the profile
5. Starting
6. Running
7. Completed
```

When launching a profile, the system:
1. Gets the currently active profile's ID
2. Calls `SwitchProfileUserDataAsync` which:
   - Deactivates (unlinks) the old profile's user data
   - Activates (links) the new profile's user data
3. Proceeds with game launch

---

## GeneralsOnline Integration

The GeneralsOnline content pipeline has been updated to support this feature:

### Map Installation
- Maps from GeneralsOnline packages are now installed to `UserMapsDirectory`
- Both the GameClient manifests and the QuickMatch MapPack manifest include maps
- Maps are hard-linked from CAS to `Documents/Command and Conquer Generals Zero Hour Data/Maps/`

### Dependency Handling
- GameClient manifests declare dependencies on `GameInstallation` (Zero Hour)
- When enabling a GameClient, the system auto-selects a compatible GameInstallation
- QuickMatch MapPack is auto-installed as a dependency

---

## Key Changes

### Files Added
- `GenHub.Core/Interfaces/UserData/IUserDataTracker.cs`
- `GenHub.Core/Interfaces/UserData/IProfileContentLinker.cs`
- `GenHub.Core/Models/UserData/UserDataManifest.cs`
- `GenHub.Core/Models/UserData/UserDataFileEntry.cs`
- `GenHub.Core/Models/UserData/UserDataIndex.cs`
- `GenHub/Features/UserData/Services/UserDataTrackerService.cs`
- `GenHub/Features/UserData/Services/ProfileContentLinkerService.cs`
- `GenHub/Infrastructure/DependencyInjection/UserDataModule.cs`

### Files Modified
- `GenHub.Core/Models/GameProfile/LaunchPhase.cs` - Added `PreparingUserData` phase
- `GenHub.Core/Constants/GameSettingsConstants.cs` - Added folder name constants
- `GenHub/Features/Launching/GameLauncher.cs` - Integrated user data switching
- `GenHub/Features/GameProfiles/ViewModels/GameProfileSettingsViewModel.cs` - Auto-dependency selection
- `GenHub/Features/GameProfiles/Services/ProfileContentLoader.cs` - Dependency resolution
- `GenHub/Features/Content/Services/GeneralsOnline/GeneralsOnlineManifestFactory.cs` - Map install targets

---

## Code Review Fixes Included

This PR also addresses the following code review feedback:

### Critical Fixes
1. **Fire-and-forget error handling**: Auto-enable dependencies now shows user warnings on failure
2. **Race condition in profile activation**: Fixed async operation inside lock by moving it outside

### Medium Fixes
3. **Duplicated map processing logic**: Extracted `CreateMapManifestFile()` helper method
4. **Magic strings for folder names**: Added `Maps`, `Replays`, `Screenshots` constants
5. **Date version parsing**: Made more robust with exact YYYY-MM-DD format validation

### Minor Fixes
6. **Potential NullReferenceException**: Added null check for `ManifestId` comparison

---

## Testing

### Unit Tests
All existing tests pass (1065 tests):
- `GenHub.Tests.Core.dll`: 1051 passed
- `GenHub.Tests.Windows.dll`: 5 passed
- `GenHub.Tests.Linux.dll`: 9 passed

### Manual Testing Checklist
- [ ] Create profile with GeneralsOnline GameClient
- [ ] Verify GameInstallation is auto-selected
- [ ] Verify QuickMatch MapPack is auto-installed
- [ ] Launch game and verify maps appear in-game
- [ ] Switch to different profile with different content
- [ ] Verify maps are properly swapped
- [ ] Delete profile and verify cleanup

---

## Breaking Changes

None. This is an additive feature that doesn't change existing behavior for workspace-targeted content.

---

## Future Improvements

1. **User modification detection**: Hash files on launch to detect user edits
2. **Shared content optimization**: Detect when multiple profiles use the same maps
3. **UI for user data management**: Show installed maps per profile in UI
4. **Migration tool**: Convert existing workspace maps to user data system

---

## Related Issues

- Fixes: Maps being copied instead of linked
- Fixes: GameInstallation dependency not auto-selected for GameClients
- Fixes: Multiple profiles sharing the same map files

---

## Reviewers

Please pay special attention to:
1. `UserDataTrackerService.cs` - Core file operations and hard linking logic
2. `ProfileContentLinkerService.cs` - Profile switching coordination
3. `GameLauncher.cs:591-615` - Launch flow integration
4. `GameProfileSettingsViewModel.cs:702-770` - Auto-dependency selection logic
