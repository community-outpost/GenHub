using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;

namespace GenHub.Core.Services.Providers.VersionSchemes;

/// <summary>
/// Orders version strings by parsing them into <see cref="ContentVersion"/> components.
/// </summary>
public abstract class VersionSchemeBase : IVersionScheme
{
    /// <inheritdoc/>
    public abstract string SchemeId { get; }

    /// <inheritdoc/>
    public abstract bool TryParse(string? version, out ContentVersion result);

    /// <inheritdoc/>
    public virtual int Compare(string? version1, string? version2)
    {
        var parsed1 = TryParse(version1, out var contentVersion1);
        var parsed2 = TryParse(version2, out var contentVersion2);

        if (parsed1 && parsed2)
        {
            return contentVersion1.CompareTo(contentVersion2);
        }

        // A version this scheme cannot read is treated as older than one it can,
        // so a malformed or "unknown" installed version never suppresses an update.
        if (parsed1)
        {
            return 1;
        }

        if (parsed2)
        {
            return -1;
        }

        return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
    }
}
