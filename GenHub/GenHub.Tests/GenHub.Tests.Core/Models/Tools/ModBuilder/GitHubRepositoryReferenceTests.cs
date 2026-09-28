using GenHub.Core.Models.Tools.ModBuilder;
using Xunit;

namespace GenHub.Tests.Core.Models.Tools.ModBuilder;

/// <summary>
/// Tests for <see cref="GitHubRepositoryReference"/> parsing.
/// </summary>
public sealed class GitHubRepositoryReferenceTests
{
    /// <summary>
    /// Valid inputs parse to the expected owner, repository, and branch.
    /// </summary>
    /// <param name="input">The raw input.</param>
    /// <param name="owner">The expected owner.</param>
    /// <param name="repo">The expected repository.</param>
    /// <param name="branch">The expected branch.</param>
    [Theory]
    [InlineData("owner/repo", "owner", "repo", "main")]
    [InlineData("  owner/repo  ", "owner", "repo", "main")]
    [InlineData("owner/repo@dev", "owner", "repo", "dev")]
    [InlineData("owner/repo@feature/foo", "owner", "repo", "feature/foo")]
    [InlineData("https://github.com/owner/repo", "owner", "repo", "main")]
    [InlineData("https://github.com/owner/repo/", "owner", "repo", "main")]
    [InlineData("github.com/owner/repo", "owner", "repo", "main")]
    [InlineData("www.github.com/owner/repo", "owner", "repo", "main")]
    [InlineData("https://www.github.com/owner/repo", "owner", "repo", "main")]
    [InlineData("https://github.com/owner/repo.git", "owner", "repo", "main")]
    [InlineData("owner/repo.git", "owner", "repo", "main")]
    [InlineData("https://github.com/owner/repo/tree/develop", "owner", "repo", "develop")]
    [InlineData("https://github.com/owner/repo/tree/feature+linux", "owner", "repo", "feature+linux")]
    [InlineData("https://github.com/owner/repo/tree/feature@linux", "owner", "repo", "feature@linux")]
    [InlineData("owner/repo@release.git", "owner", "repo", "release.git")]
    [InlineData("https://github.com/owner/repo/tree/release.git", "owner", "repo", "release.git")]
    [InlineData("owner/repo.git@release.git", "owner", "repo", "release.git")]
    [InlineData("owner/repo@v1.0.0,build.1", "owner", "repo", "v1.0.0,build.1")]
    [InlineData("https://github.com/owner/repo/tree/feature/foo", "owner", "repo", "feature/foo")]
    [InlineData("owner-name/repo.name_2", "owner-name", "repo.name_2", "main")]
    [InlineData("owner/github.com", "owner", "github.com", "main")]
    [InlineData("github.com-fan/repo", "github.com-fan", "repo", "main")]
    public void TryParse_WithValidInput_ReturnsReference(string input, string owner, string repo, string branch)
    {
        var reference = GitHubRepositoryReference.TryParse(input);

        Assert.NotNull(reference);
        Assert.Equal(owner, reference.Owner);
        Assert.Equal(repo, reference.Repo);
        Assert.Equal(branch, reference.Branch);
        Assert.Equal($"{owner}/{repo}", reference.FullName);
    }

    /// <summary>
    /// The default branch applies when the input names none.
    /// </summary>
    [Fact]
    public void TryParse_WithoutBranch_UsesDefaultBranch()
    {
        var reference = GitHubRepositoryReference.TryParse("owner/repo", "master");

        Assert.NotNull(reference);
        Assert.Equal("master", reference.Branch);
    }

    /// <summary>
    /// An embedded branch in the input takes precedence over default branch parameter.
    /// </summary>
    /// <param name="input">The raw repository input with embedded branch.</param>
    /// <param name="expectedBranch">The expected branch.</param>
    [Theory]
    [InlineData("https://github.com/owner/repo/tree/develop", "develop")]
    [InlineData("owner/repo@feature/foo", "feature/foo")]
    public void TryParse_WithEmbeddedBranch_OverridesDefaultBranch(string input, string expectedBranch)
    {
        var reference = GitHubRepositoryReference.TryParse(input, "main");

        Assert.NotNull(reference);
        Assert.Equal(expectedBranch, reference.Branch);
    }

    /// <summary>
    /// Invalid inputs return null.
    /// </summary>
    /// <param name="input">The raw input.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("owner")]
    [InlineData("owner/")]
    [InlineData("/repo")]
    [InlineData("owner/repo/extra")]
    [InlineData("owner/repo/tree")]
    [InlineData("owner/re po")]
    [InlineData("own er/repo")]
    [InlineData("owner/repo@")]
    [InlineData("owner/repo@bad branch")]
    [InlineData("https://evil.com/owner/repo")]
    [InlineData("https://evilgithub.com/owner/repo")]
    [InlineData("http://github.com/owner/repo")]
    [InlineData("ftp://github.com/owner/repo")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/")]
    public void TryParse_WithInvalidInput_ReturnsNull(string? input)
    {
        Assert.Null(GitHubRepositoryReference.TryParse(input));
    }

    /// <summary>
    /// Overlong inputs are rejected.
    /// </summary>
    [Fact]
    public void TryParse_WithOverlongInput_ReturnsNull()
    {
        var input = "owner/" + new string('a', 600);

        Assert.Null(GitHubRepositoryReference.TryParse(input));
    }
}
