using System.Globalization;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Core.Services.Providers.VersionSchemes;

/// <summary>
/// Calendar-date versions, separated ("2025-11-07", "2025/11/07") or compact ("20251107").
/// </summary>
public sealed class IsoDateVersionScheme : VersionSchemeBase
{
    private const string SeparatedFormat = "yyyy-MM-dd";
    private const string CompactFormat = "yyyyMMdd";

    private static readonly char[] Separators = ['-', '/', '.'];

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

        if (!DateTime.TryParseExact(version, SeparatedFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            var compact = string.Concat(version.Split(Separators, StringSplitOptions.RemoveEmptyEntries));

            if (!DateTime.TryParseExact(compact, CompactFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return false;
            }
        }

        result = new ContentVersion(date.Year, date.Month, date.Day);
        return true;
    }
}
