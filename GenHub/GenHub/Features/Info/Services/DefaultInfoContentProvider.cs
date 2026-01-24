using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Info;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Default implementation of the info content provider, providing complete user guide content.
/// </summary>
public class DefaultInfoContentProvider(IGeneralsOnlinePatchNotesService patchNotesService) : IInfoContentProvider
{
    private readonly List<InfoSection> _sections = CreateContent();
    private readonly IGeneralsOnlinePatchNotesService _patchNotesService = patchNotesService;

    /// <summary>
    /// Gets all info sections asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation containing the collection of info sections.</returns>
    public Task<IEnumerable<InfoSection>> GetAllSectionsAsync()
    {
        // Return the pre-loaded sections
        return Task.FromResult(_sections.OrderBy(s => s.Order).AsEnumerable());
    }

    /// <summary>
    /// Gets a specific info section by its identifier asynchronously.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns>A task representing the asynchronous operation containing the info section or null if not found.</returns>
    public Task<InfoSection?> GetSectionAsync(string sectionId)
    {
        return Task.FromResult(_sections.FirstOrDefault(s => s.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase)));
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
                    Content = "Your central hub for Command & Conquer: Generals & Zero Hour.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **What is GenHub?**
                    GenHub is a unified launcher designed to make managing your **Command & Conquer: Generals & Zero Hour** experience simple. It solves the mess of having multiple mods, maps, and patches by keeping everything isolated and organized.

                    **Platform Overview:**
                    *   **Game Profiles:** This is your main dashboard. Use it to automatically scan for your game installation, create isolated workspaces for different mods, and launch the game.
                    *   **Downloads:** The built-in browser for downloading essential community patches, multiplayer services, and mod updates.
                    *   **Tools:** A suite of utilities for managing Replays and Maps without leaving the app.
                    """,
                },
                new InfoCard
                {
                    Title = "Step 1: Scan for Games",
                    Content = "Detect your installation to get started.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Detecting Your Game:**
                    GenHub needs to know where your game is installed before it can do anything.

                    1.  Navigate to the **Game Profiles** tab.
                    2.  Click the **SCAN** button in the top toolbar.
                    3.  GenHub will search your system and detect your Steam, EA App, or CD installation automatically.

                    *Once detected, you can detect profiles based on this installation.*
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
                    Content = "Get the community recommended updates.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Recommended Setup:**
                    Head over to the **Downloads** tab to grab the essential updates that every player should have. We recommend installing:

                    *   **Generals Online:** The modern replacement for GameSpy to play online.
                    *   **TheSuperHackers:** Provides weekly code fixes and mission content.
                    *   **Community Patch:** Critical stability fixes for the base game.

                    *You can also browse and download other mods and tools in this section.*
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
                    Content = "Importing your own Mods and Maps.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **How to add your own files:**
                    If you have mods, maps, or mappacks already on your computer, you can add them to specific profiles without cluttering your main game folder.

                    1.  Go to the **Game Profiles** tab.
                    2.  Click the **Pencil Icon (Edit)** on any profile card.
                    3.  Click the **Add Local Content** button.
                    4.  Select your Mod folder, Map zip, or Mappack.

                    *This content will only be active for that specific profile.*
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
                    Content = "How GenHub handles your game data efficiently.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **The Engine Under the Hood:**
                    GenHub uses a sophisticated storage system to keep your installation clean and fast.

                    *   **Content Manifests**: Every mod or update is defined by a `ContentManifest`. Think of this as the "DNA" of the package—it lists every file, its exact version, and its dependencies.
                    *   **Declarative Packages**: Content in GenHub is "declarative." Instead of messy installers, GenHub reads the manifest and reconciles your game folder to match exactly what is defined.
                    *   **CAS (Content Addressable Storage)**: Files are stored in a central "Pool" based on their digital fingerprint (hash), not their filename.
                    *   **Deduplication**: If three different mods use the same 1GB texture file, GenHub only stores it **once** in the CAS, saving you massive amounts of disk space.
                    *   **Integrity**: Because everything is hash-based, GenHub can instantly verify if a file is corrupted or modified and fix it automatically.

                    *This system ensures that your profiles remain isolated and your disk usage stays optimal.*
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
                    Content = "Updates and compatibility checks.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Keeps your game clean:**
                    GenHub handles the messy parts of game management for you.

                    *   **Auto-Updates:** When you launch the game, GenHub automatically checks for updates to services like GeneralsOnline.
                    *   **Version Control:** It automatically cleans up old versions of patches and ensures all your profiles are using the latest compatible files, so you don't have to manually update each one.
                    """,
                    Actions =
                    [
                        new InfoAction
                        {
                            Label = "App Utils",
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
            Description = "Manage isolation-based game configurations.",
            Order = 0,
            Cards =
            [
                new InfoCard
                {
                    Title = "Your Personal Sandbox",
                    Content = "Keeping your game version, mods, and maps separate.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Your Personal Sandbox:**
                    A Profile is like a container that keeps your game version, mods, and maps separate from everything else.

                    **Why use them?**
                    1.  **Safety:** You can mess up a profile completely, and your actual game installation remains untouched.
                    2.  **Variety:** Have one profile for *Rise of the Reds*, another for *ShockWave*, and switch instantly.
                    3.  **Speed:** Profiles are virtual. They take up almost no space and build in milliseconds.
                    """,
                },
                new InfoCard
                {
                    Title = "Controls",
                    Content = "Managing your profiles.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Button Guide:**
                    1.  **Play:** Launches the game using this profile's specific mod configuration.
                    2.  **Edit Content (Pencil):** Choose which Mods, Maps, and Patches are active for this profile.
                    3.  **Settings (Gear):** Configure game options (Resolution, Audio) specifically for this profile.

                    **Steam Status:**
                    -   **Gray Icon:** Steam is not connected. Time tracking is off.
                    -   **Color Icon:** Steam is active. Your playtime will be tracked, and the Overlay will work.
                    """,
                },
                new InfoCard
                {
                    Title = "Advanced Profile Options",
                    Content = "Startup arguments and debugging.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Launch Arguments:**
                    GenHub passes arguments directly to the game. Use `-quickstart` to skip intros or `-win` for windowed mode (if not set in settings).

                    **Debugging:**
                    Check the "Logs" folder in AppData for profile startup traces.
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
            Description = "Configure `Options.ini` settings per profile.",
            Order = 1,
            Cards =
            [
                new InfoCard
                {
                    Title = "Standard Audio & Video",
                    Content = "Configuration for the base Generals engine (Options.ini).",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Display Settings**
                    *   **Resolution:** Select your screen size. Supports modern presets up to 4K and Ultrawide.
                    *   **Windowed Mode:** Essential for multi-monitor setups to prevent crashes during Alt-Tabbing.
                    *   **Anti-Aliasing:** Smooths jagged edges on 3D models.
                    *   **Gamma:** Adjusts in-game brightness.

                    **Audio & Gameplay**
                    *   **Volume Sliders:** Master, SFX, Music, and Speech levels.
                    *   **Sound Channels:** Max simultaneous sounds (Default is 16, up to 128 for high-end PCs).
                    *   **Right-Click Attack:** Switch from classic Left-Click to modern RTS Right-Click controls.
                    *   **Scroll Speed:** Edge-scrolling sensitivity.
                    """,
                },
                new InfoCard
                {
                    Title = "TheSuperHackers Engine",
                    Content = "Advanced client extensions and stability fixes.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Active Development Build**
                    TheSuperHackers (TSH) is the **primary build being worked on actively** by the community developers. It provides the base for all modern feature testing and stability improvements.

                    **Engine Enhancements**
                    *   **Cursor Capture:** Locks the mouse inside the game window. Configurable for Menus vs Gameplay and Fullscreen vs Windowed.
                    *   **Edge Scrolling:** Enables camera movement at screen edges even in windowed mode.
                    *   **Font Scaling:** Adjust resolution-based font sizes for better readability on high-DPI displays.

                    **In-Game Information**
                    *   **Money per Minute:** Real-time income rate display.
                    *   **Time & Performance:** Overlays for System Time, FPS, and Network Latency.
                    *   **Auto-Replay Archiving:** Automatically organizes replay files into a structured directory.
                    """,
                },
                new InfoCard
                {
                    Title = "GeneralsOnline Features",
                    Content = "Social, Networking, and Lobby integration.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Integrated Evolution**
                    GeneralsOnline is the modern lobby service that powers online play. **Any Generals online settings inherit directly from TSH changes**, ensuring a unified experience between offline and online play.

                    **Network & Social**
                    *   **Ping & Ranks:** Displays player latency and ladder rankings in the lobby.
                    *   **Auto-Login/Remember Me:** Streamlines the connection process.
                    *   **Smart Notifications:** Desktop-style alerts when friends come online or send requests.
                    *   **Chat Customization:** Adjustable font sizes and fade-out durations for the lobby chat.

                    **Game Camera**
                    *   **Camera Height:** Specialized logic to handle zoom limits.
                    *   **Move Speed Ratio:** Sensitivity of camera movement in the online engine.
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
            Description = "Manage Mods, Maps, and Patches.",
            Order = 2,
            Cards =
            [
                new InfoCard
                {
                    Title = "Content Types & Hierarchy",
                    Content = "Definitions and load-order priority.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Game Client**
                    The root game content. This is the unmodified version of C&C Generals or Zero Hour installed on your system.
                    *Use Case:* Used as the base for every profile. You might switch this if you have multiple game versions (e.g., a "Clean" Install vs a "Modded" Install).

                    **Mod**
                    A major game modification that alters gameplay, factions, and units.
                    *Use Case:* Activate "Rise of the Reds" to play with new factions like the ECA, or "ShockWave" for enhanced generals. Mods serve as the core experience for a profile.

                    **Map**
                    A custom battlefield for Skirmish or Multiplayer modes.
                    *Use Case:* Add individual maps like "Tournament Desert" or custom mission maps that you downloaded from community sites.

                    **Map Pack**
                    A curated collection of multiple maps bundled together.
                    *Use Case:* Instead of cluttering your list with 100 separate map files, use a Map Pack to enable an entire tournament pool or "6-Player Maps" collection with a single checkbox.

                    **Patch**
                    A system-level enhancement that runs alongside the game engine.
                    *Use Case:* Essential for modern stability. Use the "4GB Patch" to stop out-of-memory crashes, or "GenTool" for wide-screen support and anti-cheat features online.

                    **Addon**
                    Supplementary files that add cosmetic or audio changes without breaking game compatibility.
                    *Use Case:* Enable an "Original Soundtrack Remaster" or "HD Texture Pack" that works safely on top of the base game or other mods.

                    **Tool**
                    Standalone executables that perform specific tasks outside the game.
                    *Use Case:* Link "World Builder" to edit maps, or "FinalBig" to inspect game files, making them accessible directly from your profile dashboard.
                    """,
                },
                new InfoCard
                {
                    Title = "Content Editor",
                    Content = "Assignment and ordering of content processing.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Workflow:**
                    1.  **Add Content (Bottom Pane):** Lists all available content matches.
                    2.  **Enabled Content (Top Pane):** Lists content active for this profile.
                    3.  **Ordering:** Content is applied Top-to-Bottom. Higher items overwrite lower items.

                    **Importing:**
                    Use **"Add Local"** to register external folders without copying.
                    """,
                },
                new InfoCard
                {
                    Title = "Virtual File System",
                    Content = "How content is merged at runtime.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Layered Execution:**
                    When you launch the game, GenHub creates a 'Union' of all enabled content.

                    1.  **Bottom Layer:** Game Client files.
                    2.  **Middle Layer:** Mod files (overwriting client).
                    3.  **Top Layer:** User maps and patches (highest priority).
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
            Description = "Create direct-launch shortcuts.",
            Order = 3,
            Cards =
            [
                new InfoCard
                {
                    Title = "Headless Mode Launcher",
                    Content = "Architecture for non-GUI game execution.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Direct Game Launch:**
                    Shortcuts let you launch a specific mod or game version straight from your desktop, skipping the GenHub window entirely.
                    1.  **Instant Play:** Double-click the icon, and the game starts in seconds.
                    2.  **Background Magic:** GenHub briefly wakes up in the background to set up your mod, then disappears.
                    3.  **Clean Exit:** When you quit the game, GenHub quietly cleans up the temporary files.
                    """,
                },
                new InfoCard
                {
                    Title = "Shortcut Creation",
                    Content = "Generating linkage files.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Process:**
                    1.  **Right-Click** any Profile Card and select **Create Desktop Shortcut**.
                    2.  **Result:** GenHub creates a standard Windows Shortcut (`.lnk`) on your Desktop.
                    3.  **Behavior:** Double-clicking this shortcut launches GenHub in the background to build your profile, then instantly starts the game.
                    """,
                },
                new InfoCard
                {
                    Title = "Icon Customization",
                    Content = "Visual identification of shortcuts.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Source:**
                    GenHub extracts high-resolution `.ico` resources directly from the game executable (`generals.exe` or `generals.zh.exe`).
                    If a custom icon is set in the Profile Metadata, that image is converted to an ICO container and embedded in the shortcut file.
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
            Description = "Enable Steam Overlay and Time Tracking.",
            Order = 4,
            Cards =
            [
                new InfoCard
                {
                    Title = "AppID Injection",
                    Content = "Environment variable spoofing for Steam.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Steam Connection:**
                    GenHub bridges the gap between your retail/CD/Digital copy and Steam.
                    *   **Overlay:** Chat with friends and take screenshots while playing mods.
                    *   **Status:** Show your friends you are playing *"Command & Conquer: Generals"*.
                    *   **Time Tracking:** Log your hours on your official Steam profile.
                    """,
                },
                new InfoCard
                {
                    Title = "Usage Requirements",
                    Content = "Prerequisites for successful injection.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Prerequisites:**
                    For the injection hook to succeed:
                    1.  **Process:** `Steam.exe` MUST be running in the background before launch.
                    2.  **Entitlement:** The logged-in Steam account MUST own a valid license for *Command & Conquer: The Ultimate Collection*.

                    *Note: Returns to "Non-Steam" mode gracefully if Steam is not detected.*
                    """,
                },
                new InfoCard
                {
                    Title = "Time Tracking",
                    Content = "Steam playtime logging.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Mechanism:**
                    Because Steam detects the AppID, it logs playtime as if you were running the official version.
                    This allows you to track hours even when playing Mods or Total Conversions.
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
            Description = "Import external Mods, Maps, and Tools.",
            Order = 5,
            Cards =
            [
                new InfoCard
                {
                    Title = "Universal Import",
                    Content = "Import Zips, Folders, and Executables.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **The 'Add Local' Gateway:**
                    GenHub is designed to be your central command center. Use the **Add Local** button to import content from anywhere on your PC.

                    **Supported Imports:**
                    *   **ZIP Archives:** Drag & Drop a Mod or Map Pack ZIP. GenHub extracts, organizes, and installs it automatically.
                    *   **Folders:** Point to an existing mod folder to import it without copying (if it's already extracted).
                    *   **Executables:** Add standalone tools, trainers, or specific game versions.
                    """,
                },
                new InfoCard
                {
                    Title = "Endless Possibilities",
                    Content = "Map Packs, Total Conversions, and Utilities.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **What can you add?**
                    *   **Map Packs:** Download a massive map pack (e.g., "6000 Maps.zip")? Import it, and GenHub will validate and list *every single map* individually.
                    *   **Total Conversions:** Install massive mods like *Rise of the Reds* or *ShockWave* by simply importing their folder or installer.
                    *   **Legacy Tools:** Keep your favorite classic modding tools reachable from the same dashboard.
                    """,
                },
                new InfoCard
                {
                    Title = "Smart Management",
                    Content = "Auto-validation and safe storage.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Intelligent Processing:**
                    GenHub doesn't just blindly copy files.
                    1.  **Validation:** It checks for valid `.map` files, Game Data `.big` files, and Executables.
                    2.  **Safety:** Imported content is stored in a way that prevents it from overwriting or corrupting your base game.
                    3.  **Mix & Match:** Once imported, you can enable a Map Pack *and* a Mod on the same profile instantly.
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
            Description = "Replay and Map management.",
            Order = 6,
            Cards =
            [
                new InfoCard
                {
                    Title = "Replay Manager: Import & Parse",
                    Content = "Importing game recordings.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Import Methods:**
                    *   **Quick Import (URL):** Paste a Match ID (e.g., `151553`), a GenTool URL, or a direct download link into the text box and click **Download**.
                    *   **Browse (Paperclip):** Select `.rep` files or `.zip` archives from your computer.
                    *   **Drag & Drop:** Simply drag files directly onto the Replay list.

                    **Parsing:**
                    *   GenHub reads the binary header of replay files to show you the Map, Players, and Game Version without launching the game.
                    *   *Note: Detailed match statistics parsing is coming soon.*
                    """,
                },
                new InfoCard
                {
                    Title = "Replay Manager: Cloud & Sharing",
                    Content = "Upload and share replays.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Cloud Upload (Cloud Icon):**
                    *   Select replays and click **Upload** to send them to *UploadThing* cloud storage.
                    *   **Limits:** Max 10MB per upload. Files are retained for **14 days**.
                    *   **Share:** A download link is automatically copied to your clipboard.

                    **Upload History (Down Arrow):**
                    *   View your recently uploaded files.
                    *   **Status:** "Active" (available for download) or "Expired" (deleted from cloud).
                    *   **Actions:** Copy links again or remove items from your local history list.
                    """,
                },
                new InfoCard
                {
                    Title = "Replay Manager: Archiving",
                    Content = "Zip and Unzip functionality.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Packaging (Zip Icon):**
                    *   Select multiple replays and click **Zip** to create a compressed archive in your Replay folder.
                    *   Useful for backing up tournaments or sharing bundles manually.

                    **Extraction (Uncompress):**
                    *   Select a `.zip` file in the list and click **Uncompress**.
                    *   GenHub extracts all valid `.rep` files directly into your Replay folder.
                    """,
                },
                new InfoCard
                {
                    Title = "Map Manager: Library",
                    Content = "Organizing custom maps.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Management:**
                    *   **Search:** Filter maps instantly by name or folder using the search bar.
                    *   **Thumbnails:** GenHub automatically generates previews from the map's `.tga` file (if available).
                    *   **Import:** Supports dragging & dropping entire map folders or `.zip` archives.

                    **Context Actions:**
                    *   **Delete (Trash):** Permanently removes the map from your disk.
                    *   **Open Folder:** Opens the specific map folder in Windows Explorer.
                    """,
                },
                new InfoCard
                {
                    Title = "Map Manager: Map Packs",
                    Content = "Creating map collections.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **What is a Map Pack?**
                    A Map Pack is a logical grouping of maps (e.g., "Standard Tournament Set" or "4-Player FFA Maps").

                    **How to Create:**
                    1.  Select multiple maps using `Ctrl+Click` or `Shift+Click`.
                    2.  Click the **"Pack"** button (top right).
                    3.  Enter a name for your collection under "Create New" and click **Create MapPack**.

                    **Usage:**
                    You can quickly see which maps belong to a pack and manage them as a group.
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
            Description = "Detect or register game installations.",
            Order = 7,
            Cards =
            [
                new InfoCard
                {
                    Title = "Auto-Detection",
                    Content = "Heuristic scanning for installed games.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Heuristic Scanner:**
                    GenHub searches for valid `generals.exe` binaries by querying:
                    1.  **Windows Registry:**
                        *   `HKLM\SOFTWARE\WOW6432Node\Electronic Arts\EA Games\Generals`
                        *   `HKLM\SOFTWARE\WOW6432Node\EA Games\Command and Conquer Generals Zero Hour`
                    2.  **Library Paths:** `C:\Program Files\EA Games`, `SteamLibrary\steamapps\common`.
                    """,
                },

                new InfoCard
                {
                    Title = "Signature Verification",
                    Content = "Anti-piracy and integrity checks.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **SHA-256 Hashing:**
                    GenHub validates game integrity by computing the SHA-256 checksum of `generals.exe` and `game.dat`.
                    *   **Known Good:** Matches against an internal database of No-CD patches, v1.04 officials, and The First Decade binaries.
                    *   **Unknown:** Unknown hashes are flagged as "Unverified" but still usable.
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
            Description = "Technical details of NTFS Hardlink isolation.",
            Order = 8,
            Cards =
            [
                new InfoCard
                {
                    Title = "The Magic Mirror",
                    Content = "Understanding the localized file system.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **The "Magic Mirror":**
                    When you hit Play, GenHub creates a "Virtual Copy" of your game installation instantly.

                    **Why is this cool?**
                    1.  **Zero Space:** It looks like a full 5GB game, but it takes up 0MB of disk space on your drive.
                    2.  **Safety:** Any changes made by mods happen in this "Mirror". If a mod breaks the game, your actual installation is perfectly safe.
                    """,
                },
                new InfoCard
                {
                    Title = "Troubleshooting",
                    Content = "Resolving common build errors.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Common Issues:**
                    -   **"Access Denied":** GenHub requires Write permissions to `AppData`. Run as Admin if issues persist.
                    -   **"File In Use":** Ensure the game process is fully terminated before rebuilding.
                    """,
                },
                new InfoCard
                {
                    Title = "Performance Specs",
                    Content = "Efficiency and integrity metrics.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Hardlinks:**
                    -   **Speed:** < 50ms creation time (Metadata only).
                    -   **Space:** 0 bytes additional disk usage (Pointers).
                    -   **Integrity:** Read-only source files. Modifications in workspace do not corrupt the installation.
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
            Description = "Update mechanism.",
            Order = 9,
            Cards =
            [
                new InfoCard
                {
                    Title = "Version Control",
                    Content = "GitHub Releases integration.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Source:**
                    Updates are fetched directly from the public GitHub repository.

                    **Verification:**
                    Release tags are compared against local assembly versions.
                    """,
                },
                new InfoCard
                {
                    Title = "Update Workflow",
                    Content = "Applying patches.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Update Process:**
                    1.  **Notification:** A bar appears at the bottom when an update is found (checked every 4 hours).
                    2.  **Background Download:** Updates download incrementally to save bandwidth while you play.
                    3.  **Instant Apply:** Clicking "Restart" applies the update in ~5 seconds and restores your session.
                    """,
                },
                new InfoCard
                {
                    Title = "Rollback Capability",
                    Content = "Reverting to previous versions.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Manual Rollback:**
                    GenHub does not support automatic rollbacks.
                    To revert, download an older release `.zip` from GitHub and overwrite the installation folder manually.
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
                    Content = "Generals Online is not just another GameSpy emulator - it's a complete reimagining of multiplayer services for Command & Conquer: Generals - Zero Hour.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Built upon the source code released by Electronic Arts, this community-driven project revitalizes and modernizes the game's multiplayer experience, improving stability, client functionality, and overall service reliability - all while preserving the original gameplay you know and love.",
                },
                new InfoCard
                {
                    Title = "Do I need a clean install of Zero Hour?",
                    Content = "No. GeneralsOnline can be installed onto your current Generals installation.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = "The installer handles everything for you. You do not need to delete your existing game data.",
                },
                new InfoCard
                {
                    Title = "Can I play GeneralsOnline if I have GenTool/GenPatcher installed?",
                    Content = "Yes.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "GeneralsOnline is designed to be compatible with GenTool and GenPatcher. It lives in its own subspace.",
                },
                new InfoCard
                {
                    Title = "Can I play GeneralsOnline if I have custom UI / control bars installed?",
                    Content = "Yes.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Custom UI asests like control bars are fully supported and will work just like they do in standard Zero Hour.",
                },
                new InfoCard
                {
                    Title = "Does GeneralsOnline modify my game installation?",
                    Content = "No. GeneralsOnline is standalone and does not modify your installation.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "You can continue to run the 'standard' Generals game alongside GeneralsOnline.",
                },
                new InfoCard
                {
                    Title = "Are custom maps & map transfers supported?",
                    Content = "Yes.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = "GeneralsOnline supports high-speed map transfers in-lobby, so you can play your favorite custom maps with others effortlessly.",
                },
                new InfoCard
                {
                    Title = "How do I run Generals Online?",
                    Content = "Use the desktop shortcut or run GeneralsOnlineZH.exe",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = "The launcher provides a streamlined way to start the game, Manage your profile, and join the lobby.",
                },
                new InfoCard
                {
                    Title = "Does GeneralsOnline work with cracked games?",
                    Content = "GeneralsOnline is only tested and developed against the Steam and EA Origin/Play versions of the game.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    We do not modify the protections which Electronic Arts has applied to the game in any way, shape or form.

                    We recommend buying the game on Steam as this is the best place to play at this time and supports the developers.
                    """,
                },
                new InfoCard
                {
                    Title = "How do I login?",
                    Content = "Generals Online supports 3 login methods. Steam, Discord and GameReplays.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = "Choose the platform you are most comfortable with. Your progress and stats will be linked to that specific account.",
                },
                new InfoCard
                {
                    Title = "Is it safe to login with my Steam/Discord/GameReplays account?",
                    Content = "Yes. We utilize OpenID, which means we never see your credentials.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "We utilize OpenID, which means we never see your credentials - just a unique identifier that identifies your account. You can read more about this technology on Wikipedia.",
                },
                new InfoCard
                {
                    Title = "How do I know if the service is online?",
                    Content = "You can check the service-status channel in our Discord, or on our Status Page.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = "The live status is also reflected in the login screen of the client.",
                },
                new InfoCard
                {
                    Title = "How do I report bugs or give feedback?",
                    Content = "Please visit our Discord!",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = "We have dedicated channels for bug reporting and feedback. Our development team is active and listens to the community.",
                },
                new InfoCard
                {
                    Title = "How do I get updates?",
                    Content = "We release updates regularly. Your game will automatically update itself.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = "We release updates regularly. Your game will automatically update itself when you enter the multiplayer section of the game.",
                },
                new InfoCard
                {
                    Title = "Do I need software like Radmin, Hamachi, GameRanger etc?",
                    Content = "No. Generals Online is standalone and needs no additional software.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "All networking is handled natively by the service, providing a true modern multiplayer experience without third-party wrappers.",
                },
                new InfoCard
                {
                    Title = "Do I need to forward ports and configure my router/network?",
                    Content = "No. Generals Online solves this issue on your behalf.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Our NAT traversal technology handles connectivity automatically, so you can focus on the game.",
                },
                new InfoCard
                {
                    Title = "Is GeneralsOnline secure?",
                    Content = "Yes. We utilize the latest industry standard encryption (AES256-GCM) for network traffic.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = "We utilize the latest industry standard encryption (AES256-GCM) for network traffic. This is more secure than the original C&C Generals game.",
                },
                new InfoCard
                {
                    Title = "I get a Windows Firewall pop-up, what does that mean?",
                    Content = "This is because the application is a 'new application' to the firewall and is attempting network communication.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = "The first time you access the multiplayer menu you may get a Windows firewall pop-up. This is because the application is a 'new application' to the firewall and is attempting network/internet communication. Hitting allow will enable you to proceed.",
                },
                new InfoCard
                {
                    Title = "What are relays?",
                    Content = "Relays allow users who would otherwise be unable to connect to each other to do just that.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Relays allow users who would otherwise be unable to connect to each other to do just that. It is a commonly used mechanism in modern retail games and platforms such as Steam and behaves similar to the Tunnels system utilized on CNCNet for earlier C&C games.",
                },
                new InfoCard
                {
                    Title = "Do relays impact the experience?",
                    Content = "Typically not.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "Typically not. In certain environments, a relayed connection may even be faster than a direct connection due to the premium backbone being used.",
                },
                new InfoCard
                {
                    Title = "How does the game select which relay to use?",
                    Content = "Relays connections are formed dynamically on a player-to-player basis.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    Relays connections are formed dynamically on a player-to-player basis, ensuring each P2P connection utilizes the server location with the lowest latency for that particular pair of players.

                    Users within one lobby/match can utilize different servers in different regions to achieve optimal latency.
                    """,
                },
                new InfoCard
                {
                    Title = "Are relays secure?",
                    Content = "Yes.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = "The relay servers do not have access to the encryption keys that would be required to read the traffic they are relaying.",
                },
                new InfoCard
                {
                    Title = "Can I host a relay?",
                    Content = "We thank you for your interest, however, we do not have a need for community relays at this time.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = "We thank you for your interest, however, we do not have a need for community relays at this time. Generals Online utilizes the CloudFlare backend which is available in 330 cities in 125 countries and has a latency of ~50ms from 95% of the worlds population.",
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
