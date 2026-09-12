using System.Globalization;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Core.Services.Providers.VersionSchemes;

/// <summary>
/// Generals Online versions: a MMDDYY date, an optional QFE revision, and any number of trailing
/// build tags. "082826", "060526_QFE1", "042826_QFE3_EAC" and "011526_QFE1_EAC_X86" are all valid;
/// day releases without QFE default to revision 0, and the trailing tags identify a build, not a
/// release, so they take no part in ordering.
/// </summary>
public sealed class MmddyyQfeVersionScheme : VersionSchemeBase
{
    /// <inheritdoc/>
    public override string SchemeId => VersionSchemeConstants.MmddyyQfe;

    /// <inheritdoc/>
    public override bool TryParse(string? version, out ContentVersion result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var segments = version.Split('_', StringSplitOptions.TrimEntries);
        if (segments.Length < 1 || segments.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        var dateSegment = segments[0];
        if (dateSegment.Length != GeneralsOnlineConstants.VersionDateFormat.Length)
        {
            return false;
        }

        // The publisher's two-digit year is explicitly in the 2000-2099 range.
        // DateTime's default two-digit-year cutoff would otherwise reinterpret
        // later releases as dates in the previous century.
        var fourDigitYearDate = $"{dateSegment[..4]}20{dateSegment[4..]}";
        if (!DateTime.TryParseExact(
                fourDigitYearDate,
                "MMddyyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return false;
        }

        var qfe = 0;
        if (segments.Length > 1)
        {
            var qfeSegments = segments
                .Skip(1)
                .Where(segment => segment.StartsWith(
                    GeneralsOnlineConstants.QfeMarkerPrefix,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (qfeSegments.Length > 1)
            {
                return false;
            }

            if (qfeSegments.Length == 1)
            {
                var qfeSegment = qfeSegments[0];
                var qfeDigits = qfeSegment[GeneralsOnlineConstants.QfeMarkerPrefix.Length..];
                if (!int.TryParse(qfeDigits, NumberStyles.None, CultureInfo.InvariantCulture, out qfe))
                {
                    return false;
                }
            }
        }

        result = new ContentVersion(date.Year, date.Month, date.Day, qfe);
        return true;
    }
}
