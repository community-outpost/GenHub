using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// One player's full script content: loose scripts plus groups.
/// </summary>
public sealed class ScriptListModel
{
    /// <summary>Gets the loose scripts.</summary>
    public List<ScriptModel> Scripts { get; } = [];

    /// <summary>Gets the script groups.</summary>
    public List<ScriptGroupModel> Groups { get; } = [];
}
