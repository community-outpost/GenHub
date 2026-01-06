using System.Text.RegularExpressions;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper for resolving Steam AppIDs from local installation manifests.
/// </summary>
public static partial class SteamAppIdResolver
{
    /// <summary>
    /// Attempts to resolve the Steam AppID for a game installation by searching for its appmanifest in the Steam library.
    /// </summary>
    /// <param name="installationPath">The absolute path to the game installation directory.</param>
    /// <param name="steamAppId">When this method returns, contains the resolved Steam AppID if successful; otherwise, an empty string.</param>
    /// <returns>True if the AppID was successfully resolved; otherwise, false.</returns>
    public static bool TryResolveSteamAppIdFromInstallationPath(string installationPath, out string steamAppId)
    {
        steamAppId = string.Empty;

        if (string.IsNullOrWhiteSpace(installationPath))
        {
            return false;
        }

        DirectoryInfo? installDirInfo;
        try
        {
            installDirInfo = new DirectoryInfo(installationPath);
        }
        catch
        {
            return false;
        }

        var installDirName = installDirInfo.Name;
        var commonDir = installDirInfo.Parent;
        if (commonDir == null || !commonDir.Name.Equals("common", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var steamAppsDir = commonDir.Parent;
        if (steamAppsDir == null || !steamAppsDir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IEnumerable<string> manifests;
        try
        {
            manifests = Directory.EnumerateFiles(steamAppsDir.FullName, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return false;
        }

        foreach (var manifestPath in manifests)
        {
            string raw;
            try
            {
                raw = File.ReadAllText(manifestPath);
            }
            catch
            {
                continue;
            }

            var match = InstallDirRegex().Match(raw);
            if (!match.Success)
            {
                continue;
            }

            var manifestInstallDir = match.Groups["dir"].Value;
            if (!manifestInstallDir.Equals(installDirName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var baseName = Path.GetFileNameWithoutExtension(manifestPath);
            var idMatch = AppManifestRegex().Match(baseName);
            if (!idMatch.Success)
            {
                continue;
            }

            steamAppId = idMatch.Groups["id"].Value;
            return !string.IsNullOrWhiteSpace(steamAppId);
        }

        return false;
    }

    [GeneratedRegex("\"installdir\"\\s+\"(?<dir>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirRegex();

    [GeneratedRegex("^appmanifest_(?<id>\\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AppManifestRegex();
}
