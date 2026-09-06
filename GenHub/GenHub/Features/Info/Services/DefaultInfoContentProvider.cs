using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Info;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Provides default information sections, guides, and documentation for GenHub.
/// </summary>
public class DefaultInfoContentProvider(IGeneralsOnlinePatchNotesService patchNotesService) : IInfoContentProvider
{
    private readonly List<InfoSection> _sections = CreateContent();
    private readonly IGeneralsOnlinePatchNotesService _patchNotesService = patchNotesService;

    /// <inheritdoc/>
    public Task<IEnumerable<InfoSection>> GetAllSectionsAsync()
    {
        return Task.FromResult(_sections.OrderBy(s => s.Order).AsEnumerable());
    }

    /// <inheritdoc/>
    public Task<InfoSection?> GetSectionAsync(string sectionId)
    {
        return Task.FromResult(_sections.FirstOrDefault(s => s.Id.Equals(sectionId, System.StringComparison.OrdinalIgnoreCase)));
    }

    private static List<InfoSection> CreateContent()
    {
        return
        [
            CreateQuickStartSection(),
            CreateGameProfilesSection(),
            CreateGameSettingsSection(),
            CreateGameProfileContentSection(),
            CreateShortcutsSection(),
            CreateSteamIntegrationSection(),
            CreateLocalContentSection(),
            CreateToolsSection(),
            CreateGeneralsOnlineFAQSection(),
            CreateGeneralsOnlineChangeLogSection(),
            CreateScanForGamesSection(),
            CreateWorkspaceSection(),
            CreateAppUpdatesSection(),
            CreateChangelogSection(),
        ];
    }

    private static InfoSection CreateQuickStartSection()
    {
        return new InfoSection
        {
            Id = "quickstart",
            Title = "Quickstart Guide",
            Description = "Getting started with GenHub.",
            Order = -1,
            Cards =
            [
                new InfoCard
                {
                    Title = "Welcome to GenHub",
                    Content = "Your central launcher for Command & Conquer: Generals and Zero Hour.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **What is GenHub?**
                    GenHub is a modern launcher and manager for **Command & Conquer: Generals** and **Zero Hour**. It keeps your game, mods, custom maps, and multiplayer services organized and isolated so you can switch setups instantly without breaking your original game installation.

                    **Platform Overview:**
                    *   **Game Profiles:** Your primary hub. Scan for your game installation, set up mod configurations, and launch the game.
                    *   **Downloads:** Direct, one-click downloads for community patches, multiplayer services, and community mods.
                    *   **Tools:** Built-in managers for inspecting replays and organizing custom maps.
                    """,
                },
                new InfoCard
                {
                    Title = "Step 1: Scan for Games",
                    Content = "Locate and link your game installation.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Detecting Your Game:**
                    GenHub connects to your existing game files before launching profiles.

                    1.  Navigate to the **Game Profiles** tab.
                    2.  Click the **Scan for Games** button in the toolbar.
                    3.  GenHub automatically searches standard Steam, EA App, Origin, and CD install directories.

                    *Once detected, you can create and launch profiles based on this installation.*
                    """,
                    Actions =
                    [
                        new InfoAction
                        {
                            Label = "Go to Detection Guide",
                            ActionId = "NAV_INFO_scan-games",
                            IconKey = "Magnify",
                            IsPrimary = true,
                        },
                    ],
                },
                new InfoCard
                {
                    Title = "Step 2: Essential Downloads",
                    Content = "Recommended community updates for modern systems.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Recommended Community Additions:**
                    Visit the **Downloads** tab to get recommended updates for modern hardware and online play:

                    *   **Generals Online:** Modern online multiplayer lobby and matchmaking, replacing the discontinued GameSpy service.
                    *   **TheSuperHackers Engine:** Active community engine updates offering widescreen support, high-DPI scaling, and crash fixes.
                    *   **Community Patches:** Game balance, memory enhancements, and stability fixes.
                    """,
                    Actions =
                    [
                        new InfoAction
                        {
                            Label = "Go to Downloads",
                            ActionId = "NAV_Downloads",
                            IconKey = "CloudDownload",
                            IsPrimary = true,
                        },
                        new InfoAction
                        {
                            Label = "Learn about Content",
                            ActionId = "NAV_INFO_game-profile-content",
                            IconKey = "BookOpenVariant",
                        },
                    ],
                },
                new InfoCard
                {
                    Title = "Step 3: Add Local Content",
                    Content = "Import your existing mods and maps.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Adding Your Own Files:**
                    If you already have mod files, standalone maps, or map packs on your PC, you can attach them directly to specific profiles:

                    1.  Go to the **Game Profiles** tab.
                    2.  Click the **Edit Profile** button (pencil icon) on any profile card.
                    3.  Click **Add Local Content**.
                    4.  Select your mod folder, map archive, or map pack.

                    *This content remains linked to that specific profile without touching other profiles or your base game files.*
                    """,
                    Actions =
                    [
                        new InfoAction
                        {
                            Label = "Learn how to Import",
                            ActionId = "NAV_INFO_local-content",
                            IconKey = "FolderUpload",
                            IsPrimary = true,
                        },
                    ],
                },
                new InfoCard
                {
                    Title = "The Core: Manifests & CAS",
                    Content = "How GenHub manages files and saves disk space.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **How Storage Works:**
                    GenHub uses content manifests and a shared storage cache to keep your files organized and fast:

                    *   **Content Manifests:** Package manifests clearly list every file, version, and dependency for each mod or patch.
                    *   **Central Storage Pool (CAS):** Files are stored by content hash in a central cache rather than duplicated across multiple folders.
                    *   **Deduplication:** When multiple mods use identical textures or game assets, GenHub stores that file once, saving gigabytes of disk space.
                    *   **Integrity Verification:** Files are verified with checksums before launch to automatically detect and repair corrupted or missing assets.
                    """,
                    Actions =
                    [
                        new InfoAction
                        {
                            Label = "Storage Settings",
                            ActionId = "NAV_Settings",
                            IconKey = "Harddisk",
                        },
                    ],
                },
                new InfoCard
                {
                    Title = "Automated Maintenance",
                    Content = "Automatic updates and version compatibility.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Background Maintenance:**
                    GenHub handles routine background maintenance automatically:

                    *   **Update Checks:** Automatically checks for service and engine updates before launching your game.
                    *   **Clean Version Management:** Removes outdated patch files cleanly so your profiles always stay on compatible, tested versions.
                    """,
                    Actions =
                    [
                        new InfoAction
                        {
                            Label = "App Updates",
                            ActionId = "NAV_INFO_app-updates",
                            IconKey = "Update",
                        },
                    ],
                },
            ],
        };
    }

    private static InfoSection CreateGameProfilesSection()
    {
        return new InfoSection
        {
            Id = "game-profiles",
            Title = "Game Profiles",
            Description = "Create and manage isolated game configurations.",
            Order = 0,
            Cards =
            [
                new InfoCard
                {
                    Title = "Your Personal Sandbox",
                    Content = "Keep your mods, maps, and game settings isolated and safe.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Isolated Game Profiles:**
                    A profile is an independent configuration for your game. Instead of reinstalling or swapping files manually:

                    1.  **Safety:** Mod files never overwrite your original game installation. If a mod causes problems, your base game remains completely untouched.
                    2.  **Multiple Configurations:** Keep separate profiles for vanilla Zero Hour, Rise of the Reds, ShockWave, or custom balance patches, and switch between them instantly.
                    3.  **Speed:** Workspaces build in milliseconds using file linking, requiring almost zero extra storage on your drive.
                    """,
                },
                new InfoCard
                {
                    Title = "Controls",
                    Content = "Quick reference for profile card buttons.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Profile Card Controls:**
                    1.  **Play:** Launches the game with this profile's active mods, settings, and workspace.
                    2.  **Edit Profile (Pencil):** Opens the profile editor to select mods, maps, and adjust game settings.
                    3.  **Copy Profile (Duplicate):** Clones the profile, including all settings and enabled content, into a new profile.
                    4.  **Desktop Shortcut:** Creates a desktop shortcut to launch this profile directly.
                    5.  **Delete Profile:** Removes the profile and its dedicated workspace configuration.

                    **Copy Profile Feature:**
                    Cloning creates a complete, independent copy of the profile:
                    -   **Identical Settings:** Video, audio, and control options are duplicated.
                    -   **Identical Content:** All active mods, maps, and patches carry over.
                    -   **Independent Workspace:** Modifying the cloned profile never alters the original.

                    **Steam Status:**
                    -   **Gray Icon:** Steam integration is inactive.
                    -   **Blue Icon:** Steam integration is active. Playtime will log to Steam and the Steam Overlay will work in-game.
                    """,
                },
                new InfoCard
                {
                    Title = "Advanced Profile Options",
                    Content = "Custom launch arguments and troubleshooting.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Launch Arguments:**
                    GenHub passes custom command-line arguments directly to the game. For example, use `-quickstart` to skip introduction videos, or `-win` to force windowed mode.

                    **Troubleshooting Logs:**
                    Profile startup and launch logs are recorded in the GenHub AppData directory to help diagnose issues if a game closes unexpectedly.
                    """,
                },
            ],
        };
    }

    private static InfoSection CreateGameSettingsSection()
    {
        return new InfoSection
        {
            Id = "game-settings",
            Title = "Game Settings",
            Description = "Configure display, audio, and engine settings per profile.",
            Order = 1,
            Cards =
            [
                new InfoCard
                {
                    Title = "Standard Audio & Video",
                    Content = "Display and audio settings for the Generals engine (Options.ini).",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Display Settings:**
                    *   **Resolution:** Select your screen resolution. Supports modern widescreen, 1440p, 4K, and Ultrawide displays.
                    *   **Windowed Mode:** Run in a borderless or standard window for smooth Alt-Tabbing on multi-monitor setups.
                    *   **Anti-Aliasing & Gamma:** Smooth jagged 3D edges and fine-tune in-game brightness.

                    **Audio & Controls:**
                    *   **Volume Sliders:** Individual controls for Master, Sound Effects, Music, and Voice levels.
                    *   **Sound Channels:** Maximum simultaneous audio channels (supports up to 128 channels on modern systems).
                    *   **Right-Click Attack:** Switch between classic left-click and modern RTS right-click command schemes.
                    *   **Scroll Speed:** Customize camera movement speed at screen borders.
                    """,
                },
                new InfoCard
                {
                    Title = "TheSuperHackers Engine",
                    Content = "Modern client extensions and stability improvements.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Community Engine Enhancements:**
                    TheSuperHackers (TSH) engine is the active community codebase improving Zero Hour stability and modern feature support.

                    **Engine Improvements:**
                    *   **Cursor Clip:** Restricts the mouse to the game window during matches to prevent accidental clicks on a second monitor.
                    *   **Windowed Edge Scrolling:** Enables smooth camera scrolling at window edges even in windowed mode.
                    *   **Font Scaling:** Automatically scales in-game text and UI for high-DPI and 4K displays.

                    **In-Game Overlays:**
                    *   **Economy Stats:** Live resources-per-minute income rate display.
                    *   **Performance Metrics:** On-screen clock, FPS counter, and network latency indicators.
                    *   **Replay Archiving:** Automatically saves and structures match replays into categorized folders.
                    """,
                },
                new InfoCard
                {
                    Title = "GeneralsOnline Features",
                    Content = "Online multiplayer lobby and matchmaking features.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Modern Multiplayer Integration:**
                    Generals Online provides dedicated online matchmaking, lobbies, and community rankings for Command & Conquer: Generals and Zero Hour.

                    **Lobby & Networking:**
                    *   **Ping & Ranks:** View player latency and competitive ladder rankings directly in the lobby.
                    *   **Seamless Login:** Connect securely using Steam, Discord, or GameReplays authentication.
                    *   **Desktop Notifications:** Receive alerts when friends come online or invite you to matches.
                    *   **Chat Options:** Adjust lobby text size and fade delays to your preference.

                    **In-Game Camera:**
                    *   **Camera Zoom Height:** Customize maximum camera zoom distance for broader battlefield visibility.
                    *   **Camera Pan Speed:** Tune panning sensitivity during multiplayer matches.
                    """,
                },
            ],
        };
    }

    private static InfoSection CreateGameProfileContentSection()
    {
        return new InfoSection
        {
            Id = "game-profile-content",
            Title = "Profile Content",
            Description = "Manage mods, maps, and patches enabled for each profile.",
            Order = 2,
            Cards =
            [
                new InfoCard
                {
                    Title = "Content Types & Hierarchy",
                    Content = "Understand the roles and priority of each content type.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Game Client:**
                    The base game files (Generals or Zero Hour) installed on your system. Every profile uses a client as its base foundation.

                    **Mod:**
                    A major modification changing gameplay, factions, and units (e.g. Rise of the Reds, ShockWave). A profile typically centers around one primary mod.

                    **Map:**
                    An individual custom map file for skirmish and multiplayer battles.

                    **Map Pack:**
                    A bundled collection of maps. Using a map pack lets you toggle an entire tournament pool or custom map collection with a single checkbox.

                    **Patch:**
                    An engine or system-level enhancement that improves stability (such as the 4GB Memory Patch or GenTool).

                    **Addon:**
                    Supplementary visual or audio packs (such as remastered music or HD textures) that sit on top of mods safely.

                    **Tool:**
                    External utilities (such as World Builder or FinalBIG) that can be opened directly from your profile dashboard.
                    """,
                },
                new InfoCard
                {
                    Title = "Cloning Content",
                    Content = "How copying profiles preserves your content setup.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **How Content Duplication Works:**
                    When you use **Copy Profile**, GenHub duplicates your profile's content configuration:

                    *   **Preserved Content:** All active mods, maps, and patches are mirrored into the new profile.
                    *   **Independent Editing:** The clone is fully independent. Adding or removing content in the clone will not change the original profile.
                    *   **Zero Storage Waste:** File linking ensures that cloning a profile does not copy large mod files on your disk. Both profiles reference the shared storage cache.
                    """,
                },
                new InfoCard
                {
                    Title = "Content Editor",
                    Content = "Adding and ordering content in a profile.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Content Workflow:**
                    1.  **Available Content (Bottom):** Shows installed mods, maps, and patches you can add to this profile.
                    2.  **Enabled Content (Top):** Shows content currently active for this profile.
                    3.  **Load Priority:** Content is applied from top to bottom. Items higher in the list take priority if two packages contain conflicting files.
                    4.  **Add Local:** Link external folders or archives without copying them into GenHub.
                    """,
                },
                new InfoCard
                {
                    Title = "Virtual File System",
                    Content = "How files are merged when launching.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Layered File Merging:**
                    When you click Play, GenHub combines all active content into a unified workspace:

                    1.  **Base Layer:** Game client files form the foundation.
                    2.  **Mod Layer:** Mod files overlay and replace base game assets.
                    3.  **Top Layer:** Custom maps, addons, and patches apply with highest priority.
                    """,
                },
            ],
        };
    }

    private static InfoSection CreateShortcutsSection()
    {
        return new InfoSection
        {
            Id = "shortcuts",
            Title = "Desktop Shortcuts",
            Description = "Create one-click desktop shortcuts for your profiles.",
            Order = 3,
            Cards =
            [
                new InfoCard
                {
                    Title = "Headless Mode Launcher",
                    Content = "Launch profiles directly from your desktop.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Direct Desktop Launching:**
                    Shortcuts allow you to start any mod configuration straight from your desktop without keeping the main launcher window open:

                    1.  **Direct Launch:** Double-click the shortcut to start the game immediately.
                    2.  **Silent Setup:** GenHub runs briefly in the background to prepare the profile workspace, then hands off to the game.
                    3.  **Clean Exit:** Workspace temporary files are automatically cleaned up when the game closes.
                    """,
                },
                new InfoCard
                {
                    Title = "Shortcut Creation",
                    Content = "How to add a profile shortcut to your desktop.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Creating a Shortcut:**
                    1.  In **Game Profiles**, right-click any profile card (or click the Desktop shortcut icon).
                    2.  Select **Create Desktop Shortcut**.
                    3.  A standard Windows shortcut (`.lnk`) appears on your desktop.
                    4.  Double-clicking this shortcut launches that specific profile configuration immediately.
                    """,
                },
                new InfoCard
                {
                    Title = "Icon Customization",
                    Content = "Visual icons for your desktop shortcuts.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Shortcut Icons:**
                    GenHub extracts official high-resolution icon resources from the game executable (`generals.exe` or `game.dat`).
                    If your profile uses custom metadata or mod artwork, GenHub converts that image into an icon embedded directly in the shortcut.
                    """,
                },
            ],
        };
    }

    private static InfoSection CreateSteamIntegrationSection()
    {
        return new InfoSection
        {
            Id = "steam-integration",
            Title = "Steam Integration",
            Description = "Track playtime and use the Steam Overlay with mods.",
            Order = 4,
            Cards =
            [
                new InfoCard
                {
                    Title = "AppID Injection",
                    Content = "Use Steam playtime tracking and the overlay with any mod.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Steam Integration:**
                    GenHub connects your mod launches with Steam so you can take advantage of Steam community features:

                    *   **Steam Overlay:** Chat with friends, join invites, and take screenshots in-game.
                    *   **Friend Status:** Displays Command & Conquer: Generals as your current game.
                    *   **Playtime Tracking:** Hours played with mods count toward your official Steam library stats.
                    """,
                },
                new InfoCard
                {
                    Title = "Usage Requirements",
                    Content = "Requirements for Steam integration.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Prerequisites:**
                    To use Steam features:
                    1.  The **Steam desktop application** must be running before launching the game.
                    2.  The active Steam account must own *Command & Conquer: The Ultimate Collection*.

                    *Note: If Steam is not running, GenHub will launch the profile in standard mode without interruption.*
                    """,
                },
                new InfoCard
                {
                    Title = "Time Tracking",
                    Content = "Steam playtime logging across mod profiles.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Playtime Tracking:**
                    Because Steam recognizes the game through GenHub's launcher, all playtime across your various mods, map packs, and profiles is logged to your Steam library.
                    """,
                },
            ],
        };
    }

    private static InfoSection CreateLocalContentSection()
    {
        return new InfoSection
        {
            Id = "local-content",
            Title = "Local Content",
            Description = "Import external mods, maps, and tools from your PC.",
            Order = 5,
            Cards =
            [
                new InfoCard
                {
                    Title = "Universal Import",
                    Content = "Add ZIP archives, folders, and executables into GenHub.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **The 'Add Local' Button:**
                    Use the **Add Local** button to import content from anywhere on your computer:

                    *   **ZIP Archives:** Drag and drop a mod or map pack ZIP archive. GenHub unpacks and registers it automatically.
                    *   **Folders:** Link directly to an unpacked mod folder on your drive without duplicating files.
                    *   **Executables:** Link standalone tools, map editors, or custom game binaries.
                    """,
                },
                new InfoCard
                {
                    Title = "Endless Possibilities",
                    Content = "Map packs, total conversions, and community utilities.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Supported Content:**
                    *   **Map Packs:** Large map collections are automatically cataloged and indexed into individual maps.
                    *   **Total Conversions:** Full game modifications like Rise of the Reds, ShockWave, or Contra can be imported directly.
                    *   **Community Utilities:** Access classic editing tools and utilities right from your profile dashboard.
                    """,
                },
                new InfoCard
                {
                    Title = "Smart Management",
                    Content = "Validation and isolated storage for imported files.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Safe Organization:**
                    GenHub verifies and isolates imported content:

                    1.  **File Verification:** Checks for valid `.map`, `.big`, and executable files before adding them to your library.
                    2.  **Non-Destructive Storage:** Imported files are kept in isolated storage and will never overwrite or alter your base game installation.
                    3.  **Flexibility:** Combine imported map packs, patches, and mods on any profile.
                    """,
                },
            ],
        };
    }

    private static InfoSection CreateToolsSection()
    {
        return new InfoSection
        {
            Id = "tools",
            Title = "Tools & Utilities",
            Description = "Inspect replays and manage custom maps directly.",
            Order = 6,
            Cards =
            [
                new InfoCard
                {
                    Title = "Replay Manager: Import & Parse",
                    Content = "Import and inspect game recordings.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Importing Replays:**
                    *   **Match ID or URL:** Paste a Match ID, GenTool URL, or replay download link and click **Download**.
                    *   **Browse:** Select `.rep` files or `.zip` archives from your PC.
                    *   **Drag & Drop:** Drop replay files directly into the Replay Manager window.

                    **Replay Details:**
                    *   GenHub inspects replay file headers to show the map name, players, and game version before you watch.
                    """,
                },
                new InfoCard
                {
                    Title = "Replay Manager: Cloud & Sharing",
                    Content = "Upload and share replays with other players.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Cloud Sharing:**
                    *   Select replays and click **Upload** to upload them to secure cloud storage.
                    *   A shareable download link is automatically copied to your clipboard.

                    **Upload History:**
                    *   Review recently uploaded replays.
                    *   Copy download links again or remove expired entries from your list.
                    """,
                },
                new InfoCard
                {
                    Title = "Replay Manager: Archiving",
                    Content = "Zip and unzip replay collections.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Creating Archives:**
                    *   Select multiple replays and click **Zip** to compress them into an archive for sharing or tournament submissions.

                    **Extracting Archives:**
                    *   Select a `.zip` archive in the replay list and click **Uncompress** to extract all `.rep` files directly into your replay folder.
                    """,
                },
                new InfoCard
                {
                    Title = "Map Manager: Library",
                    Content = "Browse and organize custom maps.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Map Management:**
                    *   **Search:** Filter custom maps quickly by name or folder.
                    *   **Minimap Previews:** Displays map preview thumbnails extracted directly from map files.
                    *   **Import:** Drag and drop map folders or `.zip` archives to install them instantly.

                    **Actions:**
                    *   **Delete:** Remove unused maps from your drive.
                    *   **Open Folder:** Open the specific map folder in Windows Explorer.
                    """,
                },
                new InfoCard
                {
                    Title = "Map Manager: Map Packs",
                    Content = "Organize maps into reusable collections.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **What is a Map Pack?**
                    A Map Pack bundles multiple maps together (such as a tournament map pool or 4-player FFA collection).

                    **Creating a Map Pack:**
                    1.  Select multiple maps with `Ctrl+Click` or `Shift+Click`.
                    2.  Click **Pack** in the top-right toolbar.
                    3.  Enter a name and click **Create MapPack**.

                    Once created, you can toggle the entire map collection on or off for any profile in one click.
                    """,
                },
            ],
        };
    }

    private static InfoSection CreateScanForGamesSection()
    {
        return new InfoSection
        {
            Id = "scan-games",
            Title = "Game Detection",
            Description = "Automatically detect and verify game installations.",
            Order = 7,
            Cards =
            [
                new InfoCard
                {
                    Title = "Auto-Detection",
                    Content = "How GenHub locates installed games on your computer.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Detection Methods:**
                    GenHub locates game installations by scanning:
                    1.  **Steam Libraries:** Automatically detects Steam installations of Command & Conquer: The Ultimate Collection.
                    2.  **EA App / Origin:** Locates official EA App install directories and registry records.
                    3.  **Classic CD & Retail:** Checks standard installation paths and registry keys for classic disk editions.

                    If your game is installed in a custom location, click **Browse** to link its folder manually.
                    """,
                },
                new InfoCard
                {
                    Title = "Signature Verification",
                    Content = "Integrity checks and version verification.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Binary Verification:**
                    GenHub calculates SHA-256 hashes of `generals.exe` and `game.dat` to confirm game versions and file integrity.
                    *   **Verified:** Matches known official releases (such as Steam edition, EA App, The First Decade, or v1.04).
                    *   **Unverified:** Custom or unrecognized binaries are labeled as Unverified, but remain fully launchable.
                    """,
                },
            ],
        };
    }

    private static InfoSection CreateWorkspaceSection()
    {
        return new InfoSection
        {
            Id = "workspaces",
            Title = "Virtual Workspaces",
            Description = "Workspace strategies, file linking techniques, and isolation mechanics.",
            Order = 8,
            Cards =
            [
                new InfoCard
                {
                    Title = "The Magic Mirror",
                    Content = "Understanding how isolated game workspaces work.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **How Workspaces Work:**
                    When you click Play, GenHub instantly prepares a dedicated workspace folder for that specific profile.

                    **Key Benefits:**
                    1.  **Zero Extra Disk Space:** In linked modes (HardLink and SymlinkOnly), the workspace functions as a complete multi-gigabyte game folder while consuming virtually 0 MB of extra disk space.
                    2.  **Complete Profile Isolation:** Mods and configurations live in dedicated profile workspaces. Your main game directory remains untouched, so files never get mixed up. (For mods that modify game binaries in-place, select Hybrid or Full Copy mode).
                    3.  **Instant Switching:** Switch between large total conversions like *Rise of the Reds* and *ShockWave* in seconds without reinstalling or moving files.
                    """,
                },
                new InfoCard
                {
                    Title = "Workspace Strategies Compared",
                    Content = "Comparing HardLink, SymlinkOnly, HybridCopySymlink, and FullCopy strategies.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Choosing the Right Strategy:**
                    GenHub supports four file linking strategies under **Settings -> Game Configuration**:

                    *   **HardLink (Default & Recommended):**
                        *   *How it works:* Creates direct filesystem pointers (hard links) on the same drive. If your workspace and game files are on different drives, GenHub automatically falls back to copying files.
                        *   *Disk Space:* **0 bytes** extra storage when on the same drive (copies if across different drives).
                        *   *Speed:* Instant (< 50ms) on the same volume.
                        *   *Privileges:* No administrator privileges or Developer Mode required.
                        *   *Recommendation:* Keep your workspaces and game installation on the **same drive** (e.g. both on `C:` or both on `D:`) for optimal zero-space performance.

                    *   **SymlinkOnly:**
                        *   *How it works:* Creates symbolic links pointing to source files and directories.
                        *   *Disk Space:* **Negligible** (~a few KB of link pointers).
                        *   *Speed:* Instant (< 50ms).
                        *   *Advantage:* Links seamlessly across **different drives and partitions**.
                        *   *Requirement:* On Windows, requires **Administrator rights** or **Developer Mode** enabled in Windows Settings.

                    *   **HybridCopySymlink (Balanced Compatibility):**
                        *   *How it works:* Copies essential engine files, scripts, and configuration files into the workspace while symlinking large media files (textures, audio, and video).
                        *   *Disk Space:* Balanced footprint (copies key configs, links media).
                        *   *Speed:* Fast (1-2 seconds).
                        *   *Advantage:* Protects configuration files from cross-profile conflicts while keeping disk usage low.

                    *   **FullCopy (Universal Fallback):**
                        *   *How it works:* Physically copies every game and mod file into the workspace directory.
                        *   *Disk Space:* Uses the full game size (**2-5+ GB** per profile).
                        *   *Speed:* Slower (10-30+ seconds depending on drive speed).
                        *   *Advantage:* Maximum compatibility across external drives, network drives, and restricted environments.
                    """,
                },
                new InfoCard
                {
                    Title = "Hardlinks vs Symlinks vs Copies: Deep Dive",
                    Content = "How file linking differs under the hood.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **How Linking Works Under the Hood:**

                    *   **Hardlink:**
                        A hardlink points directly to the existing file data on disk at the filesystem level. Because the underlying file data is shared, creating a hardlink takes zero extra storage. Hardlinks must reside on the same drive partition as the original file.

                    *   **Symlink (Symbolic Link):**
                        A symlink is a lightweight pointer that stores a path to the target file or folder, similar to a transparent operating system shortcut. Symlinks can cross different drives, but Windows security policies require elevated privileges or Developer Mode to create them.

                    *   **Full Copy:**
                        A complete duplicate of the file written to a new location on disk.

                    **Automatic Fallback:**
                    If you configure Symlink mode but run GenHub without administrator rights or Developer Mode, GenHub automatically falls back to hardlinks when files reside on the same drive, ensuring your game launches without interruption.
                    """,
                },
                new InfoCard
                {
                    Title = "Troubleshooting & Permissions",
                    Content = "Resolving common permissions and workspace build errors.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Common Issues & Solutions:**

                    *   **"Access Denied" or Privilege Errors:**
                        *   If using the Symlink strategy on Windows, enable **Developer Mode** in *Windows Settings -> System -> For developers*, or run GenHub as Administrator.
                        *   Alternatively, switch your Default Workspace Strategy to **HardLink** in GenHub Settings.
                    *   **Cross-Drive Linking & Storage:**
                        *   Hardlinks require both the game files and workspace to be on the same drive volume to achieve zero-space linking. If they are on different drives, GenHub falls back to copying files.
                        *   To keep workspaces fast and zero-space, place your CAS pool and workspace directories on the same drive as your game installation in **Settings -> Data Directories**, or enable Developer Mode for symlinks.
                    *   **"File In Use" / Locked File Warnings:**
                        *   Make sure all instances of `generals.exe` and `game.dat` are closed before switching profiles or rebuilding workspaces.
                    """,
                },
                new InfoCard
                {
                    Title = "Performance Specs",
                    Content = "Efficiency, speed, and integrity metrics across strategies.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Strategy Performance Summary:**

                    *   **HardLink:**
                        *   *Creation Time:* < 50ms on same volume
                        *   *Disk Overhead:* 0 MB on same volume (copies if across different drives)
                        *   *Integrity:* Shared data clusters (CAS objects remain immutable in the cache)
                    *   **SymlinkOnly:**
                        *   *Creation Time:* < 50ms
                        *   *Disk Overhead:* < 1 MB
                        *   *Integrity:* Pointer redirection across drives
                    *   **Hybrid:**
                        *   *Creation Time:* 1-2 seconds
                        *   *Disk Overhead:* Small (copies essential configs, links media assets)
                        *   *Integrity:* Isolated configs, shared media links
                    *   **Full Copy:**
                        *   *Creation Time:* 10-30 seconds
                        *   *Disk Overhead:* Full game size (2,000 - 5,000+ MB)
                        *   *Integrity:* Total physical file separation
                    """,
                },
            ],
        };
    }

    private static InfoSection CreateAppUpdatesSection()
    {
        return new InfoSection
        {
            Id = "app-updates",
            Title = "App Updates",
            Description = "Manage launcher updates and release channels.",
            Order = 9,
            Cards =
            [
                new InfoCard
                {
                    Title = "Version Control",
                    Content = "Official releases and update checking.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **How Updates Work:**
                    GenHub checks for updates automatically from official GitHub releases. When a new version is published, GenHub verifies the release and displays an update notification.
                    """,
                },
                new InfoCard
                {
                    Title = "Update Workflow",
                    Content = "Applying updates seamlessly.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Update Process:**
                    1.  **Notification:** An update banner appears when a new release is available.
                    2.  **Background Download:** Updates download quietly in the background without interrupting your gameplay.
                    3.  **Fast Restart:** Clicking **Restart** applies the update in seconds and restores your launcher session.
                    """,
                },
                new InfoCard
                {
                    Title = "Rollback Capability",
                    Content = "How to revert to an earlier release if needed.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Reverting to Previous Versions:**
                    GenHub automatically preserves your profile configurations and settings during updates. If you ever need to use an earlier build, download the previous release archive from GitHub and extract it into your GenHub installation directory.
                    """,
                },
            ],
        };
    }

    private static InfoSection CreateChangelogSection()
    {
        return new InfoSection
        {
            Id = "changelogs",
            Title = "Changelog",
            Description = "Version history.",
            Order = 10,
            Cards = [],
        };
    }

    private static InfoSection CreateGeneralsOnlineFAQSection()
    {
        return new InfoSection
        {
            Id = "faq",
            Title = "Frequently Asked Questions",
            Description = "Common questions about the Generals Online service.",
            Order = 7,
            Cards =
            [
                new InfoCard
                {
                    Title = "What is Generals Online?",
                    Content = "Generals Online is a modern multiplayer and lobby platform for Command & Conquer: Generals and Zero Hour.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Generals Online replaces the discontinued GameSpy service with modern multiplayer matchmaking, lobby features, automatic updates, and ladder rankings—preserving classic gameplay while delivering stable online play on modern PCs.",
                },
                new InfoCard
                {
                    Title = "Do I need a clean install of Zero Hour?",
                    Content = "No. Generals Online works alongside your existing installation.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = "You do not need a fresh game installation or to delete existing files. GenHub isolates Generals Online so your base game files remain untouched.",
                },
                new InfoCard
                {
                    Title = "Can I play Generals Online if I have GenTool or GenPatcher installed?",
                    Content = "Yes. Generals Online is fully compatible with GenTool and GenPatcher.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Generals Online runs in its own profile environment and works alongside GenTool widescreen and anti-cheat features without conflicts.",
                },
                new InfoCard
                {
                    Title = "Can I use custom UI or control bars?",
                    Content = "Yes. Custom UI assets and control bars are supported.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Custom UI modifications, such as HUD control bars, work normally in Generals Online.",
                },
                new InfoCard
                {
                    Title = "Does Generals Online modify my original game files?",
                    Content = "No. Your original installation files are never modified.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Generals Online runs from an isolated profile workspace. Your main game folder remains clean and untouched.",
                },
                new InfoCard
                {
                    Title = "Are custom maps supported?",
                    Content = "Yes. Custom maps and in-lobby map transfers are supported.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = "Generals Online supports in-game and lobby map downloads so you can play custom maps with other players seamlessly.",
                },
                new InfoCard
                {
                    Title = "How do I launch Generals Online?",
                    Content = "Launch through GenHub or your profile desktop shortcut.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = "Select your Generals Online profile in GenHub and click Play, or launch it directly with a desktop shortcut created from that profile.",
                },
                new InfoCard
                {
                    Title = "Which game versions are supported?",
                    Content = "Developed and tested for official Steam and EA App / Origin releases.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    Generals Online is designed for official Steam and EA releases. For the best experience and easiest setup, the Steam release of Command & Conquer: The Ultimate Collection is recommended.
                    """,
                },
                new InfoCard
                {
                    Title = "How do I log in?",
                    Content = "Sign in securely using Steam, Discord, or GameReplays.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = "Generals Online uses OpenID authentication. You authenticate directly through Steam, Discord, or GameReplays—your account passwords are never seen or stored by Generals Online.",
                },
                new InfoCard
                {
                    Title = "Is logging in safe?",
                    Content = "Yes. OpenID ensures your account password remains completely private.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "OpenID only transmits a secure account identifier to verify your identity. Your login credentials are handled directly by Steam, Discord, or GameReplays.",
                },
                new InfoCard
                {
                    Title = "How do I check if the service is online?",
                    Content = "Check the in-game status, the community Discord, or the status page.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = "Live service status is shown on the login screen, with real-time announcements available on the community Discord.",
                },
                new InfoCard
                {
                    Title = "How do I report bugs or suggest features?",
                    Content = "Join the community Discord to submit feedback.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = "The development team actively tracks issues and community suggestions in dedicated Discord channels.",
                },
                new InfoCard
                {
                    Title = "How are updates delivered?",
                    Content = "Updates download automatically through the launcher.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = "When an update is released, GenHub detects and applies it so you are always on the latest version.",
                },
                new InfoCard
                {
                    Title = "Do I need third-party VPN tools (Hamachi, Radmin, GameRanger)?",
                    Content = "No. Online matchmaking is built directly into the service.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Generals Online includes native networking and matchmaking. You do not need third-party virtual LAN software or external wrappers to play online.",
                },
                new InfoCard
                {
                    Title = "Do I need to forward router ports?",
                    Content = "No. Built-in NAT traversal connects players automatically.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Modern NAT traversal handles player connections automatically without requiring manual port forwarding on your home router.",
                },
                new InfoCard
                {
                    Title = "Is network communication secure?",
                    Content = "Yes. Game traffic is encrypted using AES-256.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = "Network traffic uses industry-standard AES-256-GCM encryption, providing significantly better security than the original game engine's unencrypted packets.",
                },
                new InfoCard
                {
                    Title = "Why did Windows Firewall prompt for permission?",
                    Content = "Windows prompts when a new app accesses the network for the first time.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = "When connecting to multiplayer servers for the first time, Windows Firewall asks to allow network access. Click Allow to enable online connectivity.",
                },
                new InfoCard
                {
                    Title = "What are connection relays?",
                    Content = "Relays route traffic when direct peer-to-peer connections are blocked.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "If two players have strict firewalls that prevent direct peer-to-peer connection, traffic routes seamlessly through community relay servers (similar to Steam networking or CNCNet tunnels).",
                },
                new InfoCard
                {
                    Title = "Do relays cause lag or performance drops?",
                    Content = "Typically no. Relays use high-bandwidth, low-latency backbone servers.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Relay servers are hosted on high-speed backbones and often provide comparable or better latency than congested direct peer-to-peer routes.",
                },
                new InfoCard
                {
                    Title = "How does the game select which relay to use?",
                    Content = "Relay connections are formed dynamically on a player-to-player basis.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    Relay connections are established dynamically per player pair, selecting the server location with the lowest latency for that match.
                    Users in the same lobby can connect through different regional edge nodes to achieve optimal ping.
                    """,
                },
                new InfoCard
                {
                    Title = "Are relays secure?",
                    Content = "Yes. Relays cannot decrypt match traffic.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = "Relay servers forward encrypted packets and do not have access to the encryption keys required to read or inspect traffic.",
                },
                new InfoCard
                {
                    Title = "Can I host a relay?",
                    Content = "Community relay hosting is not needed at this time.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Generals Online operates on global edge infrastructure spanning hundreds of data centers worldwide, delivering low latency without requiring community relay hosting.",
                },
            ],
        };
    }

    private static InfoSection CreateGeneralsOnlineChangeLogSection()
    {
        return new InfoSection
        {
            Id = "go-changelog",
            Title = "Changelog",
            Description = "View the latest changes and updates to the Generals Online service.",
            Order = 8,
            Cards = [], // Content managed by dynamic view
        };
    }
}
