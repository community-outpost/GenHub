using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Services.Tools;
using System;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="GenHubBuildInspector"/>.
/// </summary>
public class GenHubBuildInspectorTests
{
    private readonly IGenHubBuildInspector _inspector = new GenHubBuildInspector();

    /// <summary>
    /// Verifies that paths containing GenHub build extensions and name patterns are identified.
    /// </summary>
    /// <param name="path">The file path to test.</param>
    /// <param name="expected">Expected detection result.</param>
    [Theory]
    [InlineData("C:\\Releases\\GenHub-Setup-1.0.0.exe", true)]
    [InlineData("/tmp/GenHub.Core-1.2.3.nupkg", true)]
    [InlineData("D:\\Builds\\genhub-v2.0.0-win-x64.zip", true)]
    [InlineData("GenHub-Nightly.7z", true)]
    [InlineData("C:\\Games\\Command and Conquer Generals\\generals.exe", false)]
    [InlineData("C:\\Games\\Zero Hour\\game.dat", false)]
    [InlineData("C:\\Apps\\Discord.exe", false)]
    [InlineData("", false)]
    public void IsGenHubBuildPath_DetectsAppropriately(string path, bool expected)
    {
        var result = _inspector.IsGenHubBuildPath(path);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that release naming produces correct version and suggested category.
    /// </summary>
    [Fact]
    public void Inspect_ReleaseArtifact_InfersReleaseMetadata()
    {
        var info = _inspector.Inspect("C:\\Builds\\GenHub-Setup-1.5.2.exe");

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("1.5.2", info.Version);
        Assert.Equal("Release", info.SuggestedCategory);
        Assert.Equal("Release", info.BuildChannel);
        Assert.Null(info.PullRequestNumber);
        Assert.False(info.IsCustomBuild);
    }

    /// <summary>
    /// Verifies that PR builds are categorized as Test and extract the PR number.
    /// </summary>
    [Fact]
    public void Inspect_PullRequestArtifact_InfersTestCategoryAndPrNumber()
    {
        var info = _inspector.Inspect("/builds/GenHub-PR-123-1.0.0-dev.45.exe");

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("1.0.0-dev.45", info.Version);
        Assert.Equal("Test", info.SuggestedCategory);
        Assert.Equal("PR", info.BuildChannel);
        Assert.Equal(123, info.PullRequestNumber);
    }

    /// <summary>
    /// Verifies that development / nightly builds are categorized as Dev.
    /// </summary>
    [Fact]
    public void Inspect_DevBuildArtifact_InfersDevCategory()
    {
        var info = _inspector.Inspect("C:\\CI\\genhub-dev-build-456.zip");

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("Dev", info.SuggestedCategory);
        Assert.Equal("Dev", info.BuildChannel);
    }

    /// <summary>
    /// Verifies that community forks are categorized as CustomFork.
    /// </summary>
    [Fact]
    public void Inspect_ForkArtifact_InfersCustomForkCategory()
    {
        var info = _inspector.Inspect("D:\\Downloads\\GenHub-Community-Fork-v1.0.0.zip");

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("1.0.0", info.Version);
        Assert.Equal("CustomFork", info.SuggestedCategory);
        Assert.True(info.IsCustomBuild || info.IsFork);
    }

    /// <summary>
    /// Verifies that non-GenHub files report IsGenHubBuild as false and Unknown version.
    /// </summary>
    [Fact]
    public void Inspect_NonGenHubFile_ReturnsFalse()
    {
        var info = _inspector.Inspect("C:\\Games\\generals.exe");

        Assert.False(info.IsGenHubBuild);
        Assert.Equal("Unknown", info.Version);
        Assert.Equal("Release", info.SuggestedCategory);
    }

    /// <summary>
    /// Verifies that a non-existent file does not throw and falls back cleanly.
    /// </summary>
    [Fact]
    public void Inspect_NonExistentFile_DoesNotThrow()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "GenHub-Setup-9.9.9-doesnotexist.exe");
        var info = _inspector.Inspect(fakePath);

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("9.9.9", info.Version);
        Assert.Equal("Release", info.SuggestedCategory);
    }
}
