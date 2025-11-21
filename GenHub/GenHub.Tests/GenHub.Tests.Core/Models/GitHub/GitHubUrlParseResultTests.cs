using GenHub.Core.Models.GitHub;

namespace GenHub.Tests.Core.Models.GitHub;

/// <summary>
/// Unit tests for <see cref="GitHubUrlParseResult"/>.
/// </summary>
public class GitHubUrlParseResultTests
{
    /// <summary>
    /// Verifies that CreateSuccess creates a successful result with owner and repo.
    /// </summary>
    [Fact]
    public void CreateSuccess_WithOwnerAndRepo_CreatesSuccessfulResult()
    {
        // Arrange
        var owner = "testowner";
        var repo = "testrepo";

        // Act
        var result = GitHubUrlParseResult.CreateSuccess(owner, repo, null);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Failed);
        Assert.Equal(owner, result.Owner);
        Assert.Equal(repo, result.Repo);
        Assert.Null(result.Tag);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// Verifies that CreateSuccess creates a successful result with owner, repo, and tag.
    /// </summary>
    [Fact]
    public void CreateSuccess_WithOwnerRepoAndTag_CreatesSuccessfulResult()
    {
        // Arrange
        var owner = "testowner";
        var repo = "testrepo";
        var tag = "v1.0.0";

        // Act
        var result = GitHubUrlParseResult.CreateSuccess(owner, repo, tag);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(owner, result.Owner);
        Assert.Equal(repo, result.Repo);
        Assert.Equal(tag, result.Tag);
        Assert.False(result.HasErrors);
    }

    /// <summary>
    /// Verifies that CreateFailure creates a failed result with error messages.
    /// </summary>
    [Fact]
    public void CreateFailure_WithErrors_CreatesFailedResult()
    {
        // Arrange
        var errors = new[] { "Invalid URL", "Missing owner" };

        // Act
        var result = GitHubUrlParseResult.CreateFailure(errors);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.Failed);
        Assert.True(result.HasErrors);
        Assert.Equal(errors.Length, result.Errors.Count);
        Assert.Contains("Invalid URL", result.Errors);
        Assert.Contains("Missing owner", result.Errors);
        Assert.Equal(string.Empty, result.Owner);
        Assert.Equal(string.Empty, result.Repo);
        Assert.Null(result.Tag);
    }

    /// <summary>
    /// Verifies that CreateFailure creates a failed result with single error.
    /// </summary>
    [Fact]
    public void CreateFailure_WithSingleError_CreatesFailedResult()
    {
        // Arrange
        var error = "URL is null";

        // Act
        var result = GitHubUrlParseResult.CreateFailure(error);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.Failed);
        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal(error, result.FirstError);
        Assert.Equal(string.Empty, result.Owner);
        Assert.Equal(string.Empty, result.Repo);
        Assert.Null(result.Tag);
    }

    /// <summary>
    /// Verifies that the constructor sets properties correctly for success.
    /// </summary>
    [Fact]
    public void Constructor_WithSuccess_SetsPropertiesCorrectly()
    {
        // Arrange
        var owner = "owner";
        var repo = "repo";
        var tag = "tag";

        // Act
        var result = new GitHubUrlParseResult(true, owner, repo, tag);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(owner, result.Owner);
        Assert.Equal(repo, result.Repo);
        Assert.Equal(tag, result.Tag);
        Assert.False(result.HasErrors);
    }

    /// <summary>
    /// Verifies that the constructor sets properties correctly for failure.
    /// </summary>
    [Fact]
    public void Constructor_WithFailure_SetsPropertiesCorrectly()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2" };

        // Act
        var result = new GitHubUrlParseResult(false, errors: errors);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.HasErrors);
        Assert.Equal(errors.Length, result.Errors.Count);
        Assert.Equal(string.Empty, result.Owner);
        Assert.Equal(string.Empty, result.Repo);
        Assert.Null(result.Tag);
    }
}