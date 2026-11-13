namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// JSON representation of an action in TechTree.json.
/// </summary>
public class TechTreeActionJson
{
    /// <summary>Gets or sets the icon asset name.</summary>
    public string IconName { get; set; } = string.Empty;

    /// <summary>Gets or sets the CSF string identifier.</summary>
    public string HotkeyString { get; set; } = string.Empty;
}
