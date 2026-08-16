# Pull Request: feat/game-profile-system: Implement fully integrated Game Profile management

## 1. Goal
To create a complete Game Profile management system that allows users to create, edit, and delete profiles. This system will serve as the central hub that connects a `GameVersion`, installed `Content`, a `WorkspaceStrategy`, and `LaunchOptions` into a single, user-configurable entity.

## 2. Architectural Solution
This feature expands the existing `GameProfile` model and introduces a `GameProfileService` to manage the lifecycle of profiles (CRUD operations). A `GameProfileSettingsViewModel` and corresponding window will provide the UI for users to create and configure their profiles. The `GameProfile` will now be the primary input for both the `IWorkspaceManager` and the `IContentDiscoveryService`'s installation methods, ensuring all operations are correctly associated with a user's configuration.

## 3. Files Added / Modified
*   GenHub.Core/Interfaces/GameProfiles/IGameProfileService.cs (new)
*   GenHub.Core/Models/GameProfiles/GameProfile.cs (modified to be a full model)
*   GenHub/Features/GameProfiles/Services/GameProfileService.cs (new)
*   GenHub/Features/GameProfiles/ViewModels/GameProfileSettingsViewModel.cs (modified)
*   GenHub/Features/GameProfiles/Views/GameProfileSettingsWindow.axaml (modified)
*   GenHub/Features/GameProfiles/ViewModels/GameProfileItemViewModel.cs (modified to represent a full profile)
*   GenHub/Features/GameProfiles/Views/GameProfileCardView.axaml (modified)
*   GenHub/Common/ViewModels/MainViewModel.cs (modified to manage a list of profiles)
*   GenHub/Infrastructure/DependencyInjection/AppServices.cs (modified to add IGameProfileService)

## 4. Git Commit Strategy
```powershell
# 1. Start from the target integration branch
git checkout main
git pull origin main

# 2. Create your feature branch
git checkout -b feat/game-profile-system

# --- Commit 1: Core Profile Service and Model ---
git add GenHub.Core/Interfaces/GameProfiles/
git add GenHub.Core/Models/GameProfiles/
git commit -m "feat(core): Add GameProfile service and expand model"

# --- Commit 2: Implement GameProfile Service ---
git add GenHub/Features/GameProfiles/Services/GameProfileService.cs
git commit -m "feat(profiles): Implement service for GameProfile management"

# --- Commit 3: Implement Profile Creation/Editing UI ---
git add GenHub/Features/GameProfiles/ViewModels/GameProfileSettingsViewModel.cs
git add GenHub/Features/GameProfiles/Views/GameProfileSettingsWindow.axaml
git commit -m "feat(ui): Implement UI for creating and editing game profiles"

# --- Commit 4: Integrate Profiles into Main UI ---
git add GenHub/Common/ViewModels/MainViewModel.cs
git add GenHub/Features/GameProfiles/ViewModels/GameProfileItemViewModel.cs
git add GenHub/Features/GameProfiles/Views/GameProfileCardView.axaml
git commit -m "feat(ui): Integrate profile list into main application view"

# --- Commit 5: Integrate Profiles with Content and Workspace Systems ---
git add GenHub.Core/Interfaces/Content/IContentDiscoveryService.cs
git add GenHub.Core/Interfaces/Workspace/IWorkspaceManager.cs
git add GenHub/Features/Content/Services/ContentDiscoveryService.cs
git add GenHub/Features/Workspace/WorkspaceManager.cs
git commit -m "refactor(core): Integrate profiles as the driver for content and workspace operations"

# 3. Push your branch to remote
git push --set-upstream origin feat/game-profile-system
```

## 5. Pull Request Details
**Title:**
feat/game-profile-system: Implement fully integrated Game Profile management

**Description:**
1.  **What changed** – This PR introduces a full-featured `GameProfileService` for managing user configurations. It provides the UI for creating and editing profiles and refactors the content and workspace systems to be driven by a `GameProfile`.
2.  **Why** – The `GameProfile` is the central concept for the user. This feature makes it a concrete, manageable entity and fully integrates it with all other systems, completing the core architectural vision.
3.  **How** – A new `GameProfileService` handles loading and saving profiles from a JSON file. The `MainViewModel` uses this service to display the list of profiles. When a user installs content or launches a game, the action is now associated with a specific `GameProfile`, which provides all the necessary context (base version, content list, workspace strategy) for the operation.

**Testing:**
-   Unit tests for `GameProfileService` covering CRUD operations.
-   ViewModel tests for `GameProfileSettingsViewModel` to verify configuration logic.
-   Integration tests to ensure that installing content to a profile correctly updates its content list and that launching uses the correct workspace and launch options.

**Next Steps:**
-   Implement profile import/export functionality.
-   Add cloud synchronization for game profiles.
