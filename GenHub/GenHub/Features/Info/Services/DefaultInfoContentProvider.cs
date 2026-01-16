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
public class DefaultInfoContentProvider : IInfoContentProvider
{
    private readonly List<InfoSection> _sections;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInfoContentProvider"/> class.
    /// </summary>
    public DefaultInfoContentProvider()
    {
        _sections = CreateContent();
    }

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
        ];
    }

    private static InfoSection CreateGameProfilesSection()
    {
        return new InfoSection
        {
            Id = "game-profiles",
            Title = "Game Profiles",
            Description = "The heart of GenHub. Manage your Mods, Maps, and Patches.",
            IconKey = "ControllerClassic",
            Order = 0,
            Cards =
            [
                new InfoCard
                {
                    Title = "The Profile Card Explained",
                    Content = "The card you see in the demo above is your main control center. Here is exactly what every element does:",
                    Type = InfoCardType.HowTo,
                    IconKey = "CursorDefaultClick",
                    IsExpandable = true,
                    DetailedContent = """
                    **Main Interactive Elements:**

                    1.  **Launch Button (The big play icon):**
                        -   **Action:** Prepares the workspace and launches `generals.exe` or `game.dat`.
                        -   **Behavior:** The button pulses while launching. GenHub minimizes to tray (if configured).

                    2.  **Profile Name (Text):**
                        -   **Action:** Displays the name you gave the profile (e.g., "RotR 1.87").
                        -   **Note:** You can rename this at any time using the Edit button.

                    3.  **Steam Button (Icon: Steam Logo):**
                        -   **Video Guide:** Hover over the card to see the button appear compared to the dimmed version.
                        -   **State: Dimmed:** Steam integration is OFF.
                        -   **State: Lit/Colored:** Steam integration is ON.
                        -   **How to Use:** Click this icon directly on the card to toggle it. When ON, GenHub tells Steam you are playing "Command & Conquer: Generals", enabling the Shift+Tab overlay and time tracking.

                    4.  **Edit Button (Icon: Pencil):**
                        -   **Action:** Opens the **Profile Editor**.
                        -   **Use this to:** Change names, icons, or most importantly, **Manage Enabled Content** (Mods, Maps, Patches).

                    5.  **Settings Button (Icon: Cog/Gear):**
                        -   **Action:** Opens the **Options.ini Editor** specific to *this* profile.
                        -   **Use this to:** Change resolution, camera height, or volume for *this specific mod* without affecting others.

                    6.  **Delete Button (Icon: Trash Can - visible on hover/context menu):**
                        -   **Action:** Permanently removes the profile configuration.
                        -   **Safety:** It does *not* delete your save games or the mod files themselves, just the GenHub profile entry.
                    """,
                    Actions =
                    [
                        new InfoAction { Label = "Create a New Profile", ActionId = InfoNavigationActions.NavigateToGameProfiles, IconKey = "Plus", IsPrimary = true },
                    ],
                },
                new InfoCard
                {
                    Title = "Managing Enabled Content",
                    Content = "How to combine Mods, Maps, and Patches into a single playable profile.",
                    Type = InfoCardType.Concept,
                    IconKey = "FileDocumentMultiple",
                    IsExpandable = true,
                    DetailedContent = """
                    **The 'Enabled Content' System:**
                    GenHub allows you to mix and match content. A profile is just a list of "Enabled" items that get loaded when you click Launch.

                    **How to Enable Content:**
                    1.  Click the **Edit (Pencil)** button on a Profile Card.
                    2.  Go to the **Content** tab.
                    3.  On the right (`Available Content`), find your item and click it.
                    4.  It moves to the left (`Enabled Content`).

                    **Real-World Examples:**
                    *   **Example 1: The 'Casino' Profile**
                        *   Add `GenPatcher` (Game Client).
                        *   Add `Map Pack: Casino Maps` (Collection of maps).
                        *   Result: A clean game that only loads your casino maps.

                    *   **Example 2: 'AOD' (Art of Defense)**
                        *   Add `GenPatcher` (Game Client).
                        *   Add `Map: The Super Cherka` (Specific AOD map).
                        *   Add `Script: AOD Balance Fix` (Hypothetical patch).
                        *   Result: Optimized setup for AOD lobbies.

                    *   **Example 3: 'Generals Online'**
                        *   Add `Generals Online` (Game Client/Wrapper).
                        *   Add `Community Patch 1.06` (Patch).
                        *   Result: Ready for competitive online play.
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
            Description = "Configure resolution, audio, and network options.",
            IconKey = "Tune",
            Order = 1,
            Cards =
            [
                new InfoCard
                {
                    Title = "Safe Options Editing",
                    Content = "Edit resolution, camera height, and more without risking game crashes.",
                    Type = InfoCardType.HowTo,
                    IconKey = "Tune",
                    IsExpandable = true,
                    DetailedContent = """
                    **How it works:**
                    GenHub creates a unique `Options.ini` for every profile. Your base game's settings are never touched!

                    **Key Settings Explained:**

                    1.  **Resolution:**
                        -   Forces custom resolutions (e.g., 2560x1440) directly into the .ini.
                        -   *Pro Tip:* High resolutions in Generals require a 'Camera Height' adjustment to avoid being too zoomed in.

                    2.  **Camera Height:**
                        -   Standard is `312.0`. Increasing this to `500.0` or `600.0` provides a modern widescreen view.
                        -   GenHub ensures the value is formatted correctly for the game engine.

                    3.  **Experimental Features:**
                        -   **Particle Cap:** Increase this to prevent smoke and explosions from disappearing in large battles.
                        -   **Texture Resolution:** Force the highest quality textures.

                    **Accessing:**
                    Click the **Gear Icon** on any Profile Card.
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
            Title = "GameProfile Content",
            Description = "Manage enabled mods, maps, and patches.",
            IconKey = "LayersThree",
            Order = 2,
            Cards =
            [
                new InfoCard
                {
                    Title = "The Content Selection Interface",
                    Content = "Learn how to use the Profile Editor to combine different types of content.",
                    Type = InfoCardType.HowTo,
                    IconKey = "LayersThree",
                    IsExpandable = true,
                    DetailedContent = """
                    **The Two-Pane System:**

                    1.  **Enabled Content (Left):**
                        -   These are the items currently part of your profile.
                        -   **Rule:** You must have exactly ONE **Game Installation** or **Game Client** enabled to launch.
                        -   **Action:** Click the 'X' (or the card itself) to disable an item and move it back to Available.

                    2.  **Add Content (Right):**
                        -   These are items you own but hasn't been added to this profile yet.
                        -   **Action:** Click the '+' (or the card itself) to enable an item.

                    **Smart Filtering:**

                    -   **Content Type Pills:** Quickly switch between viewing Mods, Maps, Patches, etc.
                    -   **Game Filter Toggle:** Switch between "Generals" and "Zero Hour" to find content compatible with your intended game version.
                    -   **Search / Refresh:** Use the refresh icon if you just manually added files to your mod folders while the app was open.
                    """,
                },
                new InfoCard
                {
                    Title = "Importing Local Content",
                    Content = "How to add mods or maps that you downloaded manually.",
                    Type = InfoCardType.HowTo,
                    IconKey = "FolderPlus",
                    IsExpandable = true,
                    DetailedContent = """
                    **The 'Add Local' Button:**

                    Located in the Profile Editor -> Content tab, this button allows you to import any folder as a GenHub managed item.

                    **Steps to Import:**
                    1.  **Name:** Give it a friendly name (e.g. "ShockWave 1.2").
                    2.  **Path:** Point to the folder where the mod files (`.big`, etc.) are located.
                    3.  **Type:** Tell GenHub if it's a Mod, Map, or Patch. This determines where it shows up in filters.
                    4.  **Game:** Select if it's for Generals or Zero Hour.

                    **Why use this?**
                    Importing via 'Add Local' lets you keep your mod files anywhere on your PC while keeping the game installation clean. GenHub will link them automatically when you launch.
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
            Description = "Launch profiles directly from your desktop.",
            IconKey = "RocketLaunch",
            Order = 2,
            Cards =
            [
                new InfoCard
                {
                    Title = "Creating a Shortcut",
                    Content = "Skip the GenHub UI and launch straight into the game.",
                    Type = InfoCardType.HowTo,
                    IconKey = "MonitorDashboard",
                    IsExpandable = true,
                    DetailedContent = """
                    **How to create:**
                    1.  Find the **Profile Card** you want to shortcut.
                    2.  **Right-Click** anywhere on the card background.
                    3.  Select **Create Desktop Shortcut**.
                    4.  A new icon appears on your Windows Desktop.

                    **What it looks like:**
                    -   **Icon:** Uses the game icon or the custom icon you assigned to the profile.
                    -   **Name:** Takes the profile name (e.g., "RotR 1.87").
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
            Description = "Track hours and use the Overlay.",
            IconKey = "Steam",
            Order = 3,
            Cards =
            [
                new InfoCard
                {
                    Title = "The Steam Link Feature",
                    Content = "GenHub acts as a bridge. It tells Steam 'The user is playing Generals' so your friends can see it and hours are counted.",
                    Type = InfoCardType.Concept,
                    IconKey = "Link",
                    IsExpandable = true,
                    DetailedContent = """
                    **How to Enable:**
                    1.  Look at any **Profile Card**.
                    2.  Find the small **Steam Logo** button (usually next to Edit/Settings).
                    3.  **Click it.**
                        *   **Gray:** Off.
                        *   **Blue/Colored:** On.

                    **Prerequisites:**
                    -   You theoretically need to own the game on Steam (App ID: 24800).

                    """,
                },
            ],
        };
    }

    private static InfoSection CreateLocalContentSection() // Renamed from CreateContentSection
    {
        return new InfoSection
        {
            Id = "local-content",
            Title = "Local Content",
            Description = "Manage your downloaded mods, maps, and patches.",
            IconKey = "FolderZip",
            Order = 4,
            Cards =
            [
                new InfoCard
                {
                    Title = "Understanding Content Types",
                    Content = "GenHub supports multiple content types, each serving a specific purpose in your game setup.",
                    Type = InfoCardType.Concept,
                    IconKey = "FileDocumentMultiple",
                    IsExpandable = true,
                    DetailedContent = """
                    **Game Client (Executable):**
                    -   **What it is:** The actual game executable (e.g., `generals.exe`, `game.dat`).
                    -   **Examples:** Zero Hour v1.04, Generals Online, GenPatcher.
                    -   **Required:** Every profile MUST have exactly one Game Client enabled to launch.
                    -   **Icon in UI:** Game controller or disc icon.

                    **Mods:**
                    -   **What it is:** Total conversion or major gameplay overhauls.
                    -   **Examples:** Rise of the Reds, ShockWave, Contra, The End of Days.
                    -   **File Format:** Usually `.big` files or folders with custom assets.
                    -   **Icon in UI:** Puzzle piece icon.
                    -   **Note:** Mods often replace the entire game experience.

                    **Maps:**
                    -   **What it is:** Individual custom maps for skirmish or multiplayer.
                    -   **Examples:** Tournament Desert II, Twilight Flame Optimized.
                    -   **File Format:** `.map` files or folders containing map data.
                    -   **Icon in UI:** Map marker icon.
                    -   **Managed via:** Map Manager tool (see Tools section).

                    **Map Packs:**
                    -   **What it is:** Collections of maps grouped together.
                    -   **Examples:** "Ranked 1v1 Maps 2025", "Co-Op Mission Maps".
                    -   **Purpose:** Organize maps by category (competitive, casual, etc.).
                    -   **Icon in UI:** Layered map icon.
                    -   **How to Create:** Use the Map Manager to select maps and create a pack.

                    **Addons:**
                    -   **What it is:** Cosmetic or audio enhancements that don't change gameplay.
                    -   **Examples:** Modern GUI Overlay, HD Sound Effects, Music Packs.
                    -   **File Format:** `.big` files, audio files, or UI assets.
                    -   **Icon in UI:** Plus icon or addon symbol.
                    -   **Compatibility:** Can usually be combined with any mod.

                    **Patches:**
                    -   **What it is:** Small modifications, fixes, or enhancements.
                    -   **Examples:** GenTool, ControlBar Pro, 4GB Patch, Community Patch 1.06.
                    -   **File Format:** `.big` files, `.dll` injections, or script files.
                    -   **Icon in UI:** Code brackets icon.
                    -   **Applied to:** Base game or specific mods.

                    **Modding Tools:**
                    -   **What it is:** Utilities for creating or editing game content.
                    -   **Examples:** World Builder, FinalBig, GenPatcher.
                    -   **Purpose:** Map creation, asset extraction, game modification.
                    -   **Icon in UI:** Wrench/gear icon.
                    -   **Note:** These are not enabled in game profiles, but can be launched separately.
                    """,
                },
                new InfoCard
                {
                    Title = "Adding New Content",
                    Content = "How to import your downloaded content into GenHub.",
                    Type = InfoCardType.HowTo,
                    IconKey = "PlusBox",
                    IsExpandable = true,
                    DetailedContent = """
                    **Method 1: Automatic Detection**
                    -   GenHub scans specific folders for new content on startup.
                    -   **For Mods:** Place in `Documents\Command and Conquer Generals Zero Hour Data\Mods`.
                    -   **For Maps:** Place in `Documents\Command and Conquer Generals Zero Hour Data\Maps`.
                    -   **Restart GenHub** to detect new content.

                    **Method 2: Manual Import (Recommended)**
                    1.  Click the **"Add Local"** button in the Content Editor (shown in the demo above).
                    2.  **Name:** Enter a descriptive name (e.g., "Rise of the Reds 1.87").
                    3.  **Browse:** Select the folder containing your content.
                    4.  **Content Type:** Choose the appropriate type (Mod, Map, Addon, etc.).
                    5.  **Game Type:** Select Generals or Zero Hour.
                    6.  Click **"Add"** to import.

                    **Supported Formats:**
                    -   **Folders:** Any folder containing game files.
                    -   **Archives:** `.zip`, `.rar`, `.7z` (extract first, then import the folder).
                    -   **Big Files:** `.big` files (place in a folder, then import the folder).

                    **Important Notes:**
                    -   Always download content from **trusted sources** (e.g., ModDB, official mod websites).
                    -   Some mods require specific game versions (e.g., Zero Hour 1.04).
                    -   Read the mod's installation instructions for any special requirements.
                    """,
                    Actions =
                    [
                        new InfoAction { Label = "Go to Local Content", ActionId = InfoNavigationActions.NavigateToLocalContent, IconKey = "FolderOpen" },
                    ],
                },
                new InfoCard
                {
                    Title = "Using Content with Game Profiles",
                    Content = "How to enable and manage content in your game profiles.",
                    Type = InfoCardType.HowTo,
                    IconKey = "ControllerClassic",
                    IsExpandable = true,
                    DetailedContent = """
                    **The Profile + Content System:**
                    A Game Profile is essentially a "playlist" of content. When you click Launch, GenHub loads only the content you've enabled for that profile.

                    **How to Enable Content:**
                    1.  Go to **Game Profiles** tab.
                    2.  Click the **Edit (Pencil)** button on any profile card.
                    3.  Click the **Content (Box)** tab at the top.
                    4.  On the right side, you'll see **"Add Content"** with filter buttons.
                    5.  Click a filter (e.g., "Mods") to see available mods.
                    6.  Click any item to move it to **"Enabled Content"** on the left.
                    7.  Click **Save** to apply changes.

                    **Understanding the Content Editor UI:**
                    -   **Left Side (Enabled Content):** Items that WILL be loaded when you launch this profile.
                    -   **Right Side (Add Content):** Items available to add.
                    -   **Filter Buttons:** Click to switch between content types (Games, Mods, Maps, etc.).
                    -   **Game Type Filters:** "Generals" and "Zero Hour" buttons filter content by game version.
                    -   **Add Local Button:** Import new content from your PC.
                    -   **Refresh Button:** Reload the content list (useful after downloading new content).

                    **Real-World Example Workflows:**

                    **Example 1: "Vanilla Zero Hour" Profile**
                    -   **Enabled Content:** Zero Hour v1.04 (Game Client).
                    -   **Result:** Clean, unmodded game.

                    **Example 2: "Rise of the Reds" Profile**
                    -   **Enabled Content:**
                        1.  Zero Hour v1.04 (Game Client).
                        2.  Rise of the Reds 1.87 (Mod).
                        3.  GenTool v8.9 (Patch - for enhanced features).
                    -   **Result:** Full RotR experience with GenTool enhancements.

                    **Example 3: "Competitive 1v1" Profile**
                    -   **Enabled Content:**
                        1.  Generals Online (Game Client).
                        2.  Ranked 1v1 Maps 2025 (Map Pack).
                        3.  Community Patch 1.06 (Patch).
                    -   **Result:** Optimized setup for online ranked play with only competitive maps.

                    **Example 4: "Modded + Custom Maps" Profile**
                    -   **Enabled Content:**
                        1.  Zero Hour v1.04 (Game Client).
                        2.  ShockWave 1.201 (Mod).
                        3.  Naval Wars Map Pack (Map Pack).
                        4.  HD Sound Effects (Addon).
                    -   **Result:** ShockWave mod with custom maps and improved audio.

                    **Pro Tips:**
                    -   **Multiple Profiles:** Create separate profiles for different mods/setups.
                    -   **Map Packs:** Use Map Packs to avoid cluttering your in-game map list.
                    -   **Workspace Strategy:** If you experience issues, try changing the Workspace Strategy in profile settings (Advanced tab).
                    -   **Command Line Args:** Add custom launch arguments in the Advanced tab (e.g., `-quickstart` to skip intro videos).
                    """,
                    Actions =
                    [
                        new InfoAction { Label = "Create a New Profile", ActionId = InfoNavigationActions.NavigateToGameProfiles, IconKey = "Plus", IsPrimary = true },
                    ],
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
            Description = "Master the built-in Replay and Map managers.",
            IconKey = "Tools",
            Order = 5,
            Cards = [],
        };
    }

    private static InfoSection CreateScanForGamesSection()
    {
        return new InfoSection
        {
            Id = "scan-games",
            Title = "Game Detection",
            Description = "How GenHub finds your installation.",
            IconKey = "Radar",
            Order = 6,
            Cards =
            [
                new InfoCard
                {
                    Title = "Automatic Scanning",
                    Content = "GenHub searches these locations on startup:",
                    Type = InfoCardType.Concept,
                    IconKey = "Magnify",
                    IsExpandable = true,
                    DetailedContent = """
                    1.  **Registry Keys:** `HKLM\Software\EA Games\Command and Conquer Generals Zero Hour`
                    2.  **Steam Library:** Standard `steamapps\common` folders.
                    3.  **Default Paths:** `C:\Program Files (x86)\EA Games\...`

                    **Status Indicators:**
                    -   **Green Check:** Game found and verified valid.
                    -   **Red X:** Game executable missing.
                    """,
                },
                new InfoCard
                {
                    Title = "Manual Location",
                    Content = "If you have a portable version or custom install location.",
                    Type = InfoCardType.HowTo,
                    IconKey = "FolderSearch",
                    IsExpandable = true,
                    DetailedContent = """
                    1.  Go to **Settings**.
                    2.  Under **Game Installation Path**, click **Browse**.
                    3.  Select the folder containing `generals.exe`.
                    4.  GenHub will validate the file immediately.
                    """,
                    Actions =
                    [
                        new InfoAction { Label = "Go to Settings", ActionId = InfoNavigationActions.NavigateToSettings, IconKey = "Cog" },
                    ],
                },
            ],
        };
    }

    private static InfoSection CreateWorkspaceSection()
    {
        return new InfoSection
        {
            Id = "workspaces",
            Title = "File System Magic",
            Description = "Deep dive into how GenHub keeps mods isolated.",
            IconKey = "Harddisk",
            Order = 7,
            Cards =
            [
                new InfoCard
                {
                    Title = "The 'Workspace' Concept",
                    Content = "GenHub never modifes your original game folder. It builds a formatted 'Virtual' folder for every run.",
                    Type = InfoCardType.Concept,
                    IconKey = "LayersThree",
                    IsExpandable = true,
                    DetailedContent = """
                    **The Process:**
                    1.  **Game Files:** We create `Symbolic Links` to your clean game data. (Takes 0ms, 0 bytes).
                    2.  **Mod Files:** We inject real mod files on top.
                    3.  **User Data:** We redirect `Data\INI` and other loose files to the virtual folder.

                    **Why?**
                    -   You can run Mod A and Mod B at the same time.
                    -   If a Mod breaks the game, you just delete the profile. Your base game is safe.
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
            Description = "Keeping GenHub up to date.",
            IconKey = "Update",
            Order = 8,
            Cards = [],
        };
    }

    private static InfoSection CreateChangelogSection()
    {
        return new InfoSection
        {
            Id = "changelogs",
            Title = "Changelog",
            Description = "See what's new in every version.",
            IconKey = "History",
            Order = 9,
            Cards = [],
        };
    }
}
