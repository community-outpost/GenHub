using System;
using System.Collections.Generic;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.CommunityOutpost;

/// <summary>
/// Registry that maps GenPatcher 4-character content codes to GenHub content metadata.
/// Based on the dl.dat format from GenPatcher (legi.cc/gp2/dl.dat).
/// </summary>
public static class GenPatcherContentRegistry
{
    /// <summary>
    /// Language code mappings for patch suffixes.
    /// </summary>
    private static readonly Dictionary<char, (string Code, string Name)> LanguageSuffixes = new()
    {
        ['e'] = ("en", "English"),
        ['b'] = ("pt-BR", "Brazilian Portuguese"),
        ['c'] = ("zh", "Chinese"),
        ['d'] = ("de", "German"),
        ['f'] = ("fr", "French"),
        ['i'] = ("it", "Italian"),
        ['k'] = ("ko", "Korean"),
        ['p'] = ("pl", "Polish"),
        ['s'] = ("es", "Spanish"),
        ['2'] = ("de-alt", "German (Alternate)"),
    };

    /// <summary>
    /// Shared resolution variants for high-resolution control bars.
    /// </summary>
    private static readonly List<ContentVariant> ResolutionVariants =
    [
        new ContentVariant { Id = "720p", Name = "720p", VariantType = "resolution", Value = "720", IncludePatterns = ["*720*"], ExcludePatterns = ["*900*", "*1080*", "*1440*", "*2160*"], IsDefault = false },
        new ContentVariant { Id = "900p", Name = "900p", VariantType = "resolution", Value = "900", IncludePatterns = ["*900*"], ExcludePatterns = ["*720*", "*1080*", "*1440*", "*2160*"], IsDefault = false },
        new ContentVariant { Id = "1080p", Name = "1080p (Recommended)", VariantType = "resolution", Value = "1080", IncludePatterns = ["*1080*"], ExcludePatterns = ["*720*", "*900*", "*1440*", "*2160*"], IsDefault = true },
        new ContentVariant { Id = "1440p", Name = "1440p (2K)", VariantType = "resolution", Value = "1440", IncludePatterns = ["*1440*"], ExcludePatterns = ["*720*", "*900*", "*1080*", "*2160*"], IsDefault = false },
        new ContentVariant { Id = "2160p", Name = "2160p (4K)", VariantType = "resolution", Value = "2160", IncludePatterns = ["*2160*"], ExcludePatterns = ["*720*", "*900*", "*1080*", "*1440*"], IsDefault = false },
    ];

    /// <summary>
    /// Static content metadata for known content codes.
    /// </summary>
    private static readonly Dictionary<string, GenPatcherContentMetadata> KnownContent = new(StringComparer.OrdinalIgnoreCase)
    {
        // Community Patch (TheSuperHackers Build from legi.cc/patch)
        ["community-patch"] = new GenPatcherContentMetadata
        {
            ContentCode = "community-patch",
            DisplayName = "Community Patch (TheSuperHackers Build)",
            Description = "The latest TheSuperHackers patch build for Zero Hour. Includes bug fixes, balance changes, and quality of life improvements.",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.CommunityPatch,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Base Game Files - These are the patched versions (1.08 for Generals, 1.04 for Zero Hour)
        // The GenPatcher dl.dat codes "10gn" and "10zh" represent the latest patched game clients
        ["10gn"] = new GenPatcherContentMetadata
        {
            ContentCode = "10gn",
            DisplayName = "Generals 1.08",
            Description = "Generals game client (Version 1.08) - the latest official patch version",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            Version = "1.08",
            Category = GenPatcherContentCategory.BaseGame,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["10zh"] = new GenPatcherContentMetadata
        {
            ContentCode = "10zh",
            DisplayName = "Zero Hour 1.04",
            Description = "Zero Hour game client (Version 1.04) - the latest official patch version",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "1.04",
            Category = GenPatcherContentCategory.BaseGame,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Control Bar Addons
        ["cbbs"] = new GenPatcherContentMetadata
        {
            ContentCode = "cbbs",
            DisplayName = "Control Bar HD (Base)",
            Description = "High resolution UI textures for the control bar. Required for all HD and Pro control bars.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.ControlBar,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "400_ControlBarHDBaseZH.big",
            IsBaseDependency = true,
        },
        ["cben"] = new GenPatcherContentMetadata
        {
            ContentCode = "cben",
            DisplayName = "Control Bar HD (Language)",
            Description = "Language-specific UI strings and tooltips for the HD control bar.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.ControlBar,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "400_ControlBarHDEnglishZH.big",
            IsBaseDependency = true,
        },
        ["cbpc"] = new GenPatcherContentMetadata
        {
            ContentCode = "cbpc",
            DisplayName = "Control Bar Pro (Core)",
            Description = "Core files required for Pro ExiLe and Pro Xezon control bars.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.ControlBar,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "400_ControlBarProCoreZH.big",
            IsBaseDependency = true,
        },
        ["cbpr"] = new GenPatcherContentMetadata
        {
            ContentCode = "cbpr",
            DisplayName = "Control Bar Pro (ExiLe)",
            Description = "Created by ExiLe. High transparency, modern look, widescreen compatible. Requires GenTool.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.ControlBar,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "340_ControlBarPro{variant}ZH.big",
            SupportsVariants = true,
            Variants = ResolutionVariants,
        },
        ["cbpx"] = new GenPatcherContentMetadata
        {
            ContentCode = "cbpx",
            DisplayName = "Control Bar Pro (Xezon)",
            Description = "Created by FAS & xezon. Modern, compact layout, widescreen compatible. Requires GenTool.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.ControlBar,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "340_ControlBarPro{variant}ZH.big",
            SupportsVariants = true,
            Variants = ResolutionVariants,
        },

        // Camera Modifications
        ["crgn"] = new GenPatcherContentMetadata
        {
            ContentCode = "crgn",
            DisplayName = "Camera Mod - Generals",
            Description = "Camera modification for Generals",
            ContentType = ContentType.Addon,
            TargetGame = GameType.Generals,
            Category = GenPatcherContentCategory.Camera,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["crzh"] = new GenPatcherContentMetadata
        {
            ContentCode = "crzh",
            DisplayName = "Camera Mod - Zero Hour",
            Description = "Camera modification for Zero Hour",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Camera,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["dczh"] = new GenPatcherContentMetadata
        {
            ContentCode = "dczh",
            DisplayName = "D-Control - Zero Hour",
            Description = "D-Control camera for Zero Hour",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Camera,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Hotkeys
        ["ewba"] = new GenPatcherContentMetadata
        {
            ContentCode = "ewba",
            DisplayName = "Easy Win Hotkeys (Advanced)",
            Description = "Advanced hotkey configuration for competitive play.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Hotkeys,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "!HotkeysEasyWinAdvancedZH.big",
        },
        ["ewbi"] = new GenPatcherContentMetadata
        {
            ContentCode = "ewbi",
            DisplayName = "Easy Win Hotkeys (International)",
            Description = "Standard hotkey layout optimized for non-English keyboards.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Hotkeys,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "!HotkeysEasyWinInternationalZH.big",
        },
        ["hlde"] = new GenPatcherContentMetadata
        {
            ContentCode = "hlde",
            DisplayName = "Standard Hotkeys (German)",
            Description = "German hotkey configuration for Zero Hour.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            LanguageCode = "de",
            Category = GenPatcherContentCategory.Hotkeys,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "!HotkeysGermanZH.big",
        },
        ["hleg"] = new GenPatcherContentMetadata
        {
            ContentCode = "hleg",
            DisplayName = "Legionnaire's Hotkeys",
            Description = "A grid-based hotkey layout (QWERTY) that is easy to learn for modern RTS players.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            LanguageCode = "en",
            Category = GenPatcherContentCategory.Hotkeys,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "!HotkeysLegionnaireZH.big",
        },
        ["hlei"] = new GenPatcherContentMetadata
        {
            ContentCode = "hlei",
            DisplayName = "Leikeze's Hotkeys",
            Description = "Highly recommended hotkey preset. Balanced for efficiency and ease of use.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            LanguageCode = "en",
            Category = GenPatcherContentCategory.Hotkeys,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "!HotkeysLeikezeZH.big",
        },
        ["hlen"] = new GenPatcherContentMetadata
        {
            ContentCode = "hlen",
            DisplayName = "Hotkeys Indicators (Leikeze/Legionnaire)",
            Description = "Control bar overlay icons for Leikeze's and Legionnaire's hotkeys.",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            LanguageCode = "en",
            Category = GenPatcherContentCategory.Hotkeys,
            InstallTarget = ContentInstallTarget.Workspace,
            RequiresRepacking = true,
            OutputFilename = "!HotkeysLeikezeIndicatorsZH.big",
            IsBaseDependency = true,
        },

        // Tools
        ["gent"] = new GenPatcherContentMetadata
        {
            ContentCode = "gent",
            DisplayName = "GenTool",
            Description = "GenTool utility for Generals/Zero Hour",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Tools,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        ["gena"] = new GenPatcherContentMetadata
        {
            ContentCode = "gena",
            DisplayName = "GenAssist",
            Description = "GenAssist helper utility",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Tools,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Maps
        ["maod"] = new GenPatcherContentMetadata
        {
            ContentCode = "maod",
            DisplayName = "Maps (Art of Defense)",
            Description = "AOD is Art of Defense, similar to Tower Defense, but for Zero Hour. Includes popular maps like Demilitarized Zone, Extreme Circle, and Super V.",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Maps,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["mmis"] = new GenPatcherContentMetadata
        {
            ContentCode = "mmis",
            DisplayName = "Custom Missions Pack",
            Description = "Single player and multiplayer co-op missions. Includes Operation Kihill Beach, Iranian Counterstrike, TKLyo's USA Campaign, and more.",
            ContentType = ContentType.Mission,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Maps,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["mscr"] = new GenPatcherContentMetadata
        {
            ContentCode = "mscr",
            DisplayName = "Map Scripting Resources",
            Description = "Special modded (scripted) and no-money maps like Battle Royale and Rebel Uprise. Note: Most do not work with AI.",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Maps,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["mskr"] = new GenPatcherContentMetadata
        {
            ContentCode = "mskr",
            DisplayName = "Skirmish Map Pack",
            Description = "High quality 1v1, 2v2, 3v3, 4v4 and FFA maps. Includes World Builder Contest maps, Combat-Island, Defcon 51, and more.",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Maps,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Visuals
        ["icon"] = new GenPatcherContentMetadata
        {
            ContentCode = "icon",
            DisplayName = "Icons Pack",
            Description = "Custom icons for the game",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Visuals,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["drtx"] = new GenPatcherContentMetadata
        {
            ContentCode = "drtx",
            DisplayName = "DirectX Textures",
            Description = "High-resolution DirectX texture pack",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Visuals,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["unct"] = new GenPatcherContentMetadata
        {
            ContentCode = "unct",
            DisplayName = "Uncut Content",
            Description = "Restored uncut game content",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Visuals,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Prerequisites - System install
        ["vc05"] = new GenPatcherContentMetadata
        {
            ContentCode = "vc05",
            DisplayName = "VC++ 2005 Redistributable",
            Description = "Microsoft Visual C++ 2005 Redistributable (x86)",
            ContentType = ContentType.Executable,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Prerequisites,
            InstallTarget = ContentInstallTarget.System,
        },
        ["vc08"] = new GenPatcherContentMetadata
        {
            ContentCode = "vc08",
            DisplayName = "VC++ 2008 Redistributable",
            Description = "Microsoft Visual C++ 2008 Redistributable (x86)",
            ContentType = ContentType.Executable,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Prerequisites,
            InstallTarget = ContentInstallTarget.System,
        },
        ["vc10"] = new GenPatcherContentMetadata
        {
            ContentCode = "vc10",
            DisplayName = "VC++ 2010 Redistributable",
            Description = "Microsoft Visual C++ 2010 Redistributable (x86)",
            ContentType = ContentType.Executable,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Prerequisites,
            InstallTarget = ContentInstallTarget.System,
        },
    };

    /// <summary>
    /// Gets metadata for a content code.
    /// </summary>
    /// <param name="contentCode">The 4-character content code.</param>
    /// <returns>Content metadata, or a dynamically generated one if the code is unknown.</returns>
    public static GenPatcherContentMetadata GetMetadata(string contentCode)
    {
        if (string.IsNullOrWhiteSpace(contentCode))
        {
            return CreateUnknownMetadata(contentCode ?? string.Empty);
        }

        var normalizedCode = contentCode.Trim();

        // Check for known content first (case-insensitive due to dictionary comparer)
        if (KnownContent.TryGetValue(normalizedCode, out var metadata))
        {
            return metadata;
        }

        // Try to parse as a patch code (e.g., "108e", "104b")
        var patchMetadata = TryParsePatchCode(normalizedCode);
        if (patchMetadata != null)
        {
            return patchMetadata;
        }

        // Return unknown metadata
        return CreateUnknownMetadata(contentCode);
    }

    /// <summary>
    /// Gets all known content codes.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="string"/> where each element is a known content code.</returns>
    public static IEnumerable<string> GetKnownContentCodes()
    {
        return KnownContent.Keys;
    }

    /// <summary>
    /// Checks if a content code is known.
    /// </summary>
    /// <param name="contentCode">The content code to check.</param>
    /// <returns>true if the content code is known; otherwise, false.</returns>
    public static bool IsKnownCode(string contentCode)
    {
        return KnownContent.ContainsKey(contentCode.ToLowerInvariant());
    }

    /// <summary>
    /// Tries to parse a content code as a patch code (e.g., "108e" = Patch 1.08 English).
    /// </summary>
    private static GenPatcherContentMetadata? TryParsePatchCode(string code)
    {
        if (code.Length != 4)
        {
            return null;
        }

        // Pattern: [1][version digit][version digit][language char]
        // e.g., 108e = 1.08 English, 104b = 1.04 Brazilian
        if (code[0] != '1')
        {
            return null;
        }

        // Try to parse the version (positions 1-2)
        var versionPart = code.Substring(1, 2);
        if (!int.TryParse(versionPart, out var versionNumber))
        {
            return null;
        }

        // Get the language suffix (position 3)
        var languageSuffix = code[3];
        if (!LanguageSuffixes.TryGetValue(languageSuffix, out var languageInfo))
        {
            return null;
        }

        // Determine target game based on version
        // 108 = Generals 1.08, 104 = Zero Hour 1.04
        var isGenerals = versionNumber == 8; // 1.08 is Generals

        var targetGame = isGenerals ? GameType.Generals : GameType.ZeroHour;
        var version = $"1.0{versionNumber}";

        return new GenPatcherContentMetadata
        {
            ContentCode = code,
            DisplayName = $"Patch {version} ({languageInfo.Name})",
            Description = $"Official {(isGenerals ? "Generals" : "Zero Hour")} patch {version} - {languageInfo.Name} version",
            ContentType = ContentType.Patch,
            TargetGame = targetGame,
            LanguageCode = languageInfo.Code,
            Version = version,
            Category = GenPatcherContentCategory.OfficialPatch,
            InstallTarget = ContentInstallTarget.Workspace,
        };
    }

    /// <summary>
    /// Creates metadata for an unknown content code.
    /// </summary>
    private static GenPatcherContentMetadata CreateUnknownMetadata(string code)
    {
        return new GenPatcherContentMetadata
        {
            ContentCode = code,
            DisplayName = $"Unknown Content ({code})",
            Description = $"GenPatcher content: {code}",
            ContentType = ContentType.UnknownContentType,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Other,
            InstallTarget = ContentInstallTarget.Workspace,
        };
    }
}