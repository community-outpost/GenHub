using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Constants;

/// <summary>
/// Installation source type identifiers for game installations.
/// These constants represent WHERE the game was installed from (Steam, EA App, Retail, etc.).
/// </summary>
/// <remarks>
/// IMPORTANT: These are NOT content publishers. Publishers are discovered dynamically via IContentProvider.
/// Content providers register themselves with the ContentOrchestrator and can come from anywhere:
/// - Official sources (EA/Steam detected installations)
/// - Community platforms (GitHub, ModDB, HTTP endpoints)
/// - Custom sources (any IContentProvider implementation)
///
/// This class ONLY contains constants for game installation detection and GameInstallationType mapping.
/// For content publisher information, see IContentProvider.SourceName property.
/// </remarks>
public static class InstallationSourceConstants
{
    /// <summary>EA App (formerly Origin) platform installation.</summary>
    public const string EaApp = "eaapp";

    /// <summary>Steam platform installation.</summary>
    public const string Steam = "steam";

    /// <summary>Retail/physical installation.</summary>
    public const string Retail = "retail";

    /// <summary>The First Decade compilation installation.</summary>
    public const string TheFirstDecade = "thefirstdecade";

    /// <summary>Wine/Proton compatibility layer installation.</summary>
    public const string Wine = "wine";

    /// <summary>CD-ROM/ISO installation.</summary>
    public const string CdIso = "cdiso";

    /// <summary>Origin platform installation (deprecated, use EaApp).</summary>
    public const string Origin = "origin";

    /// <summary>RG Mechanics platform installation.</summary>
    public const string RgMechanics = "rgmechanics";

    /// <summary>Unknown or unspecified installation source.</summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// Gets all installation source type identifiers.
    /// </summary>
    public static HashSet<string> AllInstallationTypes => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EaApp,
        Steam,
        Retail,
        TheFirstDecade,
        Wine,
        CdIso,
        Unknown,
        Origin,
        RgMechanics,
    };

    /// <summary>
    /// Maps GameInstallationType enum to installation source string.
    /// </summary>
    /// <param name="installationType">The game installation type to convert.</param>
    /// <returns>The corresponding installation source identifier string.</returns>
    public static string FromInstallationType(GameInstallationType installationType)
    {
        return installationType.ToInstallationSourceString();
    }
}