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
    /// GeneralsGameCode releases must be classified as GameClient explicitly (not inferred),
    /// so that SuperHackersManifestFactory.CanHandle accepts the resolved manifest.
    /// </summary>
    [Fact]
    public void InferContentType_GeneralsGameCode_ReturnsExplicitGameClient()
    {
        var (type, isInferred) = GitHubInferenceHelper.InferContentType("GeneralsGameCode", "weekly-2026-07-24");
        Assert.Equal(ContentType.GameClient, type);
        Assert.False(isInferred);
    }

    /// <summary>
    /// When no known topic is present the topic lookup returns an inferred Addon guess;
    /// callers must treat IsInferred == true as "run the name-based fallback".
    /// </summary>
    [Fact]
    public void InferContentTypeFromTopics_UnknownTopics_ReturnsInferredAddon()
    {
        var (type, isInferred) = GitHubInferenceHelper.InferContentTypeFromTopics(new[] { "some-unrelated-topic" });
        Assert.Equal(ContentType.Addon, type);
        Assert.True(isInferred);
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
    [InlineData("generals-repo", "", GameType.ZeroHour)]
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
    [InlineData("script.sh", true)]

    // A native game binary has no extension. This previously returned false here while
    // returning true in ContentManifestBuilder, so the same file was classified
    // differently depending on which factory built the manifest.
    [InlineData("generalszh", true)]

    // Changed deliberately: a dynamic library is loadable code, not a runnable file.
    // dyld and ld.so map libraries with read access, so the execute bit is meaningless,
    // and under a hard-link workspace setting it would mutate a shared CAS blob.
    [InlineData("library.dll", false)]
    [InlineData("libSDL3.dylib", false)]
    [InlineData("readme.txt", false)]
    public void IsExecutableFile_ReturnsExpectedResult(string fileName, bool expected)
    {
        var result = GitHubInferenceHelper.IsExecutableFile(fileName);
        Assert.Equal(expected, result);
    }
}
