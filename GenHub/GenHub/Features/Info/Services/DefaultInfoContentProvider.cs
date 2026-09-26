using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Info;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Provides default static help content and FAQs for GenHub.
/// </summary>
public class DefaultInfoContentProvider : IInfoContentProvider
{
    private readonly List<InfoSection> _sections;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInfoContentProvider"/> class.
    /// </summary>
    public DefaultInfoContentProvider()
    {
        _sections =
        [
            CreateQuickStartSection(),
            CreateGameProfilesSection(),
            CreateGameSettingsSection(),
            CreateGameProfileContentSection(),
            CreateShortcutsSection(),
            CreateSteamIntegrationSection(),
            CreateLocalContentSection(),
            CreateToolsSection(),
            CreateScanForGamesSection(),
            CreateWorkspaceSection(),
            CreateAppUpdatesSection(),
            CreateChangelogSection(),
            CreateGeneralsOnlineFAQSection(),
            CreateGeneralsOnlineChangeLogSection(),
        ];
    }

    /// <inheritdoc />
    public Task<IEnumerable<InfoSection>> GetAllSectionsAsync() =>
        Task.FromResult(_sections.OrderBy(s => s.Order).AsEnumerable());

    /// <inheritdoc />
    public Task<InfoSection?> GetSectionAsync(string sectionId) =>
        Task.FromResult(_sections.FirstOrDefault(s => s.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Creates the quickstart guide section.
    /// </summary>
    /// <returns>The quickstart <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateQuickStartSection()
    {
        return new InfoSection
        {
            Id = InfoConstants.SectionQuickstart,
            Title = "Quickstart Guide",
            Description = "Learn the basics of setting up GenHub and launching your first game in minutes.",
            Order = -1,
            Cards =
            [
                new InfoCard
                {
                    Id = InfoConstants.CardQuickstartWelcome,
                    Title = "Welcome to GenHub",
                    Content = "The modern profile manager and launcher for Command & Conquer: Generals and Zero Hour.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Welcome to GenHub.**
                    GenHub simplifies playing, modding, and managing Command & Conquer: Generals and Zero Hour.

                    **Core Concepts:**
                    * **Isolated Profiles:** Each game profile is completely separated from your base game installation.
                    * **Zero-Copy Workspaces:** Filesystem linking allows mods to run without duplicating gigabytes of files on your drive.
                    * **Central Content Library:** Downloaded mods, custom maps, and community patches are stored once and shared across any number of profiles.
                    * **Community Integration:** Native support for Generals Online, TheSuperHackers modern engine, and official Steam releases.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardQuickstartStep1Scan,
                    Title = "Step 1: Scan for Games",
                    Content = "Locate and connect your existing game installations.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Detecting Your Game:**
                    GenHub connects to your existing game files before launching profiles.

                    1. Navigate to the **Game Profiles** tab.
                    2. Click the **SCAN** button in the toolbar.
                    3. GenHub automatically searches standard Steam, EA App, Origin, and CD install directories.

                    Once detected, you can create and launch profiles based on this installation.
                    """,
                    Actions =
                    [
                        new InfoAction
                        {
                            Label = "Go to Detection Guide",
                            ActionId = InfoConstants.ActionNavScanGames,
                            IconKey = InfoConstants.IconMagnify,
                            IsPrimary = true,
                        },
                    ],
                },
                new InfoCard
                {
                    Id = InfoConstants.CardQuickstartStep2Downloads,
                    Title = "Step 2: Essential Downloads",
                    Content = "Recommended community updates for modern systems.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Recommended Community Additions:**
                    Visit the **Downloads** tab to get recommended updates for modern hardware and online play:

                    * **Generals Online:** Modern online multiplayer lobby and matchmaking, replacing the discontinued GameSpy service.
                    * **TheSuperHackers Engine:** Active community engine updates offering widescreen support, high-DPI scaling, and crash fixes.
                    * **Community Patches:** Game balance, memory enhancements, and stability fixes.
                    """,
                    Actions =
                    [
                        new InfoAction
                        {
                            Label = "Go to Downloads",
                            ActionId = InfoConstants.ActionNavDownloads,
                            IconKey = InfoConstants.IconCloudDownload,
                            IsPrimary = true,
                        },
                        new InfoAction
                        {
                            Label = "Learn about Content",
                            ActionId = InfoConstants.ActionNavGameProfileContent,
                            IconKey = InfoConstants.IconBookOpenVariant,
                        },
                    ],
                },
                new InfoCard
                {
                    Id = InfoConstants.CardQuickstartStep3LocalContent,
                    Title = "Step 3: Add Local Content",
                    Content = "Import your existing mods and maps.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Adding Your Own Content:**
                    Already have mods or maps on your PC? Import them directly into GenHub:

                    1. In any profile editor, switch to the **Content** tab.
                    2. Click **Add Local** in the bottom-right corner.
                    3. Choose a folder, ZIP archive, or executable to register.
                    4. Check the box next to your imported item to enable it for that profile.
                    """,
                    Actions =
                    [
                        new InfoAction
                        {
                            Label = "Local Content Guide",
                            ActionId = InfoConstants.ActionNavLocalContent,
                            IconKey = InfoConstants.IconFolderUpload,
                            IsPrimary = true,
                        },
                    ],
                },
                new InfoCard
                {
                    Id = InfoConstants.CardQuickstartStep4Play,
                    Title = "Step 4: Launch and Play",
                    Content = "Start your game with isolated profiles.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Launching Profiles:**
                    Once your profile is configured with your preferred mods and resolution:

                    1. Go to the **Game Profiles** tab.
                    2. Click the **Play** button on any profile card.
                    3. GenHub prepares an isolated workspace in milliseconds and launches the game.

                    Your base game files remain clean and completely untouched.
                    """,
                },
            ],
        };
    }

    /// <summary>
    /// Creates the game profiles section.
    /// </summary>
    /// <returns>The game profiles <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateGameProfilesSection()
    {
        (string Id, string Title, string Content, InfoCardType Type, string Detailed)[] cardData =
        [
            (InfoConstants.CardProfilesDemo,
             "Interactive Demo: Profile Card",
             "Live interactive profile card simulation.",
             InfoCardType.Feature,
             """
             **Live Interactive Profile Card:**
             This interactive card simulates an active game profile in GenHub.
             * **Play:** Launches the game using the profile's dedicated workspace and configuration.
             * **Edit (Pencil):** Opens the profile editor to manage active mods, maps, and engine settings.
             * **Duplicate:** Clones the profile with all settings preserved.
             * **Shortcuts:** Generates a desktop shortcut with custom icon and command arguments.
             """),
            (InfoConstants.CardProfilesSandbox,
             "Your Personal Sandbox",
             "Keep your mods, maps, and game settings isolated and safe.",
             InfoCardType.Concept,
             """
             **Isolated Game Profiles:**
             A profile is an independent configuration for your game. Instead of reinstalling or swapping files manually:

             1. **Safety:** Mod files never overwrite your original game installation. If a mod causes problems, your base game remains completely untouched.
             2. **Multiple Configurations:** Keep separate profiles for vanilla Zero Hour, Rise of the Reds, ShockWave, or custom balance patches, and switch between them instantly.
             3. **Speed:** Workspaces build in milliseconds using file linking, requiring almost zero extra storage on your drive.
             """),
            (InfoConstants.CardProfilesControls,
             "Controls & Window Persistence",
             "Profile card buttons, manifest scrubbing, and window sizing.",
             InfoCardType.HowTo,
             """
             **Profile Card Controls:**
             1. **Play:** Launches the game with this profile's active mods, settings, and workspace.
             2. **Edit Profile (Pencil):** Opens the profile editor to select mods, maps, and adjust game settings.
             3. **Copy Profile (Duplicate):** Clones the profile, including all settings and enabled content, into a new profile.
             4. **Desktop Shortcut:** Creates a desktop shortcut to launch this profile directly.
             5. **Delete Profile:** Removes the profile and its dedicated workspace configuration.

             **Manifest Scrubbing:**
             When downloaded packages or local manifests are deleted from disk, GenHub automatically cleans up orphaned references across all profiles, preventing broken launch states.

             **Window & Layout Persistence:**
             The profile editor remembers window dimensions, sidebar widths, and maximized states across app restarts.
             """),
            (InfoConstants.CardProfilesAdvancedOptions,
             "Advanced Profile Options & Camera Settings",
             "Custom launch arguments, camera height/speed limits, and EA intro movie skipping.",
             InfoCardType.Feature,
             """
             **Launch Arguments:**
             GenHub passes custom command-line arguments directly to the game. For example, use `-quickstart` to skip introduction videos, or `-win` to force windowed mode.

             **Camera & Visual Tuning:**
             Configure camera height limits, scroll speed ratios, and EA logo movie skipping directly from the profile settings interface without hand-editing INI files.

             **Troubleshooting Logs:**
             Profile startup and launch logs are recorded in the GenHub AppData directory to help diagnose issues if a game closes unexpectedly.
             """),
        ];

        return new InfoSection
        {
            Id = InfoConstants.SectionGameProfiles,
            Title = "Game Profiles",
            Description = "Create and manage isolated game configurations.",
            Order = 0,
            Cards = cardData.Select(c => CreateCard(c.Id, c.Title, c.Content, c.Type, c.Detailed)).ToList(),
        };
    }

    /// <summary>
    /// Creates the game settings section.
    /// </summary>
    /// <returns>The game settings <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateGameSettingsSection()
    {
        return new InfoSection
        {
            Id = InfoConstants.SectionGameSettings,
            Title = "Game Settings",
            Description = "Configure display, audio, and engine settings per profile.",
            Order = 1,
            Cards =
            [
                new InfoCard
                {
                    Id = InfoConstants.CardSettingsDemo,
                    Title = "Interactive Demo: Settings Window",
                    Content = "Live interactive game settings window with category navigation and live controls.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Interactive Game Settings Mockup:**
                    Test and explore all graphics, audio, control, and engine settings directly inside this live interactive window.
                    * Select any category in the left sidebar to jump directly to its options.
                    * Adjust resolution, windowed mode, sound channels, or camera heights.
                    * Toggle community engine extensions from TheSuperHackers and Generals Online.
                    * Click the top tabs (Content, Profile Settings, Game Settings) to navigate between guides.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardSettingsVideo,
                    Title = "Video & Display Architecture",
                    Content = "Display resolution, windowed mode, graphics quality, LOD, and shadow rendering.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Resolution & Window Modes:**
                    * **Resolution (Width x Height):** Configures display dimensions (Options.ini `Resolution = X Y`). GenHub fully supports modern widescreen, 1440p, 4K, and Ultrawide displays without stretching the HUD.
                    * **Windowed Mode:** Runs the game in a borderless or standard window (`Windowed = yes` or `-win`). Prevents game crashes and freezes when Alt-Tabbing across multiple monitors.
                    * **Gamma & Brightness:** Direct3D gamma ramp modifier (`Gamma = 50`) controlling midtone illumination across maps.

                    **Graphics Quality & Performance:**
                    * **Anti-Aliasing (MSAA):** Full-screen multi-sample anti-aliasing (`AntiAliasing = 0-4`) smoothing geometry silhouettes on 3D vehicles and terrain.
                    * **Dynamic LOD / Static LOD:** Level-of-Detail geometry mesh decimation. At high zoom, lower LOD models preserve high framerates during intensive battles.
                    * **Max Particle Count:** Limits simultaneous smoke, fire, shockwave, and shrapnel particles (`MaxParticleCount = 5000`) to eliminate framerate drops during superweapon strikes.
                    * **Texture Quality:** Low, Medium, or High texture resolution mipmap clamping.

                    **Shadows & Advanced Visual Effects:**
                    * **3D Volumetric Shadows:** High-fidelity real-time shadow projection from vehicles and structures onto uneven terrain.
                    * **2D Texture Decal Shadows:** Lightweight projected shadow textures under units; recommended for large 8-player matches.
                    * **Cloud & Light Maps:** Dynamic cloud shadows sweeping across terrain and pre-baked per-vertex lightmaps.
                    * **Water Edge Smoothing:** Pixel shader alpha-blending at riverbanks and coastlines preventing hard geometric tile clipping.
                    * **Behind Buildings Occlusion:** Translucent X-ray silhouette rendering when units move behind tall structures.
                    * **Heat Effects:** Distortion refraction pixel shaders on jet thrusters, rocket trails, and thermal explosions.
                    * **Trees & Foliage:** Density and draw distance of environmental flora and terrain props.
                    * **Skip Intro Movies:** Command line `-quickstart` switch bypassing the EA splash movies for fast boot times.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardSettingsAudio,
                    Title = "Audio Channels & Sound Physics",
                    Content = "DirectSound mixing channels, 3D spatial attenuation, and volume mixing.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Volume Mixing Channels:**
                    * **Master Volume:** Global DirectSound output attenuation (`Volume = 0-100`).
                    * **Sound Effects Volume:** Weapon discharges, explosions, jet engines, and mechanical movements (`SoundFXVolume = 0-100`).
                    * **Music Volume:** Dynamic interactive orchestral score that changes based on combat state (`MusicVolume = 0-100`).
                    * **Voice & Speech Volume:** Faction EVA battlefield alerts ("Building complete", "Unit lost") and unit confirmation speech (`VoiceVolume = 0-100`).

                    **Spatial Sound & Hardware Mixing:**
                    * **3D Sound & Spatial Falloff:** Calculates sound listener coordinates, simulating realistic distance attenuation, stereo panning, and Doppler frequency shifts (`3DSound = yes`).
                    * **Audio Channels (DirectSound Buffers):** Number of simultaneous audio sample streams (`AudioChannels = 16-128`). Modern audio hardware handles 64 or 128 channels without sound cutting out during massive 8-player artillery barrages.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardSettingsControlsCamera,
                    Title = "Controls, Camera Physics & Network",
                    Content = "Scroll speed, alternate mouse schemes, camera altitude, and packet pacing.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Mouse & Unit Controls:**
                    * **Scroll Speed:** Sensitivity of screen-edge mouse panning (`ScrollSpeed = 50`).
                    * **Alternate Mouse Setup (Right-Click Attack/Move):** Switches from classic 2003 C&C left-click targeting to modern RTS standard right-click move and attack (`AlternateMouseSetup = yes`).
                    * **Double-Click Attack Move:** Double-clicking an enemy unit orders all matching on-screen units to converge and attack.
                    * **Unit Auto-Retaliation:** Configures unit guard stance to automatically retaliate against distant enemy fire without breaking formation.
                    * **Scroll Anchor:** Displays a visual pivot marker on the map when scrolling with the middle mouse button.

                    **Networking & HUD Options:**
                    * **SendDelay (Packet Pacing):** Paces multiplayer lockstep network turns (`SendDelay = yes`), reducing buffer bloat and sync errors over high-latency connections.
                    * **Clock Font Size:** Adjusts on-screen match timer font scale for high-resolution displays.
                    * **Profanity Filter:** Toggles client-side chat profanity masking.

                    **Camera Altitude & Perspective:**
                    * **Camera Base Altitude & Min/Max Zoom:** Controls vertical viewing distance above terrain for tactical overview.
                    * **Camera Pitch Angle:** Adjusts camera tilt angle between steep top-down and cinematic oblique perspectives.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardDownloadsSuperHackers,
                    Title = "TheSuperHackers Engine Extensions",
                    Content = "Replay archiving, economy HUD, observer mode, and cursor capture.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Engine Enhancements & Stability:**
                    TheSuperHackers (TSH) engine is the active community codebase modernizing Command & Conquer: Generals and Zero Hour.

                    **Hardware & Display Integration:**
                    * **Cursor Clip / Capture:** Hardware-locks the mouse cursor to the game viewport during matches in both fullscreen and windowed modes. Prevents accidental clicks onto secondary monitors during fast micro.
                    * **Windowed Screen Edge Scrolling:** Enables smooth camera panning simply by moving the cursor to window edges in windowed mode, eliminating the need to drag with the right mouse button.
                    * **Window Transition Speed Multiplier:** Eliminates artificial menu fade-out delays for instantaneous navigation across lobbies and scoreboards.

                    **In-Game Information Overlays:**
                    * **Money Per Minute (MPM) Economy HUD:** Real-time income tracker displaying resource collection rate, supply truck efficiency, and secondary economy flow.
                    * **Observer / Spectator Mode:** Grants non-playing referees and tournament casters an uninhibited tactical view of all players without fog-of-war constraints.
                    * **HUD Font Size Scalers:** Independent font size configuration for system clock, network ping latency, and render FPS counters.
                    * **Transaction Volume:** Independent volume control for financial sound effects (bounties, cash drops, hacker income).
                    * **Replay Auto-Archiving:** Automatically timestamps and organizes skirmish and multiplayer replays into dedicated dated directories, preventing LastReplay.rep from being overwritten.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardDownloadsGeneralsOnline,
                    Title = "Generals Online Modern Client & Network",
                    Content = "Matchmaking, rank badges, unlocked framerate, and host camera limits.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Generals Online Architecture:**
                    Generals Online (GO) provides modern multiplayer networking, ranked matchmaking, and client extensions replacing discontinued GameSpy services.

                    **Lobby & Account Features:**
                    * **Auto-Login & Encrypted Session Tokens:** Securely restores player sessions without re-entering credentials.
                    * **Player Ranks & Ladder Badges:** Displays competitive MMR rankings, seasonal badges, and disconnect ratings directly in lobby rooms.
                    * **Social Toast & Sound Notifications:** Customizable audio-visual popups when friends join lobbies, send direct messages, or invite you to matches.
                    * **Chat Font & Fade Delays:** Customizes lobby chat font size and duration before messages fade from the HUD.

                    **Graphics & Camera Modernization:**
                    * **Unlocked Framerate Limiter (Up to 500 FPS):** Uncouples graphics render loop from the 30-tick engine physics rate, delivering buttery-smooth 144Hz, 240Hz, or 360Hz refresh rates.
                    * **Stats, Ping & FPS Overlay:** In-engine DirectX HUD displaying frame draw times, ping round-trip latency, and packet jitter.
                    * **Host-Enforced Max Zoom Limits:** Allows game hosts to lock maximum camera zoom height across all connected players to ensure competitive fairness.
                    * **Camera Move Speed Ratio:** Dynamically scales camera panning speed relative to current zoom altitude for natural mouse feel.
                    * **GameSpy IP Override & Diagnostics:** Redirects legacy direct-IP connections to community proxy infrastructure and enables verbose packet logs for netcode diagnostics.
                    """,
                },
            ],
        };
    }

    /// <summary>
    /// Creates the profile content hierarchy and editor section.
    /// </summary>
    /// <returns>The profile content <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateGameProfileContentSection()
    {
        return new InfoSection
        {
            Id = InfoConstants.SectionGameProfileContent,
            Title = "Profile Content",
            Description = "Manage mods, maps, and patches enabled for each profile.",
            Order = 2,
            Cards =
            [
                new InfoCard
                {
                    Id = InfoConstants.CardContentDemo,
                    Title = "Interactive Demo: Content Editor",
                    Content = "Live interactive content manager showing mod selection and priority ordering.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Live Content Manager Demo:**
                    Explore how mods, maps, game clients, and tools are structured and ordered inside a profile.
                    * Browse active mods and toggle addons on or off.
                    * Reorder items to establish load priority for overriding INI files or Big archives.
                    * Switch between Content, Profile Settings, and Game Settings using the tabs above.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardProfilesHierarchy,
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
                    Supplementary visual or audio packs (such as remastered music, custom hotkeys, or HD textures) that sit on top of mods safely.

                    **Tool:**
                    External utilities (such as World Builder, Hotkey Editor, or FinalBIG) that can be opened directly from your profile dashboard.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardProfilesCloning,
                    Title = "Cloning Content",
                    Content = "How copying profiles preserves your content setup.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **How Content Duplication Works:**
                    When you use **Copy Profile**, GenHub duplicates your profile's content configuration:

                    * **Preserved Content:** All active mods, maps, and patches are mirrored into the new profile.
                    * **Independent Editing:** The clone is fully independent. Adding or removing content in the clone will not change the original profile.
                    * **Zero Storage Waste:** File linking ensures that cloning a profile does not copy large mod files on your disk. Both profiles reference the shared storage cache.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardProfilesEditor,
                    Title = "Content Editor",
                    Content = "Adding and ordering content in a profile.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Content Workflow:**
                    1. **Available Content (Bottom):** Shows installed mods, maps, and patches you can add to this profile.
                    2. **Enabled Content (Top):** Shows content currently active for this profile.
                    3. **Load Priority:** Content is applied from top to bottom. Items higher in the list take priority if two packages contain conflicting files.
                    4. **Add Local:** Link external folders or archives without copying them into GenHub.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardProfilesVfs,
                    Title = "Virtual File System",
                    Content = "How files are merged when launching.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Layered File Merging:**
                    When you click Play, GenHub combines all active content into a unified workspace:

                    1. **Base Layer:** Game client files form the foundation.
                    2. **Mod Layer:** Mod files overlay and replace base game assets.
                    3. **Top Layer:** Custom maps, addons, and patches apply with highest priority.
                    """,
                },
            ],
        };
    }

    /// <summary>
    /// Creates the desktop shortcuts section.
    /// </summary>
    /// <returns>The desktop shortcuts <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateShortcutsSection()
    {
        (string Id, string Title, string Content, InfoCardType Type, string Detailed)[] cardData =
        [
            (InfoConstants.CardShortcutsDemo,
             "Interactive Demo: Desktop Shortcuts",
             "Interactive flow showing profile shortcut generation and command line targets.",
             InfoCardType.Feature,
             """
             **Interactive Desktop Shortcut Simulation:**
             Inspect how GenHub connects desktop shortcut (.lnk) files directly to isolated profile workspaces.
             * Double-clicking launches the profile in headless mode without keeping the main UI open.
             * Custom high-DPI icon artwork is extracted and embedded automatically.
             """),
            (InfoConstants.CardShortcutsHeadless,
             "Headless Mode Launcher",
             "Launch profiles directly from your desktop.",
             InfoCardType.Concept,
             """
             **Direct Desktop Launching:**
             Shortcuts allow you to start any mod configuration straight from your desktop without keeping the main launcher window open:

             1. **Direct Launch:** Double-click the shortcut to start the game immediately.
             2. **Silent Setup:** GenHub runs briefly in the background to prepare the profile workspace, then hands off to the game.
             3. **Clean Exit:** Workspace temporary files are automatically cleaned up when the game closes.
             """),
            (InfoConstants.CardShortcutsCreation,
             "Shortcut Creation",
             "How to add a profile shortcut to your desktop.",
             InfoCardType.HowTo,
             """
             **Creating a Shortcut:**
             1. In **Game Profiles**, right-click any profile card (or click the Desktop shortcut icon).
             2. Select **Create Desktop Shortcut**.
             3. A standard Windows shortcut (`.lnk`) appears on your desktop.
             4. Double-clicking this shortcut launches that specific profile configuration immediately.
             """),
            (InfoConstants.CardShortcutsIcons,
             "Icon Customization",
             "Visual icons for your desktop shortcuts.",
             InfoCardType.Feature,
             """
             **Shortcut Icons:**
             GenHub extracts official high-resolution icon resources from the game executable (`generals.exe` or `game.dat`).
             If your profile uses custom metadata or mod artwork, GenHub converts that image into an icon embedded directly in the shortcut.
             """),
        ];

        return new InfoSection
        {
            Id = InfoConstants.SectionShortcuts,
            Title = "Desktop Shortcuts",
            Description = "Create one-click desktop shortcuts for your profiles.",
            Order = 3,
            Cards = cardData.Select(c => CreateCard(c.Id, c.Title, c.Content, c.Type, c.Detailed)).ToList(),
        };
    }

    /// <summary>
    /// Creates the Steam integration section.
    /// </summary>
    /// <returns>The Steam integration <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateSteamIntegrationSection()
    {
        (string Id, string Title, string Content, InfoCardType Type, string Detailed)[] cardData =
        [
            (InfoConstants.CardSteamDemo,
             "Interactive Demo: Steam Integration",
             "Live preview of Steam overlay hook and launcher integration.",
             InfoCardType.Feature,
             """
             **Live Steam Integration Simulation:**
             Preview how GenHub injects Steam AppID parameters to enable the official Steam Overlay, friend status, and library playtime tracking for any mod profile.
             """),
            (InfoConstants.CardSteamAppId,
             "AppID Injection",
             "Use Steam playtime tracking and the overlay with any mod.",
             InfoCardType.Concept,
             """
             **Steam Integration:**
             GenHub connects your mod launches with Steam so you can take advantage of Steam community features:

             * **Steam Overlay:** Chat with friends, join invites, and take screenshots in-game.
             * **Friend Status:** Displays Command & Conquer: Generals as your current game.
             * **Playtime Tracking:** Hours played with mods count toward your official Steam library stats.
             """),
            (InfoConstants.CardSteamRequirements,
             "Usage Requirements",
             "Requirements for Steam integration.",
             InfoCardType.HowTo,
             """
             **Prerequisites:**
             To use Steam features:
             1. The **Steam desktop application** must be running before launching the game.
             2. The active Steam account must own *Command & Conquer: The Ultimate Collection*.

             *Note: If Steam is not running, GenHub will launch the profile in standard mode without interruption.*
             """),
            (InfoConstants.CardSteamTimeTracking,
             "Time Tracking",
             "Steam playtime logging across mod profiles.",
             InfoCardType.Feature,
             """
             **Playtime Tracking:**
             Because Steam recognizes the game through GenHub's launcher, all playtime across your various mods, map packs, and profiles is logged to your Steam library.
             """),
        ];

        return new InfoSection
        {
            Id = InfoConstants.SectionSteam,
            Title = "Steam Integration",
            Description = "Track playtime and use the Steam Overlay with mods.",
            Order = 4,
            Cards = cardData.Select(c => CreateCard(c.Id, c.Title, c.Content, c.Type, c.Detailed)).ToList(),
        };
    }

    /// <summary>
    /// Creates the local content import section.
    /// </summary>
    /// <returns>The local content <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateLocalContentSection()
    {
        return new InfoSection
        {
            Id = InfoConstants.SectionLocalContent,
            Title = "Local Content",
            Description = "Import external mods, custom engine builds, modding tools, and maps.",
            Order = 5,
            Cards =
            [
                new InfoCard
                {
                    Id = InfoConstants.CardLocalContentDemo,
                    Title = "Interactive Demo: Add Local Content",
                    Content = "Interactive dialog mockup with pre-configured mod, client, tool, and executable presets.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Interactive Local Content Importer:**
                    Test importing folders, archives, and executables into GenHub without touching your disk files.
                    * Use the preset buttons (Mod, Client, Tool, Executable) to test automatic metadata recognition.
                    * Inspect how GenLauncher scrambled .gib files are automatically normalized into clean .big archives.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardContentImporting,
                    Title = "Importing local content into your library",
                    Content = "Add folders, ZIP archives, and executables as reusable content items.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **The Add Local workflow**
                    Use the **Add Local** button in the profile Content tab or library view to register external files into GenHub:

                    * **Folders:** Select an unpacked mod directory or community tool folder on your drive.
                    * **ZIP archives:** Select or drop an archive. GenHub extracts files into an isolated staging area for inspection.
                    * **Executables:** Choose a standalone `.exe` such as WorldBuilder or an engine binary.

                    **Central content storage**
                    When you confirm an import, GenHub registers the item into your local content pool and creates an immutable manifest. The content item is stored once on disk and can be attached to any number of game profiles without copying or duplicating files.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardContentExecutableSelection,
                    Title = "Content types and executable selection",
                    Content = "Configure mods, addons, maps, modding tools, and game clients.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Selecting the correct content type**
                    The content type determines how GenHub mounts and runs your files:

                    * **Mods:** Full game modifications containing `.big` archives (such as ShockWave, Rise of the Reds, or Contra), INI overrides, and custom art assets.
                    * **Addons and patches:** Incremental additions such as camera height adjustments, texture packs, or balance patches that layer over base games or mods.
                    * **Maps and map packs:** Loose `.map` files with `.tga` preview images or bundled map archives. GenHub indexes map metadata and makes them available across profiles.
                    * **Modding tools:** Utilities like GenHotkeys, FinalBIG, or BigViewer. For modding tools, the content preview tree displays a **Select** button next to each `.exe`. Clicking **Select** designates the primary executable so GenHub can launch the tool directly from the profile tool tray.
                    * **Game clients and executables:** Custom game binaries, such as community test builds from TheSuperHackers, or standalone editors like WorldBuilder. Marking the main executable tells GenHub which binary starts the game client or editor.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardContentGenLauncherNormalization,
                    Title = "GenLauncher file normalization",
                    Content = "Detect and repair scrambled .gib archives and suffix-renamed files.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Why normalization is necessary**
                    GenLauncher modifies files directly inside the game directory when activating and deactivating mods. It renames active `.big` files to `.gib` to scramble them, appends `.GLR` (replaced files), `.GOF` (original file backups), and `.GLTC` (temporary copies) suffixes, and creates stray symbolic links. Importing a directory left in this state prevents the game engine from reading mod archives.

                    **Automated normalization in GenHub**
                    When you select a folder or archive containing GenLauncher files, GenHub's normalization service identifies these artifacts automatically during staging:

                    1. Renames all scrambled `.gib` archives back to standard `.big` files so the game engine can mount them.
                    2. Strips `.GLR`, `.GOF`, and `.GLTC` suffixes to restore standard file names.
                    3. Cleans up broken or invalid symbolic links left by previous installations.

                    Normalization runs safely in the staging area before registration, ensuring your imported content item contains clean standard assets.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardWorkspacesIsolation,
                    Title = "Profile linking and workspace isolation",
                    Content = "Link local items to game profiles without modifying base game files.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Linking content to game profiles**
                    After registering a local content item, open any profile in **Profile Settings** and navigate to the **Content** tab:

                    * Enable the checkbox next to any mod, addon, tool, or map pack to attach it to that profile.
                    * Reorder items in the list to configure load priority when multiple items override the same INI settings or art assets.
                    * Assign custom game clients (such as a test build from TheSuperHackers) in the Client selector.

                    **Workspace isolation**
                    GenHub never writes modded files into your original Command & Conquer installation directory. When launching a profile, GenHub creates an isolated workspace using symbolic links or hardlinks to combine your base game with the specific content items assigned to that profile. Your base game files remain untouched, and profiles run independently without file conflicts.
                    """,
                },
            ],
        };
    }

    /// <summary>
    /// Creates the tools and utilities section.
    /// </summary>
    /// <returns>The tools <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateToolsSection()
    {
        (string Id, string Title, string Content, InfoCardType Type, string Detailed, IReadOnlyList<InfoAction>? Actions)[] cardData =
        [
            (InfoConstants.CardToolsDemo,
             "Interactive Demo: Tools Suite",
             "Interactive Replay Manager, Map Manager, and Publisher Studio showcase.",
             InfoCardType.Feature,
             """
             **Interactive Tools Suite:**
             Explore the built-in Replay Manager, Map Manager, and Publisher Studio directly below.
             * Test replay inspection, client CRC mapping, and checkpoint takeover.
             * Preview map thumbnails and create custom map packs.
             * Learn how Publisher Studio distributes content via 3-tier manifests.
             """,
             null),
            (InfoConstants.CardToolsReplayImport,
             "Replay Manager: Import & Header Inspection",
             "Import replays from disk, web links, or match IDs and inspect header metadata.",
             InfoCardType.Concept,
             """
             **Importing Replay Files.**
             * **Link Import:** Paste a replay link or ladder match URL into the import bar and click **Import**.
             * **Browse Files:** Click the paperclip button to select .rep files or zipped replay archives from disk.
             * **Drag and Drop:** Drag .rep or .zip files directly onto the Replay Manager window to register them immediately.
             * **Replay Directories:** Zero Hour replays reside in Documents\Command and Conquer Generals Zero Hour Data\Replays. Generals replays reside in Documents\Command and Conquer Generals Data\Replays.

             **Header Inspection.**
             GenHub parses the binary header before you launch playback. It reads map name, game version, match duration, player slots, factions, and colors.
             """,
             null),
            (InfoConstants.CardToolsReplayGameClientMapping,
             "Replay Manager: Client Matching & Profiles",
             "Match embedded replay CRC checksums to game clients and launch compatible profiles.",
             InfoCardType.Feature,
             """
             Zero Hour replays record game engine and INI file checksums in their header. Running a replay on a different version causes desync errors.

             **Embedded CRC Catalog.**
             GenHub compares the replay's Exe CRC and INI CRC against a preloaded catalog of 140+ game clients. Target versions include:
             * Official Steam Zero Hour 1.04 (Exe CRC 0x401D89EA, INI CRC 0x76B251A3).
             * Official Retail Zero Hour 1.04 (Exe CRC 0xDA2B4B18, INI CRC 0x76B251A3).
             * Official Steam Generals 1.09 (Exe CRC 0x1C96366F, INI CRC 0x5A8E12F0).
             * TheSuperHackers weekly test builds (such as Exe CRC 0x27533BB0 and 0x3FAD8A07).
             * Custom or unmapped replays display their raw Exe and INI CRCs directly in the list.

             **Launch and Profile Actions.**
             * **Launch:** When an existing game profile matches the replay version, click **Launch**. Open the button flyout to choose **Select Profile to Run...** or pick a specific client.
             * **Setup:** When a required client binary or data patch is missing, click **Setup**. GenHub downloads the client package and configures a compatible profile automatically.
             * **Profile:** When you have the game client installed but need a new profile, click **Profile** to create one.
             """,
             null),
            (InfoConstants.CardToolsReplayCheckpointsTakeover,
             "Replay Manager: Checkpoints & Army Takeover",
             "Generate save states at exact timestamps and take control of player armies mid-game.",
             InfoCardType.Feature,
             """
             When playing replays on clients that support recovery hooks, you can jump to match timestamps or take over player armies.

             **Recovery Dialog.**
             Click **Recovery** in any replay row to open the Replay Recovery & Takeover dialog.

             **Creating a Checkpoint.**
             1. Pick a compatible target profile in the dropdown.
             2. Drag the **Target Point** slider to your chosen timestamp or frame (for example, 05:32 at frame 9960).
             3. Click **Create Checkpoint**. GenHub launches the game engine to fast-forward through the match up to your target frame, creates a snapshot save, and closes the game automatically.

             **Resuming and Taking Over.**
             * **Resume Replay:** Select an existing checkpoint from the list and click **Resume Replay** to watch from that exact frame without waiting through the early game.
             * **Takeover Match:** Pick a player slot from the **Takeover Player** menu and click **Takeover Match**. GenHub loads the save file and gives keyboard and mouse control of that army to you.
             """,
             null),
            (InfoConstants.CardToolsReplayCloud,
             "Replay Manager: Cloud Sharing & History",
             "Upload replays to cloud storage and generate shareable links.",
             InfoCardType.Feature,
             """
             **Cloud Uploads.**
             Select one or more replays and click **Upload** in the action bar. GenHub uploads the files to cloud storage and copies the download URL to your clipboard.

             **Deep Links.**
             Click the share icon in the upload history flyout to copy a genhub:// link. When another user clicks this link, GenHub automatically downloads and selects the replay.

             **Upload History.**
             Click the history arrow next to the Upload button to inspect past uploads, copy links, or delete old entries.
             """,
             null),
            (InfoConstants.CardToolsReplayArchive,
             "Replay Manager: Zip Compression & Extraction",
             "Compress replay groups into ZIP archives or extract them into your replay library.",
             InfoCardType.HowTo,
             """
             **Creating Archives.**
             Select replays in the table using Ctrl+Click or Shift+Click, enter a name in the **Zip Name** box, and click **Zip**. GenHub creates a compressed archive in your replay folder.

             **Extracting Archives.**
             Select a .zip archive in the list and click **Uncompress**. GenHub extracts all contained .rep files directly into your replay directory and refreshes the table.
             """,
             null),
            (InfoConstants.CardToolsMapLibrary,
             "Map Manager: Library & Minimap Previews",
             "Search, install, preview, and organize custom skirmish maps.",
             InfoCardType.Concept,
             """
             **Browsing and Filtering Maps.**
             * **Search:** Filter custom maps by map name or directory folder name.
             * **Minimap Previews:** GenHub displays the overhead minimap thumbnail from the map's sidecar .tga image file (map.tga) when present in the map folder.
             * **Import:** Drag and drop map folders or .zip archives into the window to copy them into your user Maps folder (Documents\Command and Conquer Generals Zero Hour Data\Maps).
             * **Actions:** Click **Delete** to remove selected maps from disk, or click **Open Folder** to reveal the map directory in Windows Explorer.
             """,
             null),
            (InfoConstants.CardToolsMapPacks,
             "Map Manager: Map Packs",
             "Bundle custom maps into reusable packages for profiles and tournaments.",
             InfoCardType.Feature,
             """
             A Map Pack groups multiple maps together, such as a competitive tournament pool or 4-player team maps.

             **Creating a Map Pack.**
             1. Select maps in the list with Ctrl+Click or Shift+Click.
             2. Click **Pack** in the toolbar.
             3. Enter a pack name and confirm.

             Once created, you can enable or disable the entire map pack for any profile in Profile Settings.
             """,
             null),
            (InfoConstants.CardToolsHotkeyEditorRebind,
             "Hotkey Editor: Command Cards",
             "Visual hotkey assignment and conflict detection ported from GenHotkeys.",
             InfoCardType.HowTo,
             """
             **Command Card Layout.**
             Select your game and faction (USA, China, or GLA). The editor presents a 3x4 grid mirroring the in-game command bar, populated with official unit, building, and upgrade cameo graphics.

             **Rebinding Keys.**
             Click any command slot and press a key on your keyboard. For example, click the Dozer build action and press Q, or select the Ranger and assign R. The assigned letter displays directly over the button.

             **Conflict Detection.**
             When two actions on the same building share the same shortcut key, GenHub highlights both slots in red. Assigning distinct keys updates the indicator to green.
             """,
             null),
            (InfoConstants.CardToolsHotkeyEditorAddons,
             "Hotkey Editor: Addon Packaging",
             "Export custom bindings into isolated manifest addons with cameo overlays.",
             InfoCardType.Feature,
             """
             **Addon Packaging.**
             GenHub does not overwrite original game INI files. Instead, clicking **Create Addon** packages your bindings into an isolated content addon (.manifest.json). You can attach this addon to any profile in Profile Settings.

             **Cameo Overlays.**
             When generating the addon, GenHub stamps the assigned shortcut letter directly onto the corner of each unit and building cameo texture. In-game command buttons display your hotkeys automatically while playing.
             """,
             null),
            (InfoConstants.CardToolsPublisherStudioPipeline,
             "Publisher Studio: 3-Tier Publishing Pipeline",
             "Decentralized distribution across Publisher Definitions, Content Catalogs, and Immutable Release Artifacts.",
             InfoCardType.Concept,
             """
             GenHub uses a fully decentralized publishing model allowing creators to distribute content directly without central servers.

             **1. Tier 1: Publisher Definition (publisher.json)**
             * **Identity & Branding:** Stores publisher display name, avatar, description, website, and support links.
             * **Catalog Discovery:** Points to one or more Tier 2 catalog URLs under a unified publisher identity.
             * **Self-Healing URL Migration:** Contains permanent definitionUrl and previousDefinitionUrls. When hosting moves (e.g. Google Drive to Cloudflare R2), client launchers automatically heal subscription endpoints.
             * **Hosting Rule:** Tier 1 URLs must remain permanent and update in-place when metadata or catalog links change.

             **2. Tier 2: Content Catalogs (catalog-*.json)**
             * **Manifest & Package Index:** Defines packages, display titles, categories, authors, and target games (Generals, Zero Hour, or custom engine variants).
             * **Versioning & Dependencies:** Tracks SemVer version numbers, release changelogs, tags, and parent dependencies using extendsContentId.
             * **Release Records:** Each version release records exact artifact filenames, SHA-256 integrity hashes, byte sizes, and download URLs.
             * **Hosting Rule:** Tier 2 catalog URLs are permanent endpoints updated in-place whenever a new release is published.

             **3. Tier 3: Artifact Archives (*.zip, .big)**
             * **Immutable Binary Payloads:** The actual mod archives, map packs, executables, or data files installed by players.
             * **Zero Mutation Guarantee:** Every release or patch produces a new immutable download URL and SHA-256 hash. Older artifact archives must never be overwritten or deleted from the CDN, ensuring rollbacks and dependency resolution never break.
             """,
             null),
            (InfoConstants.CardToolsPublisherStudioCdnHosting,
             "Publisher Studio: External CDN & Link Maintenance",
             "Publishers maintain external CDN links — GenHub does not centrally store or host mod files.",
             InfoCardType.Feature,
             """
             GenHub is decentralized. It does not provide central CDN hosting or mirror mod archives on a central server; all downloads come directly from creator-managed external CDN hosting and cloud storage.

             **Publisher Responsibilities:**
             * **Link Availability:** Publishers are entirely responsible for hosting bandwidth and link availability. If an external file URL expires, is removed, or exceeds quota, player downloads will fail until an updated catalog is published.
             * **Retaining Old Versions:** Players can lock profiles to specific versions or roll back updates. Older Tier 3 artifact archives must remain permanently accessible on your storage provider.
             * **Mirror Redundancy:** Manifests support secondary mirror URLs (mirrors: [...]). Publishers can declare fallback endpoints (e.g. GitHub Releases primary with Google Drive or Dropbox mirrors) for maximum reliability.

             **Supported Hosting Providers:**
             * **Google Drive:** Integrated OAuth 2.0. Publisher Studio creates a GenHub_Publisher folder and uses Files.Update to keep Tier 1 and 2 file IDs stable while uploading new Tier 3 archives.
             * **Dropbox:** Integrated OAuth 2.0 with direct download link generation (?dl=1).
             * **GitHub Releases:** High-bandwidth HTTPS releases with immutable tags.
             * **Direct HTTPS / S3 / R2:** Compatible with Amazon S3, Cloudflare R2, DigitalOcean Spaces, or any HTTPS server with CORS enabled.
             """,
             null),
            (InfoConstants.CardToolsPublisherStudioUpdateNotifications,
             "Publisher Studio: Update Detection & Notifications",
             "How background catalog polling, SemVer comparison, and actionable notifications keep players up to date.",
             InfoCardType.Feature,
             """
             GenHub automatically alerts players when mod creators publish new updates, patches, or map releases.

             **1. Player Subscriptions:**
             * Players subscribe to creators via one-click deep links (genhub://subscribe?url=...), imported URLs, or QR codes.
             * Subscriptions are tracked locally in subscriptions.json, where players can toggle individual catalogs on or off.

             **2. Periodic Background Polling:**
             * The hosted PublisherCatalogUpdateService runs in the background (every 30 minutes and on launcher startup).
             * It queries only lightweight Tier 1 and Tier 2 JSON manifests via HTTP conditional headers. GenHub never downloads heavy game files during polling, minimizing publisher bandwidth usage.

             **3. Version Diff & Notification Workflow:**
             * **SemVer Evaluation:** IContentStateService compares installed package versions in CAS against the latest release version in the catalog.
             * **Actionable Notifications:** When a newer SemVer is discovered, the item state transitions to ContentState.UpdateAvailable. GenHub dispatches an in-app desktop notification with release notes and displays an 'Update Available' badge on the content card in the Downloads tab.
             * **Instant Rollbacks:** Clicking Update downloads the new Tier 3 artifact into local Content Addressable Storage (CAS). Previous versions are retained, allowing instant rollbacks at any time from Profile Settings.
             """,
             null),
            (InfoConstants.CardToolsPublisherStudioAuthoring,
             "Publisher Studio: Authoring & Interactive Showcase",
             "Visual content library, variant packaging, cloud publishing, and live studio access.",
             InfoCardType.HowTo,
             """
             Publisher Studio is a full-featured authoring environment integrated directly into GenHub under the **Tools** tab.

             **Authoring Workflow:**
             1. **Content Library:** Drag and drop game folders or .big archives into the library. The tool inspects contents, auto-detects target engines, generates slug IDs, and computes SHA-256 hashes.
             2. **Variants vs Bundles:** Configure content as a **Single Bundle** (all files installed together) or **Alternative Variants** (allowing users to choose between 1080p and 4K textures, or English and German audio).
             3. **Dependency Links:** Declare parent dependencies using extendsContentId and version constraints so required base mods or patches install automatically.
             4. **Publish & Share:** Connect your Google Drive, Dropbox, or custom CDN. Click **Publish** to upload manifests, generate genhub://subscribe links, and produce ready-to-share QR codes for community distribution.

             **Accessing Publisher Studio:**
             Click the **Open Publisher Studio** action button below or navigate to the **Tools** tab to launch the authoring studio directly.
             """,
             [
                 new InfoAction
                 {
                     Label = "Open Publisher Studio",
                     ActionId = InfoConstants.ActionNavTools,
                     IconKey = InfoConstants.IconBookOpenVariant,
                     IsPrimary = true,
                 },
             ]),
            (InfoConstants.CardToolsModBuilderSuitePipeline,
             "ModBuilder Suite: Asset Pipelines",
             "Mod workspace management, texture batch conversion, and string table compiling.",
             InfoCardType.Concept,
             """
             The ModBuilder Suite provides complete tooling for Command & Conquer SAGE engine mod development, integrated into GenHub.

             **Workspace Projects.**
             ModBuilder projects use .mbproj files (such as Shockwave.mbproj) to track source directories, build targets, and external tools like Crunch and FinalBIG.

             **Asset Pipeline.**
             * **Textures:** Batch converts image files between PNG, TGA, and DDS formats with mipmaps and alpha transparency preserved.
             * **String Tables:** Compiles and decompiles .csf string files. Supports UTF-8 and UTF-16 character encodings with an in-editor string table viewer.
             * **INI Scripts:** Automated macro substitutions and syntax checks across game object definitions.
             """,
             null),
            (InfoConstants.CardToolsModBuilderSuiteWndBuild,
             "ModBuilder Suite: WND Editor & BIG Builds",
             "GUI layout validation, canonical formatting, and incremental BIG packaging.",
             InfoCardType.Feature,
             """
             **WND Interface Editor.**
             Structured editor for Command & Conquer GUI layout files (.wnd).
             * **Validation:** Parses UI control hierarchies, detects unclosed blocks, and flags syntax errors by line number.
             * **Canonical Formatting:** Formats .wnd indentation with standard tab stops while preserving developer comments.

             **Incremental BIG Builds.**
             The build engine tracks file MD5 checksums. It compiles only modified assets and packages them into release .big archives (such as INIZeroHour.big) directly inside your profile workspace.
             """,
             null),
        ];

        return new InfoSection
        {
            Id = InfoConstants.SectionTools,
            Title = "Tools & Utilities",
            Description = "Inspect replays, manage maps, rebind hotkeys, build mods, and publish community content catalogs.",
            Order = 6,
            Cards = cardData.Select(c => CreateCard(c.Id, c.Title, c.Content, c.Type, c.Detailed, c.Actions)).ToList(),
        };
    }

    /// <summary>
    /// Creates the game detection section.
    /// </summary>
    /// <returns>The game detection <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateScanForGamesSection()
    {
        (string Id, string Title, string Content, InfoCardType Type, string Detailed)[] cardData =
        [
            (InfoConstants.CardScanDemo,
             "Interactive Demo: Scan Wizard",
             "Interactive game scanner simulating discovery across disks and registries.",
             InfoCardType.Feature,
             """
             **Interactive Scan Wizard:**
             Simulate automatic game discovery across Steam, EA App, CD directories, and custom folders.
             * Click Start Scan to simulate disk scanning and cryptographic SHA-256 signature verification.
             * Review discovered game clients before adding them to your library.
             """),
            (InfoConstants.CardScanAutoDetection,
             "Auto-Detection Across Platforms",
             "Locate Steam, EA App, CD, and Wine/Proton game installations.",
             InfoCardType.Concept,
             """
             **Detection Methods:**
             GenHub scans your system for official game releases:
             1. **Steam Libraries:** Automatically detects Command & Conquer: The Ultimate Collection across all Steam library folders (official Steam releases: Generals 1.09, Zero Hour 1.04).
             2. **EA App & Origin:** Locates official EA App install directories and registry entries.
             3. **Classic CD & Retail:** Checks standard installation paths and registry keys for classic disc editions.
             4. **Wine environments:** Locates Windows game prefixes on Linux and macOS environments.

             If your game resides in a custom directory, click **Browse** to link the folder manually.
             """),
            (InfoConstants.CardScanSignatureVerification,
             "Hash & Version Verification",
             "Integrity checks and binary version verification.",
             InfoCardType.Feature,
             """
             **Binary Verification:**
             GenHub calculates SHA-256 hashes of generals.exe and game.dat to identify exact game versions and verify file integrity.
             * **Verified:** Matches known official releases (Steam edition, EA App, The First Decade, or v1.04).
             * **Unverified:** Custom or modified community binaries are flagged as unverified, but remain fully launchable.
             """),
            (InfoConstants.CardScanCrossPlatformDetection,
             "Linux & macOS Client Detection",
             "Native binary analysis and archive linking for Wine environments.",
             InfoCardType.Feature,
             """
             **Binary Format Analysis.**
             On Linux and macOS, GenHub inspects application bundles (.app) and executable binary headers to distinguish native Mach-O, ELF, and Windows PE binaries for Wine environments.

             **Supplemental Archive Linking.**
             On non-Windows Zero Hour launches, GenHub links safe base Generals audio and speech archives into the Zero Hour workspace so sound effects and campaigns play correctly.
             """),
        ];

        return new InfoSection
        {
            Id = InfoConstants.SectionScanGames,
            Title = "Game Detection",
            Description = "Automatically detect, verify, and link game installations across Windows, Linux, and macOS.",
            Order = 7,
            Cards = cardData.Select(c => CreateCard(c.Id, c.Title, c.Content, c.Type, c.Detailed)).ToList(),
        };
    }

    /// <summary>
    /// Creates the virtual workspaces section.
    /// </summary>
    /// <returns>The workspaces <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateWorkspaceSection()
    {
        return new InfoSection
        {
            Id = InfoConstants.SectionWorkspaces,
            Title = "Virtual Workspaces",
            Description = "Workspace strategies, file linking techniques, and isolation mechanics.",
            Order = 8,
            Cards =
            [
                new InfoCard
                {
                    Id = InfoConstants.CardWorkspaceDemo,
                    Title = "Interactive Demo: Filesystem Magic",
                    Content = "Live filesystem visualizer demonstrating hardlinks, symlinks, and junctions.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Interactive Workspace Visualizer:**
                    See exactly how files are linked when building a profile workspace.
                    * Toggle between HardLink, SymlinkOnly, HybridCopySymlink, and FullCopy modes.
                    * Inspect zero-byte pointer mechanics and directory junctions in real time.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardWorkspacesMagicMirror,
                    Title = "The Magic Mirror",
                    Content = "Understanding how isolated game workspaces work.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **How Workspaces Work:**
                    When you click Play, GenHub instantly prepares a dedicated workspace folder for that specific profile.

                    **Key Benefits:**
                    1. **Zero Extra Disk Space:** In linked modes (HardLink and SymlinkOnly), the workspace functions as a complete multi-gigabyte game folder while consuming virtually 0 MB of extra disk space.
                    2. **Complete Profile Isolation:** Mods and configurations live in dedicated profile workspaces. Your main game directory remains untouched, so files never get mixed up. (For mods that modify game binaries in-place, select Hybrid or Full Copy mode).
                    3. **Instant Switching:** Switch between large total conversions like *Rise of the Reds* and *ShockWave* in seconds without reinstalling or moving files.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardWorkspacesStrategies,
                    Title = "Workspace Strategies Compared",
                    Content = "Comparing HardLink, SymlinkOnly, HybridCopySymlink, and FullCopy strategies.",
                    Type = InfoCardType.Concept,
                    IsExpandable = true,
                    DetailedContent = """
                    **Choosing the Right Strategy:**
                    GenHub supports four file linking strategies under **Settings -> Game Configuration**:

                    * **HardLink (Default & Recommended):**
                        * *How it works:* Creates direct filesystem pointers (hard links) on the same drive. Across different drives, it uses symbolic links; if symlink creation fails, workspace preparation fails. Select **FullCopy** instead.
                        * *Disk Space:* **0 bytes** extra storage (requires the same drive).
                        * *Speed:* Instant (< 50ms) on the same volume.
                        * *Privileges:* No administrator privileges or Developer Mode required on the same drive. Cross-drive symbolic link fallback has the same requirements as SymlinkOnly.
                        * *Recommendation:* Keep your workspaces and game installation on the **same drive** (e.g. both on `C:` or both on `D:`) for optimal zero-space performance.

                    * **SymlinkOnly:**
                        * *How it works:* Creates symbolic links pointing to source files and directories.
                        * *Disk Space:* **Negligible** (~a few KB of link pointers).
                        * *Speed:* Instant (< 50ms).
                        * *Advantage:* Links seamlessly across **different drives and partitions**.
                        * *Requirement:* On Windows, requires **Administrator rights** or **Developer Mode** enabled in Windows Settings.

                    * **HybridCopySymlink (Balanced Compatibility):**
                        * *How it works:* Copies essential engine files, scripts, and configuration files into the workspace while symlinking large media files (textures, audio, and video).
                        * *Disk Space:* Balanced footprint (copies key configs, links media).
                        * *Speed:* Fast (1-2 seconds).
                        * *Advantage:* Protects configuration files from cross-profile conflicts while keeping disk usage low.

                    * **FullCopy (Universal Fallback):**
                        * *How it works:* Physically copies every game and mod file into the workspace directory.
                        * *Disk Space:* Uses the full game size (**2-5+ GB** per profile).
                        * *Speed:* Slower (10-30+ seconds depending on drive speed).
                        * *Advantage:* Maximum compatibility across external drives, network drives, and restricted environments.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardWorkspacesDeepDive,
                    Title = "Hardlinks vs Symlinks vs Copies: Deep Dive",
                    Content = "How file linking differs under the hood.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **How Linking Works Under the Hood:**

                    * **Hardlink:**
                        A hardlink points directly to the existing file data on disk at the filesystem level. Because the underlying file data is shared, creating a hardlink takes zero extra storage. Hardlinks must reside on the same drive partition as the original file.

                    * **Symlink (Symbolic Link):**
                        A symlink is a lightweight pointer that stores a path to the target file or folder, similar to a transparent operating system shortcut. Symlinks can cross different drives, but Windows security policies require elevated privileges or Developer Mode to create them.

                    * **Full Copy:**
                        A complete duplicate of the file written to a new location on disk.

                    **Automatic Fallback:**
                    If you configure Symlink mode but run GenHub without administrator rights or Developer Mode, GenHub automatically falls back to hardlinks when files reside on the same drive, ensuring your game launches without interruption.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardWorkspacesTroubleshooting,
                    Title = "Troubleshooting & Permissions",
                    Content = "Resolving common permissions and workspace build errors.",
                    Type = InfoCardType.HowTo,
                    IsExpandable = true,
                    DetailedContent = """
                    **Common Issues & Solutions:**

                    * **"Access Denied" or Privilege Errors:**
                        * If using the Symlink strategy on Windows, enable **Developer Mode** in *Windows Settings -> System -> For developers*, or run GenHub as Administrator.
                        * Alternatively, switch your Default Workspace Strategy to **HardLink** in GenHub Settings when the game and workspace use the same drive.
                    * **Cross-Drive Linking & Storage:**
                        * Hardlinks require both the game files and workspace to be on the same drive volume. Across different drives, **HardLink** uses symbolic links; if symlink creation fails, workspace preparation fails. Select **FullCopy** instead.
                        * To keep workspaces fast and zero-space, place your CAS pool and workspace directories on the same drive as your game installation in **Settings -> Data Directories**, or enable Developer Mode for symlinks.
                    * **"File In Use" / Locked File Warnings:**
                        * Make sure all instances of `generals.exe` and `game.dat` are closed before switching profiles or rebuilding workspaces.
                    """,
                },
                new InfoCard
                {
                    Id = InfoConstants.CardWorkspacesPerformance,
                    Title = "Performance Specs",
                    Content = "Efficiency, speed, and integrity metrics across strategies.",
                    Type = InfoCardType.Feature,
                    IsExpandable = true,
                    DetailedContent = """
                    **Strategy Performance Summary:**

                    * **HardLink:**
                        * *Creation Time:* < 50ms on same volume
                        * *Disk Overhead:* 0 MB on same volume (requires same drive volume)
                        * *Integrity:* Shared data clusters (CAS objects remain immutable in the cache)
                    * **SymlinkOnly:**
                        * *Creation Time:* < 50ms
                        * *Disk Overhead:* < 1 MB
                        * *Integrity:* Pointer redirection across drives
                    * **Hybrid:**
                        * *Creation Time:* 1-2 seconds
                        * *Disk Overhead:* Small (copies essential configs, links media assets)
                        * *Integrity:* Isolated configs, shared media links
                    * **Full Copy:**
                        * *Creation Time:* 10-30 seconds
                        * *Disk Overhead:* Full game size (2,000 - 5,000+ MB)
                        * *Integrity:* Total physical file separation
                    """,
                },
            ],
        };
    }

    /// <summary>
    /// Creates the app updates section.
    /// </summary>
    /// <returns>The app updates <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateAppUpdatesSection()
    {
        (string Id, string Title, string Content, InfoCardType Type, string Detailed)[] cardData =
        [
            (InfoConstants.CardUpdatesDemo,
             "Interactive Demo: Updates & CI Builds",
             "Test update channels, release notes view, and GitHub CI workflow artifacts.",
             InfoCardType.Feature,
             """
             **Interactive Update Notification Sandbox:**
             Test launcher update mechanics and automated CI build subscriptions.
             * Inspect release notes and delta update payloads.
             * Switch to Browse Builds to test subscribing to automated PR and branch builds.
             """),
            (InfoConstants.CardUpdatesVersionControl,
             "Interactive Update Window & Release Channels",
             "Explore the live update window demo above, with Update and Browse Builds tabs.",
             InfoCardType.Concept,
             """
             GenHub includes an interactive update notification window. You can explore a simulated version of this window in the demo panel above.

             **Update Tab.**
             * **Version Banner:** Displays your current version alongside the incoming release (for example, simulated update v1.2.0).
             * **View Release Notes:** Opens the official GitHub release page in your browser to review new features and bug fixes.
             * **Force Refresh:** Re-queries the update server and GitHub API immediately for newly published tags.
             * **Install Update:** Downloads the compressed update package in the background and restarts GenHub to apply it.

             **Browse Builds Tab.**
             * **Open Pull Requests:** Browse automated CI builds for active pull requests (such as #101 Feature: Enhanced Profile Management or #102 Fix: Application crash on startup). Click any PR link to view its review status on GitHub.
             * **Branches:** Browse builds generated from Git branches (such as dev, v1.2-beta, or feature/ui-rework).
             * **Subscribe:** Click Subscribe next to a PR or branch to track that channel and get notified when new CI test builds are published.

             **Interactive Sandbox.**
             The demo panel above runs against simulated data so you can safely switch tabs, click buttons, and inspect the interface without modifying your installed files.
             """),
            (InfoConstants.CardUpdatesWorkflow,
             "Update Delivery & Background Downloads",
             "Background package downloads with delta compression via Velopack.",
             InfoCardType.HowTo,
             """
             GenHub delivers updates without interrupting active game sessions.

             **Background Downloads.**
             When an update is detected, GenHub downloads installer files quietly in the background. Gameplay and profile editing remain fully accessible while downloading.

             **Delta Compression.**
             GenHub uses Velopack delta packages. Instead of downloading the full application archive, only modified binaries and assets are downloaded, keeping bandwidth usage low.

             **Applying Updates.**
             Once downloaded, click **Install Update** to apply the new files and relaunch GenHub in seconds.
             """),
            (InfoConstants.CardUpdatesRollback,
             "Rollback Safety & Profile Protection",
             "Safely revert launcher versions without losing profile configurations.",
             InfoCardType.Feature,
             """
             GenHub separates launcher binaries from user data. Profile configurations, custom hotkeys, and downloaded content manifests are stored independently from the application installation directory. If you install an earlier launcher version, all your profiles, save files, and settings remain untouched.
             """),
            (InfoConstants.CardUpdatesGitHubOAuthDevice,
             "GitHub Sign-In: Device Flow (PAT Obsolete)",
             "Browser device authorization replaces obsolete personal access tokens.",
             InfoCardType.HowTo,
             """
             Personal Access Tokens (PAT) are obsolete and no longer used in GenHub. You do not need to generate or paste API keys.

             **OAuth Device Flow.**
             GenHub uses GitHub's standard device authorization grant:
             1. Open **Settings -> Updates** (or the GitHub Discovery view) and click **Sign in with GitHub**.
             2. GenHub requests an 8-character user code (such as WDAS-5678) from GitHub.
             3. Click **Copy and Open**. GenHub copies the user code to your clipboard and opens github.com/login/device in your default browser.
             4. Paste the user code into the GitHub prompt and click **Authorize community-outpost**.
             5. GenHub confirms authorization and saves credentials into encrypted local storage.

             **Session Persistence.**
             If your authorization session expires, GenHub displays a notification in Settings rather than silently failing downloads.

             **Rate Limits and CI Builds.**
             Signing in raises GitHub API limits from 60 anonymous requests per hour to 5,000 requests per hour. This allows the Browse Builds tab to download automated CI pull request and branch artifacts.
             """),
            (InfoConstants.CardUpdatesCiArtifactsPrTesting,
             "Testing Pull Requests & CI Builds",
             "Test development builds from open pull requests and branches.",
             InfoCardType.Feature,
             """
             Community members and developers can test upcoming features and bug fixes before official releases.

             **Subscribing to Builds.**
             In the **Browse Builds** tab of the update window, click **Subscribe** on active pull requests or branches. GenHub monitors the CI pipeline and notifies you with an update action when new builds are published.

             **False Update Suppression.**
             Development and PR builds detect their Git commit origin and suppress update prompts for older stable releases.
             """),
            (InfoConstants.CardUpdatesOfflineDownloads,
             "Offline Mode & Content Storage",
             "Access downloaded content offline with content addressable deduplication.",
             InfoCardType.Feature,
             """
             The Downloads tab functions fully without an active internet connection once content is downloaded.

             **Offline Browsing.**
             You can filter downloaded mods, custom maps, and game patches, inspect version numbers, and configure profiles while offline.

             **Content Addressable Storage (CAS).**
             Downloaded packages are stored by SHA-256 hash. When multiple mods share identical texture archives or audio files, GenHub keeps only one physical copy on disk.

             **Safe Cascade Deletion.**
             When deleting a downloaded package from your library, GenHub checks your active game profiles. If a profile currently uses that content, GenHub displays a warning before removal to prevent breaking your game workspace.
             """),
        ];

        return new InfoSection
        {
            Id = InfoConstants.SectionAppUpdates,
            Title = "App Updates",
            Description = "Manage launcher updates, GitHub authentication, and offline content storage.",
            Order = 9,
            Cards = cardData.Select(c => CreateCard(c.Id, c.Title, c.Content, c.Type, c.Detailed)).ToList(),
        };
    }

    /// <summary>
    /// Creates the changelog section.
    /// </summary>
    /// <returns>The changelog <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateChangelogSection()
    {
        (string Id, string Title, string Content, InfoCardType Type, string Detailed)[] cardData =
        [
            (InfoConstants.CardChangelogsDemo,
             "Interactive Demo: Release Browser",
             "Live interactive release notes browser with version selection, asset links, and filters.",
             InfoCardType.Feature,
             """
             **Interactive Changelog Viewer:**
             Browse all official GenHub releases, view release assets, inspect patch notes, and see what's new in each build.
             * Click any release in the list below to inspect its detailed release notes.
             * Directly download installers or source assets.
             * Release items are automatically linked in the right navigation bar.
             """),
            (InfoConstants.CardChangelogsOverview,
             "Release History & Changelogs",
             "Track all official GenHub desktop releases, patch notes, and engine improvements.",
             InfoCardType.Feature,
             """
             **Official Release History:**
             GenHub releases are distributed directly from official GitHub releases. Each release provides an overview of new features, bug fixes, engine compatibility updates, and performance enhancements.

             * **SemVer Numbering:** Versioning strictly follows Semantic Versioning (`MAJOR.MINOR.PATCH`).
             * **Pre-releases:** Beta releases with testing builds are marked with a Pre-release badge.
             * **Live Sync:** Release notes are fetched automatically with local caching for offline viewing.
             """),
            (InfoConstants.CardChangelogsUpdates,
             "Automatic Update Distribution",
             "How releases are polled, verified, and safely applied via background services.",
             InfoCardType.HowTo,
             """
             **Background Update Detection:**
             GenHub checks for newer releases in the background using GitHub API and publisher manifests. When an update is ready, GenHub notifies you and applies it seamlessly.
             """),
            (InfoConstants.CardChangelogsCompatibility,
             "Rollbacks & Workspace Stability",
             "Safeguard your mods and game profiles across version changes.",
             InfoCardType.Concept,
             """
             **Safe Rollbacks:**
             Because GenHub keeps game configurations isolated in profiles and zero-copy workspaces, updating GenHub never corrupts your mods, replays, or game installations.
             """),
        ];

        return new InfoSection
        {
            Id = InfoConstants.SectionChangelogs,
            Title = "Changelog",
            Description = "Version history and patch notes for GenHub releases.",
            Order = 10,
            Cards = cardData.Select(c => CreateCard(c.Id, c.Title, c.Content, c.Type, c.Detailed)).ToList(),
        };
    }

    /// <summary>
    /// Creates the Generals Online FAQ section.
    /// </summary>
    /// <returns>The Generals Online FAQ <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateGeneralsOnlineFAQSection()
    {
        var faqData = new (string Id, string Title, string Content, InfoCardType Type, string Detailed)[]
        {
            (InfoConstants.CardFaqPrefix + "0", "What is Generals Online?", "Generals Online is a modern multiplayer and lobby platform for Command & Conquer: Generals and Zero Hour.", InfoCardType.Concept, "Generals Online replaces the discontinued GameSpy service with modern multiplayer matchmaking, lobby features, automatic updates, and ladder rankings, preserving classic gameplay while delivering stable online play on modern PCs."),
            (InfoConstants.CardFaqPrefix + "1", "Do I need a clean install of Zero Hour?", "No. Generals Online works alongside your existing installation.", InfoCardType.HowTo, "You do not need a fresh game installation or to delete existing files. GenHub isolates Generals Online so your base game files remain untouched."),
            (InfoConstants.CardFaqPrefix + "2", "Can I play Generals Online if I have GenTool or GenPatcher installed?", "Yes. Generals Online is fully compatible with GenTool and GenPatcher.", InfoCardType.Concept, "Generals Online runs in its own profile environment and works alongside GenTool widescreen and anti-cheat features without conflicts."),
            (InfoConstants.CardFaqPrefix + "3", "Can I use custom UI or control bars?", "Yes. Custom UI assets and control bars are supported.", InfoCardType.Concept, "Custom UI modifications, such as HUD control bars, work normally in Generals Online."),
            (InfoConstants.CardFaqPrefix + "4", "Does Generals Online modify my original game files?", "No. Your original installation files are never modified.", InfoCardType.Concept, "Generals Online runs from an isolated profile workspace. Your main game folder remains clean and untouched."),
            (InfoConstants.CardFaqPrefix + "5", "Are custom maps supported?", "Yes. Custom maps and in-lobby map transfers are supported.", InfoCardType.Feature, "Generals Online supports in-game and lobby map downloads so you can play custom maps with other players without manual file transfers."),
            (InfoConstants.CardFaqPrefix + "6", "How do I launch Generals Online?", "Launch through GenHub or your profile desktop shortcut.", InfoCardType.HowTo, "Select your Generals Online profile in GenHub and click Play, or launch it directly with a desktop shortcut created from that profile."),
            (InfoConstants.CardFaqPrefix + "7", "Which game versions are supported?", "Developed and tested for official Steam and EA App / Origin releases.", InfoCardType.Concept, "Generals Online is designed for official Steam and EA releases. For the best experience and easiest setup, the Steam release of Command & Conquer: The Ultimate Collection is recommended."),
            (InfoConstants.CardFaqPrefix + "8", "How do I log in?", "Sign in securely using Steam, Discord, or GameReplays.", InfoCardType.HowTo, "Generals Online uses OpenID authentication. You authenticate directly through Steam, Discord, or GameReplays, so your account passwords are never seen or stored by Generals Online."),
            (InfoConstants.CardFaqPrefix + "9", "Is logging in safe?", "Yes. OpenID ensures your account password remains completely private.", InfoCardType.Concept, "OpenID only transmits a secure account identifier to verify your identity. Your login credentials are handled directly by Steam, Discord, or GameReplays."),
            (InfoConstants.CardFaqPrefix + "10", "How do I check if the service is online?", "Check the in-game status, the community Discord, or the status page.", InfoCardType.Feature, "Live service status is shown on the login screen, with real-time announcements available on the community Discord."),
            (InfoConstants.CardFaqPrefix + "11", "How do I report bugs or suggest features?", "Join the community Discord to submit feedback.", InfoCardType.HowTo, "The development team actively tracks issues and community suggestions in dedicated Discord channels."),
            (InfoConstants.CardFaqPrefix + "12", "How are updates delivered?", "Updates download automatically through the launcher.", InfoCardType.Feature, "When an update is released, GenHub detects and applies it so you are always on the latest version."),
            (InfoConstants.CardFaqPrefix + "13", "Do I need third-party VPN tools (Hamachi, Radmin, GameRanger)?", "No. Online matchmaking is built directly into the service.", InfoCardType.Concept, "Generals Online includes native networking and matchmaking. You do not need third-party virtual LAN software or external wrappers to play online."),
            (InfoConstants.CardFaqPrefix + "14", "Do I need to forward router ports?", "No. Built-in NAT traversal connects players automatically.", InfoCardType.Concept, "Modern NAT traversal handles player connections automatically without requiring manual port forwarding on your home router."),
            (InfoConstants.CardFaqPrefix + "15", "Is network communication secure?", "Yes. Game traffic is encrypted using AES-256.", InfoCardType.Feature, "Network traffic uses industry-standard AES-256-GCM encryption, providing significantly better security than the original game engine's unencrypted packets."),
            (InfoConstants.CardFaqPrefix + "16", "Why did Windows Firewall prompt for permission?", "Windows prompts when a new app accesses the network for the first time.", InfoCardType.HowTo, "When connecting to multiplayer servers for the first time, Windows Firewall asks to allow network access. Click Allow to enable online connectivity."),
            (InfoConstants.CardFaqPrefix + "17", "What are connection relays?", "Relays route traffic when direct peer-to-peer connections are blocked.", InfoCardType.Concept, "If two players have strict firewalls that prevent direct peer-to-peer connection, traffic routes through community relay servers (similar to Steam networking or CNCNet tunnels)."),
            (InfoConstants.CardFaqPrefix + "18", "Do relays cause lag or performance drops?", "Typically no. Relays use high-bandwidth, low-latency backbone servers.", InfoCardType.Concept, "Relay servers are hosted on high-speed backbones and often provide comparable or better latency than congested direct peer-to-peer routes."),
            (InfoConstants.CardFaqPrefix + "19", "How does the game select which relay to use?", "Relay connections are formed dynamically on a player-to-player basis.", InfoCardType.Feature, "Relay connections are established dynamically per player pair, selecting the server location with the lowest latency for that match. Users in the same lobby can connect through different regional edge nodes to achieve optimal ping."),
            (InfoConstants.CardFaqPrefix + "20", "Are relays secure?", "Yes. Relays cannot decrypt match traffic.", InfoCardType.Feature, "Relay servers forward encrypted packets and do not have access to the encryption keys required to read or inspect traffic."),
            (InfoConstants.CardFaqPrefix + "21", "Can I host a relay?", "Community relay hosting is not needed at this time.", InfoCardType.Concept, "Generals Online operates on global edge infrastructure spanning hundreds of data centers worldwide, delivering low latency without requiring community relay hosting."),
        };

        return new InfoSection
        {
            Id = InfoConstants.SectionFaq,
            Title = "Frequently Asked Questions",
            Description = "Common questions about the Generals Online service.",
            Order = 11,
            Cards = faqData.Select(c => CreateCard(c.Id, c.Title, c.Content, c.Type, c.Detailed)).ToList(),
        };
    }

    /// <summary>
    /// Creates the Generals Online changelog section.
    /// </summary>
    /// <returns>The Generals Online changelog <see cref="InfoSection"/>.</returns>
    private static InfoSection CreateGeneralsOnlineChangeLogSection()
    {
        (string Id, string Title, string Content, InfoCardType Type, string Detailed)[] cardData =
        [
            (InfoConstants.CardGoChangelogDemo,
             "Interactive Demo: Generals Online Patch Notes",
             "Live feed of multiplayer service updates, netcode improvements, and balance patches.",
             InfoCardType.Feature,
             """
             **Generals Online Patch Notes Viewer:**
             Stay informed about multiplayer network changes, matchmaker improvements, balance adjustments, and anti-cheat updates deployed to the Generals Online network.
             * Select any update from the patch notes list below to view its details.
             * All updates are indexed and navigable from the right sidebar table of contents.
             """),
            (InfoConstants.CardGoChangelogOverview,
             "Generals Online Patch Notes",
             "Latest service updates, lobby fixes, and netcode improvements.",
             InfoCardType.Feature,
             """
             **Generals Online Service Updates:**
             Stay informed about multiplayer network changes, matchmaker improvements, balance adjustments, and anti-cheat updates deployed to the Generals Online network.
             """),
            ("go-patch-notes-netcode",
             "Relays & Edge Infrastructure",
             "Low-latency UDP edge routing, NAT traversal, and disconnect protection.",
             InfoCardType.Concept,
             """
             **Multiplayer Infrastructure:**
             Generals Online routes match traffic through global edge relays, ensuring smooth peer connections and NAT traversal without manual port forwarding.
             """),
        ];

        return new InfoSection
        {
            Id = InfoConstants.SectionGoChangelog,
            Title = "Changelog",
            Description = "View the latest changes and updates to the Generals Online service.",
            Order = 12,
            Cards = cardData.Select(c => CreateCard(c.Id, c.Title, c.Content, c.Type, c.Detailed)).ToList(),
        };
    }

    /// <summary>
    /// Creates a new <see cref="InfoCard"/> instance.
    /// </summary>
    /// <param name="id">The unique card identifier.</param>
    /// <param name="title">The card title.</param>
    /// <param name="content">The card summary content.</param>
    /// <param name="type">The card type.</param>
    /// <param name="detailed">The detailed expandable content.</param>
    /// <param name="actions">The optional card actions.</param>
    /// <returns>A populated <see cref="InfoCard"/>.</returns>
    private static InfoCard CreateCard(
        string id,
        string title,
        string content,
        InfoCardType type,
        string? detailed = null,
        IReadOnlyList<InfoAction>? actions = null)
    {
        return new InfoCard
        {
            Id = id,
            Title = title,
            Content = content,
            Type = type,
            DetailedContent = detailed,
            IsExpandable = !string.IsNullOrWhiteSpace(detailed),
            Actions = actions != null ? [.. actions] : [],
        };
    }
}
