namespace GenHub.Core.Models.Tools.IniEditor;

/// <summary>
/// The origin of an INI reference index entry.
/// </summary>
public enum IniReferenceSource
{
    /// <summary>
    /// A block from the open document.
    /// </summary>
    Document,

    /// <summary>
    /// A block from another file in the open folder.
    /// </summary>
    Folder,

    /// <summary>
    /// A block from vanilla game data.
    /// </summary>
    Vanilla,
}
