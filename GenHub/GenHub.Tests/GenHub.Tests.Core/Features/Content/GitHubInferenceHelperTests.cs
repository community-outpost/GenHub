using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Features.Content.Services.Helpers;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="GitHubInferenceHelper"/>.
/// Ensures the inference heuristics return expected values and mark them as inferred.
/// </summary>
public class GitHubInferenceHelperTests
{
    /// <summary>
    /// Verifies <see cref="GitHubInferenceHelper.InferContentType"/> returns the expected content type and marks it as inferred.
    /// </summary>
    /// <param name="repo">Repository name used for inference.</param>
    /// <param name="releaseName">Optional release name or tag used for inference.</param>
    /// <param name="expected">Expected inferred <see cref="ContentType"/> value.</param>
    [Theory]
    [InlineData("some-repo", "patch-1.0", ContentType.Patch)]
    [InlineData("maps-repo", "v1 map pack", ContentType.MapPack)]
    [InlineData("cool-mod", "", ContentType.Mod)]
    public void InferContentType_ReturnsExpectedContentType(string repo, string? releaseName, ContentType expected)
    {
        // Act
        var (type, isInferred) = GitHubInferenceHelper.InferContentType(repo, releaseName);

        // Assert
        Assert.Equal(expected, type);
        Assert.True(isInferred, "Inference result should be marked as inferred for heuristic matches.");
    }

    /// <summary>
    /// Verifies <see cref="GitHubInferenceHelper.InferTargetGame"/> returns the expected game type and marks it as inferred.
    /// </summary>
    /// <param name="repo">Repository name used for inference.</param>
    /// <param name="releaseName">Optional release name or tag used for inference.</param>
    /// <param name="expected">Expected inferred <see cref="GameType"/> value.</param>
    [Theory]
    [InlineData("repo", "zero hour release", GameType.ZeroHour)]
    [InlineData("repo-zh", "", GameType.ZeroHour)]
    [InlineData("generals-repo", "", GameType.Generals)]
    public void InferTargetGame_ReturnsExpectedGameType(string repo, string? releaseName, GameType expected)
    {
        // Act
        var (type, isInferred) = GitHubInferenceHelper.InferTargetGame(repo, releaseName);

        // Assert
        Assert.Equal(expected, type);
        Assert.True(isInferred, "Inference result should be marked as inferred for heuristic matches.");
    }

    /// <summary>
    /// Verifies <see cref="GitHubInferenceHelper.InferTagsFromRelease"/> returns tags based on release content and flags.
    /// </summary>
    [Fact]
    public void InferTagsFromRelease_ReturnsExpectedTags()
    {
        // Arrange
        var release = new GitHubRelease
        {
            Name = "Patch and Fix",
            Body = "Includes mod and map updates",
            IsPrerelease = true,
            IsDraft = false,
        };

        // Act
        var tags = GitHubInferenceHelper.InferTagsFromRelease(release);

        // Assert
        Assert.Contains("Patch", tags);
        Assert.Contains("Fix", tags);
        Assert.Contains("Mod", tags);
        Assert.Contains("Map", tags);
        Assert.Contains("Prerelease", tags);
    }

    /// <summary>
    /// Verifies <see cref="GitHubInferenceHelper.IsExecutableFile"/> recognizes common executable extensions.
    /// </summary>
    /// <param name="fileName">File name to inspect.</param>
    /// <param name="expected">Expected boolean result.</param>
    [Theory]
    [InlineData("program.exe", true)]
    [InlineData("library.dll", true)]
    [InlineData("script.sh", true)]
    [InlineData("readme.txt", false)]
    public void IsExecutableFile_ReturnsExpectedResult(string fileName, bool expected)
    {
        var result = GitHubInferenceHelper.IsExecutableFile(fileName);
        Assert.Equal(expected, result);
    }
}