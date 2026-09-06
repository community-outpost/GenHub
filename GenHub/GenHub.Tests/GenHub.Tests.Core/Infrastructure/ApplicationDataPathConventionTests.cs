using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure;

/// <summary>
/// Architecture test enforcing that code reads the application data root from
/// <see cref="GenHub.Core.Interfaces.Common.IConfigurationProviderService.GetApplicationDataPath"/>
/// rather than reading <see cref="Environment.SpecialFolder.ApplicationData"/> directly.
/// <para>
/// Reading the OS folder directly bypasses the user-configured data directory
/// override and the portable-mode folder.
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
    /// Uses repository-relative paths with forward slashes.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // The implementation of the convention itself has to start somewhere.
        ["GenHub/GenHub/Common/Services/ConfigurationProviderService.cs"] = "Defines the canonical path.",
        ["GenHub/GenHub/Common/Services/AppConfiguration.cs"] = "Resolves the legacy roaming root the upgrade migration reads from.",
        ["GenHub/GenHub/Common/Services/UserSettingsService.cs"] = "Loads the settings file that stores the override; cannot depend on it.",

        // Displays the built-in default next to the user's override in the UI.
        ["GenHub/GenHub/Features/Settings/ViewModels/SettingsViewModel.cs"] = "Computes the factory-default path to show on reset.",

        // Core-layer fallback, overridden at the composition root by ContentPipelineModule.
        ["GenHub/GenHub.Core/Services/Providers/ProviderDefinitionLoader.cs"] = "Default only; the DI registration supplies an override.",

        // UI image cache service fallback when used outside DI.
        ["GenHub/GenHub/Infrastructure/Services/ImageCacheService.cs"] = "Fallback default path when used outside DI; DI registration injects IConfigurationProviderService.",
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

        var violations = new List<string>();

        foreach (var root in searchRoots)
        {
            Assert.True(Directory.Exists(root), $"Search root not found: {root}");

            var csFiles = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                            !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));

            foreach (var file in csFiles)
            {
                var relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (Allowed.ContainsKey(relativePath))
                {
                    continue;
                }

                var content = File.ReadAllText(file);
                if (content.Contains(ForbiddenPattern, StringComparison.Ordinal))
                {
                    violations.Add(relativePath);
                }
            }
        }

        var message =
            $"The following files read Environment.SpecialFolder.ApplicationData directly instead of " +
            $"using IConfigurationProviderService.GetApplicationDataPath():\n{string.Join('\n', violations)}\n\n" +
            "If this is intentional (e.g. bootstrapping, or defining the default for the UI), " +
            "add the file to the allowlist in ApplicationDataPathConventionTests with an explanation.";

        Assert.True(violations.Count == 0, message);
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "GenHub", "GenHub.Core")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
