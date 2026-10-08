using GenHub.Core.Constants;
using System;

namespace GenHub.Features.GameProfiles.Helpers;

/// <summary>
/// Migrates stored cover paths referencing the pre-re-encode PNG faction covers
/// to the current JPEG filenames. Single home for the legacy-name map so profile
/// loading paths cannot drift apart.
/// </summary>
public static class CoverPathMigrationHelper
{
    /// <summary>
    /// Replaces a legacy PNG faction cover filename with its JPEG successor.
    /// </summary>
    /// <param name="path">The stored cover path.</param>
    /// <returns>The path with the current cover filename.</returns>
    public static string MigrateLegacyCoverFilename(string path)
    {
        if (path.Contains(UriConstants.LegacyChinaCoverPngFilename, StringComparison.OrdinalIgnoreCase))
        {
            return path.Replace(UriConstants.LegacyChinaCoverPngFilename, UriConstants.ChinaCoverFilename, StringComparison.OrdinalIgnoreCase);
        }

        if (path.Contains(UriConstants.LegacyUsaCoverPngFilename, StringComparison.OrdinalIgnoreCase))
        {
            return path.Replace(UriConstants.LegacyUsaCoverPngFilename, UriConstants.UsaCoverFilename, StringComparison.OrdinalIgnoreCase);
        }

        if (path.Contains(UriConstants.LegacyGlaCoverPngFilename, StringComparison.OrdinalIgnoreCase))
        {
            return path.Replace(UriConstants.LegacyGlaCoverPngFilename, UriConstants.GlaCoverFilename, StringComparison.OrdinalIgnoreCase);
        }

        return path;
    }
}
