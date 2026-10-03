// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Persisted frame geometry for one WorldBuilder panel or dialog. Mirrors the
/// QT-02 store: position under [QtWindowPositions] and size under [QtWindowSize].
/// </summary>
public sealed record WbWindowGeometry
{
    /// <summary>
    /// Gets or sets the left edge in screen pixels.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the top edge in screen pixels.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets the width in screen pixels.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the height in screen pixels.
    /// </summary>
    public double Height { get; set; }
}
