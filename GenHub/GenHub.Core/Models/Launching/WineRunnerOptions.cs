using GenHub.Core.Constants;
using System.IO;

namespace GenHub.Core.Models.Launching;

/// <summary>
/// Platform-specific settings for the Wine compatibility runner.
/// </summary>
/// <param name="BinaryNames">Wine binary names resolved from PATH, in priority order.</param>
/// <param name="AbsoluteBinaryPaths">Absolute runner binary paths checked before PATH lookup.</param>
/// <param name="ExtraSearchDirectories">Additional directories searched for <see cref="BinaryNames"/>.</param>
/// <param name="PrefixPath">Wine prefix used for launches (created on demand).</param>
public sealed record WineRunnerOptions(
    IReadOnlyList<string> BinaryNames,
    IReadOnlyList<string> AbsoluteBinaryPaths,
    IReadOnlyList<string> ExtraSearchDirectories,
    string PrefixPath)
{
    /// <summary>
    /// Creates the Linux Wine runner options for the given app data root.
    /// </summary>
    /// <param name="appDataRoot">The GenHub app data root directory.</param>
    /// <returns>Linux-specific runner options.</returns>
    public static WineRunnerOptions Linux(string appDataRoot)
    {
        return new WineRunnerOptions(
            [WineConstants.WineBinaryName, WineConstants.Wine64BinaryName],
            [],
            [],
            Path.Combine(appDataRoot, WineConstants.ManagedPrefixDirectoryName));
    }

    /// <summary>
    /// Creates the macOS Wine runner options for the given app data root.
    /// </summary>
    /// <param name="appDataRoot">The GenHub app data root directory.</param>
    /// <returns>macOS-specific runner options.</returns>
    public static WineRunnerOptions MacOS(string appDataRoot)
    {
        return new WineRunnerOptions(
            [WineConstants.WineBinaryName, WineConstants.Wine64BinaryName],
            [WineConstants.CrossOverWineBinaryPath],
            [],
            Path.Combine(appDataRoot, WineConstants.ManagedPrefixDirectoryName));
    }
}
