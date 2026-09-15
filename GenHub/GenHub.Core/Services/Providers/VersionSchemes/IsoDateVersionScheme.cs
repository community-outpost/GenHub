using System.Globalization;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Core.Services.Providers.VersionSchemes;

/// <summary>
/// Calendar-date versions, separated ("2025-11-07", "2025/11/07", "2025.11.07")
/// or compact ("20251107").
/// </summary>
public sealed class IsoDateVersionScheme : VersionSchemeBase
{
    private static readonly string[] SupportedFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyy.MM.dd",
        "yyyyMMdd",
    ];

    /// <inheritdoc/>
    public override string SchemeId => VersionSchemeConstants.IsoDate;

    /// <inheritdoc/>
    public override bool TryParse(string? version, out ContentVersion result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                version,
                SupportedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return false;
        }

        result = new ContentVersion(date.Year, date.Month, date.Day);
        return true;
    }
}
