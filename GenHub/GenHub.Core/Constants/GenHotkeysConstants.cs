namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the GenHotkeys tool.
/// </summary>
public static class GenHotkeysConstants
{
    /// <summary>Tool unique identifier.</summary>
    public const string ToolId = "genhotkeys";

    /// <summary>Tool display name.</summary>
    public const string ToolName = "Hotkeys Editor";

    /// <summary>Tool description.</summary>
    public const string ToolDescription = "Visual hotkey editor for C&C Generals & Zero Hour with icon overlays and direct .big addon integration.";

    /// <summary>Tool plugin version.</summary>
    public const string PluginVersion = "1.0.0";

    /// <summary>Storage directory name for user hotkey profiles.</summary>
    public const string HotkeysStorageDirectory = "Hotkeys";

    /// <summary>Default CommandMap filename.</summary>
    public const string CommandMapFileName = "CommandMap.ini";

    /// <summary>Preset path for English CSF.</summary>
    public const string PresetsLeikezeEn = "Presets/LeikezeEN.csf";

    /// <summary>Preset path for Russian CSF.</summary>
    public const string PresetsLegionnaireRu = "Presets/LegionnaireRU.csf";

    /// <summary>Preset path for CommandMap.ini.</summary>
    public const string PresetsCommandMap = "Presets/CommandMap.ini";

    /// <summary>Tech tree relative path for Generals.</summary>
    public const string TechTreeGenerals = "Profiles/Generals/TechTree.json";

    /// <summary>Tech tree relative path for Zero Hour.</summary>
    public const string TechTreeGeneralsZh = "Profiles/GeneralsZH/TechTree.json";

    /// <summary>Naming format for generated hotkey .big files: !Hotkeys_{Game}_{ProfileName}.big.</summary>
    public const string BigFileNamePattern = "!Hotkeys_{0}_{1}.big";

    /// <summary>Target directory in .big for localized CSF files.</summary>
    public const string DataEnglishDirectory = "Data/English";

    /// <summary>Target directory in .big for overlay TGA textures.</summary>
    public const string ArtTexturesDirectory = "Art/Textures";

    /// <summary>ControlBar CSF key prefix.</summary>
    public const string CsfControlBarPrefix = "CONTROLBAR:";

    /// <summary>Command CSF key prefix.</summary>
    public const string CsfCommandPrefix = "COMMAND:";

    /// <summary>Vanilla preset name.</summary>
    public const string PresetVanilla = "Vanilla";

    /// <summary>Legionnaire preset name.</summary>
    public const string PresetLegionnaire = "Legionnaire";

    /// <summary>Leikeze preset name.</summary>
    public const string PresetLeikeze = "Leikeze";
}
