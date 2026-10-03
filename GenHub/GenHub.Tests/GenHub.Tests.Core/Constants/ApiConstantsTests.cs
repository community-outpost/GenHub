using GenHub.Core.Constants;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for <see cref="ApiConstants"/> constants.
/// </summary>
public class ApiConstantsTests
{
    /// <summary>
    /// Tests that all API constants have expected values.
    /// </summary>
    [Fact]
    public void ApiConstants_Constants_ShouldHaveExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // User agents
            Assert.Equal(ApiConstants.DefaultUserAgent, $"{AppConstants.AppName}/{AppConstants.AppVersion}");

            // GitHub
            Assert.Equal("github.com", ApiConstants.GitHubDomain);
            Assert.Equal(@"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)(?:/releases/tag/(?<tag>[^/]+))?", ApiConstants.GitHubUrlRegexPattern);
        });
    }

    /// <summary>
    /// Tests that user agent constants are not null or empty.
    /// </summary>
    [Fact]
    public void ApiConstants_UserAgentConstants_ShouldNotBeNullOrEmpty()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.NotNull(ApiConstants.DefaultUserAgent);
            Assert.NotEmpty(ApiConstants.DefaultUserAgent);
        });
    }

    /// <summary>
    /// Tests that DefaultUserAgent is correctly constructed from AppConstants.
    /// </summary>
    [Fact]
    public void ApiConstants_DefaultUserAgent_ShouldBeConstructedFromAppConstants()
    {
        // Arrange & Act & Assert
        var expectedUserAgent = $"{AppConstants.AppName}/{AppConstants.AppVersion}";
        Assert.Equal(ApiConstants.DefaultUserAgent, expectedUserAgent);
    }

    /// <summary>
    /// Tests that branch archive URLs keep simple branches direct.
    /// </summary>
    [Fact]
    public void GetGitHubBranchZipUrl_WithSimpleBranch_BuildsDirectUrl()
    {
        var url = ApiConstants.GetGitHubBranchZipUrl("owner", "repo", "main");

        Assert.EndsWith("/owner/repo/zip/main", url);
        Assert.DoesNotContain("refs/heads", url);
    }

    /// <summary>
    /// Tests that slashed branches use the refs/heads form with escaped segments.
    /// </summary>
    /// <param name="branch">The branch name.</param>
    /// <param name="expectedSuffix">The expected URL suffix.</param>
    [Theory]
    [InlineData("feature/foo", "zip/refs/heads/feature/foo")]
    [InlineData("feature/foo/bar", "zip/refs/heads/feature/foo/bar")]
    public void GetGitHubBranchZipUrl_WithSlashedBranch_UsesRefsHeads(string branch, string expectedSuffix)
    {
        var url = ApiConstants.GetGitHubBranchZipUrl("owner", "repo", branch);

        Assert.EndsWith($"/owner/repo/{expectedSuffix}", url);
        Assert.DoesNotContain("%2F", url);
    }

    /// <summary>
    /// Tests that GitHub constants are not null or empty.
    /// </summary>
    [Fact]
    public void ApiConstants_GitHubConstants_ShouldNotBeNullOrEmpty()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.NotNull(ApiConstants.GitHubDomain);
            Assert.NotEmpty(ApiConstants.GitHubDomain);
            Assert.NotNull(ApiConstants.GitHubUrlRegexPattern);
            Assert.NotEmpty(ApiConstants.GitHubUrlRegexPattern);
        });
    }

    /// <summary>
    /// Tests that GitHub constants follow proper naming conventions.
    /// </summary>
    [Fact]
    public void ApiConstants_GitHubConstants_ShouldFollowNamingConventions()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // GitHub domain should be lowercase
            Assert.Equal(ApiConstants.GitHubDomain, ApiConstants.GitHubDomain.ToLower());

            // Should not contain spaces or special characters (except for regex pattern)
            Assert.DoesNotContain(" ", ApiConstants.GitHubDomain);

            // Should not contain uppercase letters
            Assert.DoesNotMatch("[A-Z]", ApiConstants.GitHubDomain);
        });
    }

    /// <summary>
    /// Tests that string constants are of correct type.
    /// </summary>
    [Fact]
    public void ApiConstants_StringConstants_ShouldBeCorrectType()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.IsType<string>(ApiConstants.DefaultUserAgent);
            Assert.IsType<string>(ApiConstants.GitHubDomain);
            Assert.IsType<string>(ApiConstants.GitHubUrlRegexPattern);
        });
    }

    /// <summary>
    /// Tests that workflow runs format constants target the ci.yml workflow.
    /// </summary>
    [Fact]
    public void ApiConstants_WorkflowRunsFormats_ShouldTargetCiWorkflow()
    {
        // Assert
        Assert.Multiple(() =>
        {
            Assert.Contains("/actions/workflows/ci.yml/runs", ApiConstants.GitHubApiWorkflowRunsFormat);
            Assert.Contains("/actions/workflows/ci.yml/runs", ApiConstants.GitHubApiWorkflowRunsAllFormat);
            Assert.Contains("/actions/workflows/ci.yml/runs", ApiConstants.GitHubApiLatestWorkflowRunsFormat);
        });
    }
}
