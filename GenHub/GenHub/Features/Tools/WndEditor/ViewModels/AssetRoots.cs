namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// Root directories for game asset discovery.
/// </summary>
/// <param name="TargetGameRoot">The target game root directory.</param>
/// <param name="IsZeroHour">Whether the installation is Zero Hour.</param>
internal sealed record AssetRoots(string TargetGameRoot, bool IsZeroHour)
{
    /// <summary>
    /// Gets the base root path.
    /// </summary>
    public string BaseRoot => TargetGameRoot;
}
