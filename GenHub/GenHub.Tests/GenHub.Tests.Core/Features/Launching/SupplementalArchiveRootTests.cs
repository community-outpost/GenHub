using GenHub.Core.Models.Enums;
using GenHub.Features.Launching;
using Xunit;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Verifies supplemental archive root resolution for Zero Hour workspaces whose engine cannot
/// resolve the base Generals root out of band.
/// </summary>
public class SupplementalArchiveRootTests
{
    /// <summary>
    /// Zero Hour launched as a Windows binary needs its base Generals archives linked into the
    /// workspace, since the binary reads neither the Wine registry nor the environment.
    /// </summary>
    [Fact]
    public void ResolveSupplementalArchiveRoot_ZeroHourWindowsBinary_ReturnsGeneralsRoot()
    {
        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            "generalszh.exe",
            "/retail/generals");

        Assert.Equal("/retail/generals", root);
    }

    /// <summary>
    /// The executable check is case-insensitive, matching retail naming variants.
    /// </summary>
    [Fact]
    public void ResolveSupplementalArchiveRoot_ZeroHourUppercaseExecutable_ReturnsGeneralsRoot()
    {
        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            "GENERALSZH.EXE",
            "/retail/generals");

        Assert.Equal("/retail/generals", root);
    }

    /// <summary>
    /// Base Generals needs no supplemental archives: it mounts only its own content.
    /// </summary>
    [Fact]
    public void ResolveSupplementalArchiveRoot_Generals_ReturnsNull()
    {
        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.Generals,
            "generals.exe",
            "/retail/generals");

        Assert.Null(root);
    }

    /// <summary>
    /// The native engine resolves its archive roots from the environment, so it needs nothing
    /// linked into the workspace.
    /// </summary>
    /// <param name="executablePath">The game client's executable path.</param>
    [Theory]
    [InlineData("generalszh")]
    [InlineData("game.dat")]
    [InlineData("generals.ctr")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveSupplementalArchiveRoot_ZeroHourNonWindowsBinary_ReturnsNull(string? executablePath)
    {
        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            executablePath,
            "/retail/generals");

        Assert.Null(root);
    }

    /// <summary>
    /// Without a Generals root there is nothing to link, regardless of game and binary.
    /// </summary>
    /// <param name="generalsRoot">The effective Generals archive root.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSupplementalArchiveRoot_ZeroHourWindowsBinaryWithoutGeneralsRoot_ReturnsNull(string? generalsRoot)
    {
        var root = GameLauncher.ResolveSupplementalArchiveRoot(
            GameType.ZeroHour,
            "generalszh.exe",
            generalsRoot);

        Assert.Null(root);
    }
}
