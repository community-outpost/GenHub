using Avalonia.Data.Converters;
using GenHub.Core.Models.Providers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Hides a static fallback release when the live upstream preview already contains the
/// same version. Expects the static release version first, the preview release versions
/// second, and whether the selected item tracks upstream third. Returns true (visible)
/// unless the item is upstream-tracked and a normalized version match is found, so the
/// Studio detail panel never shows one upstream release twice.
/// </summary>
public class UpstreamDuplicateReleaseConverter : IMultiValueConverter
{
    /// <summary>
    /// Converts the release version, preview versions, and tracking flag into visibility.
    /// </summary>
    /// <param name="values">The static release version, preview versions, and tracking flag.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>False when the static release duplicates a preview release; otherwise, true.</returns>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 3)
        {
            return true;
        }

        if (values[2] is not true)
        {
            return true;
        }

        var version = NormalizeVersion(values[0] as string);
        if (string.IsNullOrEmpty(version))
        {
            return true;
        }

        var previewVersions = ToVersionList(values[1]);
        if (previewVersions.Count == 0)
        {
            return true;
        }

        if (previewVersions.Any(p => string.Equals(p, version, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var numeric = CatalogManifestIdentity.ExtractVersionNumber(version);
        if (numeric > 0 && previewVersions.Any(p => CatalogManifestIdentity.ExtractVersionNumber(p) == numeric))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        return version.Trim().TrimStart('v', 'V').Trim().ToUpperInvariant();
    }

    private static List<string> ToVersionList(object? value)
    {
        if (value is IEnumerable<string> strings)
        {
            return strings.Where(s => !string.IsNullOrWhiteSpace(s)).Select(NormalizeVersion).ToList();
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            var versions = new List<string>();
            foreach (var entry in enumerable)
            {
                var normalized = NormalizeVersion(entry as string);
                if (!string.IsNullOrEmpty(normalized))
                {
                    versions.Add(normalized);
                }
            }

            return versions;
        }

        return [];
    }
}
