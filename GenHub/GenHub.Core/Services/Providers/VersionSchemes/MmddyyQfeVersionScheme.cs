using System.Globalization;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Core.Services.Providers.VersionSchemes;

/// <summary>
/// Generals Online versions: a MMDDYY date, a QFE revision, and any number of trailing
/// build tags. "060526_QFE1", "042826_QFE3_EAC" and "011526_QFE1_EAC_X86" are all valid;
/// the trailing tags identify a build, not a release, so they take no part in ordering.
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

        var segments = version.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
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

        var qfeSegment = segments
            .Skip(1)
            .FirstOrDefault(segment => segment.StartsWith(GeneralsOnlineConstants.QfeMarkerPrefix, StringComparison.OrdinalIgnoreCase));

        if (qfeSegment is null)
        {
            return false;
        }

        var qfeDigits = qfeSegment[GeneralsOnlineConstants.QfeMarkerPrefix.Length..];
        if (!int.TryParse(qfeDigits, NumberStyles.None, CultureInfo.InvariantCulture, out var qfe))
        {
            return false;
        }

        result = new ContentVersion(date.Year, date.Month, date.Day, qfe);
        return true;
    }
}
