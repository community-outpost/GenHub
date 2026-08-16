# Pull Request: feat/game-launching: Implement game launching from prepared workspaces

## 1. Goal
To enable users to launch a `GameProfile` from a prepared workspace, including support for custom launch arguments and process monitoring.

## 2. Architectural Solution
This feature introduces a dedicated `IGameLauncher` service responsible for starting and monitoring game processes. It uses a `GameLaunchConfiguration` object to define all parameters for the launch, such as the executable path, working directory, and arguments. The `GameProfileLauncherViewModel` will be responsible for creating this configuration from a `GameProfile` and its associated `WorkspaceInfo`, and then invoking the `IGameLauncher`.

## 3. Files Added / Modified
*   GenHub.Core/Interfaces/Launching/IGameLauncher.cs (new)
*   GenHub.Core/Models/Launching/GameLaunchConfiguration.cs (new)
*   GenHub.Core/Models/Launching/GameProcessInfo.cs (new)
*   GenHub.Core/Models/Results/LaunchResult.cs (new)
*   GenHub/Features/Launching/GameLauncher.cs (new)
*   GenHub/Features/GameProfiles/ViewModels/GameProfileLauncherViewModel.cs (new)
*   GenHub/Features/GameProfiles/Views/GameProfileLauncherView.axaml (new)
*   GenHub/Infrastructure/DependencyInjection/WorkspaceModule.cs (modified to add IGameLauncher)

## 4. Git Commit Strategy
```powershell
# 1. Start from the target integration branch
git checkout main
git pull origin main

# 2. Create your feature branch
git checkout -b feat/game-launching

# --- Commit 1: Core Launching Contracts and Models ---
git add GenHub.Core/Interfaces/Launching/
git add GenHub.Core/Models/Launching/
git add GenHub.Core/Models/Results/LaunchResult.cs
git commit -m "feat(core): Add contracts and models for game launching"

# --- Commit 2: Implement GameLauncher Service ---
git add GenHub/Features/Launching/GameLauncher.cs
git commit -m "feat(launching): Implement GameLauncher service"

# --- Commit 3: UI and ViewModel for Launching ---
git add GenHub/Features/GameProfiles/ViewModels/GameProfileLauncherViewModel.cs
git add GenHub/Features/GameProfiles/Views/GameProfileLauncherView.axaml
git commit -m "feat(ui): Add viewmodel and view for game profile launching"

# --- Commit 4: DI Integration ---
git add GenHub/Infrastructure/DependencyInjection/WorkspaceModule.cs
git commit -m "feat(infra): Register game launcher service in DI"

# 3. Push your branch to remote
git push --set-upstream origin feat/game-launching
```

## 5. Pull Request Details
**Title:**
feat/game-launching: Implement game launching from prepared workspaces

**Description:**
1.  **What changed** – Introduced a new `IGameLauncher` service and its implementation to handle the logic of starting a game executable from a prepared workspace. Added a `GameProfileLauncherViewModel` to connect the UI to this new service.
2.  **Why** – This is the final step in the core user workflow, allowing users to actually play the game configurations they have created. It decouples the UI from the complexities of process management.
3.  **How** – The `GameProfileLauncherViewModel` will be associated with a `GameProfile`. When the user clicks "Launch", the ViewModel retrieves the `WorkspaceInfo` for that profile. It then constructs a `GameLaunchConfiguration` object using the `ExecutablePath` and `WorkingDirectory` from the workspace, combined with any custom arguments from the `GameProfile`. This configuration is passed to `IGameLauncher.LaunchGameAsync` to start the game.

**Testing:**
-   Unit tests for `GameLauncher` to verify correct `ProcessStartInfo` configuration.
-   Unit tests for `GameProfileLauncherViewModel` to ensure it correctly builds the `GameLaunchConfiguration`.
-   Manual integration testing to confirm that a game can be launched successfully from a prepared workspace.

**Next Steps:**
-   Add advanced features like process monitoring to detect if the game is still running.
-   Integrate with a "playtime" tracking feature.
-   Handle game-specific launch requirements (e.g., registry keys, environment variables).
