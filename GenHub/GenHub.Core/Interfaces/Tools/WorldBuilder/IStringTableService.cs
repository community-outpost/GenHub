// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// String-table service (TheGameText): compiled CSF plus text STR sources backing
/// DisplayName resolution. STR labels override same-named CSF labels; per-map
/// map.str labels override both.
/// </summary>
public interface IStringTableService
{
    /// <summary>
    /// Gets the number of loaded labels.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets all loaded labels ordered case-insensitively (LOCALIZED_TEXT picker source).
    /// </summary>
    /// <returns>The labels in case-insensitive order.</returns>
    IReadOnlyList<string> GetLabels();

    /// <summary>
    /// Loads the language CSF plus the text STR through the asset file system,
    /// replacing any previously loaded game (non-map) strings.
    /// </summary>
    /// <param name="fileSystem">The mounted game asset file system to read through.</param>
    /// <param name="language">The language directory name; null selects the default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The load report.</returns>
    Task<OperationResult<StringTableLoadReport>> LoadAsync(IGameAssetFileSystem fileSystem, string? language = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overlays a per-map map.str file over the loaded strings.
    /// </summary>
    /// <param name="mapStrPath">The physical map.str path beside the open map.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The overlay report (map counts only).</returns>
    Task<OperationResult<StringTableLoadReport>> LoadMapStringsAsync(string mapStrPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the string value by its label.
    /// </summary>
    /// <param name="label">The label name.</param>
    /// <returns>The value or empty string if not found.</returns>
    string GetString(string label);

    /// <summary>
    /// Tries to get the string value by its label.
    /// </summary>
    /// <param name="label">The label name.</param>
    /// <param name="value">The value when found.</param>
    /// <returns>True when the label exists.</returns>
    bool TryGetString(string label, out string value);

    /// <summary>
    /// Resolves a raw DisplayName INI value: strips the LABEL: prefix, looks the key
    /// up in the string table, and falls back to the template name when the value is
    /// missing or unresolvable.
    /// </summary>
    /// <param name="rawDisplayName">The raw DisplayName INI value.</param>
    /// <param name="fallbackName">The template name used when resolution fails.</param>
    /// <returns>The display label.</returns>
    string ResolveLabel(string? rawDisplayName, string fallbackName);
}
