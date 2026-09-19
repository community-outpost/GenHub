using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Features.Launching;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Verifies supplemental archive root resolution for Zero Hour workspaces whose engine cannot
/// resolve the base Generals root out of band.
/// </summary>
public class SupplementalArchiveRootTests
{
    /// <summary>
    /// Zero Hour resolves its installation's effective Generals root as supplemental content.
    /// </summary>
    [Fact]
    public void ResolveSupplementalArchiveRoot_ZeroHourWithGeneralsRoot_ReturnsRoot()
    {
        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            "/retail/generals",
            null);

        Assert.Equal(Path.GetFullPath("/retail/generals"), root);
    }

    /// <summary>
    /// A profile-level Generals install-path override wins over the installation root, mirroring
    /// archive-root environment assembly: it names the root actually used.
    /// </summary>
    [Fact]
    public void ResolveSupplementalArchiveRoot_ZeroHourWithProfileOverride_PrefersOverride()
    {
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.GeneralsInstallPathVariable] = "/profile/generals",
        };

        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            "/retail/generals",
            environment);

        Assert.Equal(Path.GetFullPath("/profile/generals"), root);
    }

    /// <summary>
    /// A profile override supplies the root even when the installation declares none, so the
    /// launch validation passes against the same root the workspace links.
    /// </summary>
    [Fact]
    public void ResolveSupplementalArchiveRoot_ZeroHourWithoutGeneralsRoot_UsesProfileOverride()
    {
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.GeneralsInstallPathVariable] = "/profile/generals",
        };

        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            null,
            environment);

        Assert.Equal(Path.GetFullPath("/profile/generals"), root);
    }

    /// <summary>
    /// A relative override is pinned to the absolute path the linker enumerates, so stored
    /// link targets are never classified as foreign on subsequent runs.
    /// </summary>
    [Fact]
    public void ResolveSupplementalArchiveRoot_RelativeProfileOverride_ReturnsAbsolutePath()
    {
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.GeneralsInstallPathVariable] = Path.Combine("relative", "generals"),
        };

        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            null,
            environment);

        Assert.Equal(Path.GetFullPath(Path.Combine("relative", "generals")), root);
        Assert.True(Path.IsPathRooted(root));
    }

    /// <summary>
    /// An unusable override resolves to null so launch validation reports it instead of the
    /// workspace preparation failing on it.
    /// </summary>
    [Fact]
    public void ResolveSupplementalArchiveRoot_UnusableProfileOverride_ReturnsNull()
    {
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.GeneralsInstallPathVariable] = "invalid\0path",
        };

        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            "/retail/generals",
            environment);

        Assert.Null(root);
    }

    /// <summary>
    /// Base Generals needs no supplemental archives: it mounts only its own content.
    /// </summary>
    [Fact]
    public void ResolveSupplementalArchiveRoot_Generals_ReturnsNull()
    {
        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.Generals,
            "/retail/generals",
            null);

        Assert.Null(root);
    }

    /// <summary>
    /// A profile override never leaks supplemental archives into Generals launches.
    /// </summary>
    [Fact]
    public void ResolveSupplementalArchiveRoot_GeneralsWithProfileOverride_ReturnsNull()
    {
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.GeneralsInstallPathVariable] = "/profile/generals",
        };

        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.Generals,
            "/retail/generals",
            environment);

        Assert.Null(root);
    }

    /// <summary>
    /// Without a Generals root there is nothing to link, regardless of game.
    /// </summary>
    /// <param name="generalsRoot">The effective Generals archive root.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSupplementalArchiveRoot_ZeroHourWithoutAnyRoot_ReturnsNull(string? generalsRoot)
    {
        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            generalsRoot,
            new Dictionary<string, string>());

        Assert.Null(root);
    }

    /// <summary>
    /// A blank profile override is not a root: resolution falls through to the installation root.
    /// </summary>
    /// <param name="overrideValue">The profile override value.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSupplementalArchiveRoot_BlankProfileOverride_FallsThroughToInstallationRoot(string overrideValue)
    {
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.GeneralsInstallPathVariable] = overrideValue,
        };

        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            "/retail/generals",
            environment);

        Assert.Equal(Path.GetFullPath("/retail/generals"), root);
    }
}
