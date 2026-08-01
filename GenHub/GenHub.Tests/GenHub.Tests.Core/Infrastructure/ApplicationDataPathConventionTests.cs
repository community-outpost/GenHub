using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure;

/// <summary>
/// Guards the convention that application data paths are resolved through
/// <c>IConfigurationProviderService</c> rather than read directly from the OS.
/// <para>
/// <c>ConfigurationProviderService.GetApplicationDataPath()</c> honours a user-configured
/// <c>UserSettings.ApplicationDataPath</c>. Five separate runtime call sites bypassed it
/// with a raw <c>Environment.GetFolderPath(SpecialFolder.ApplicationData)</c>, so
/// relocating the data directory moved profiles, workspaces and CAS but silently left
/// manifest discovery, provider loading, the Steam patcher and tool workspaces behind in
/// the default tree.
/// </para>
/// <para>
/// The pattern recurred five times independently, which is evidence that code review does
/// not catch it. This test does. If a new legitimate use appears, add it to the allowlist
/// with a comment explaining why it is not a bypass.
/// </para>
/// </summary>
public class ApplicationDataPathConventionTests
{
    private const string ForbiddenPattern = "GetFolderPath(Environment.SpecialFolder.ApplicationData)";

    /// <summary>
    /// Files permitted to read the OS application-data folder directly, with the reason.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // The implementation of the convention itself has to start somewhere.
        ["ConfigurationProviderService.cs"] = "Defines the canonical path.",
        ["UserSettingsService.cs"] = "Loads the settings file that stores the override; cannot depend on it.",

        // Displays the built-in default next to the user's override in the UI.
        ["SettingsViewModel.cs"] = "Computes the factory-default path to show on reset.",

        // Core-layer fallback, overridden at the composition root by ContentPipelineModule.
        ["ProviderDefinitionLoader.cs"] = "Default only; the DI registration supplies an override.",
    };

    /// <summary>
    /// Scans the shared and core projects for direct application-data lookups.
    /// </summary>
    [Fact]
    public void NoUnapprovedDirectApplicationDataLookups()
    {
        var repoRoot = FindRepositoryRoot();
        var searchRoots = new[]
        {
            Path.Combine(repoRoot, "GenHub", "GenHub"),
            Path.Combine(repoRoot, "GenHub", "GenHub.Core"),
        };

        var offenders = searchRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => !Allowed.ContainsKey(Path.GetFileName(path)))
            .Where(path => File.ReadAllText(path).Contains(ForbiddenPattern, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var message =
            $"These files read the OS application-data folder directly:{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders.Select(o => "  " + o))
            + $"{Environment.NewLine}Use IConfigurationProviderService.GetApplicationDataPath() so a user-relocated "
            + "data directory is honoured, or add the file to the allowlist in this test with a reason.";

        Assert.True(offenders.Count == 0, message);
    }

    /// <summary>
    /// Walks up from the test assembly to the directory containing the solution.
    /// </summary>
    /// <returns>The repository root path.</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "GenHub", "GenHub.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root above {AppContext.BaseDirectory}.");
    }
}
