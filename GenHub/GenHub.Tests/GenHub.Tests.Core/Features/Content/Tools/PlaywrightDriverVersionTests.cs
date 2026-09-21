using GenHub.Core.Constants;
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Tools;

/// <summary>
/// Guards the on-demand Playwright driver against version drift.
/// </summary>
public partial class PlaywrightDriverVersionTests
{
    /// <summary>
    /// Verifies the managed driver version matches the referenced Microsoft.Playwright package.
    /// </summary>
    [Fact]
    public void PlaywrightDriverVersion_MatchesReferencedPackageVersion()
    {
        var packagesProps = FindDirectoryPackagesProps();
        var content = File.ReadAllText(packagesProps);
        var match = PlaywrightPackageVersionRegex().Match(content);

        Assert.True(match.Success, "Microsoft.Playwright package version not found in Directory.Packages.props.");
        Assert.Equal(ModDBConstants.PlaywrightDriverVersion, match.Groups["version"].Value);
    }

    private static string FindDirectoryPackagesProps()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && directory != null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, "Directory.Packages.props");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Directory.Packages.props not found above the test output directory.");
    }

    [GeneratedRegex(
        "<PackageVersion\\s+Include=\"Microsoft\\.Playwright\"\\s+Version=\"(?<version>[^\"]+)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex PlaywrightPackageVersionRegex();
}
