using GenHub.Core.Constants;
using Xunit;

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
}
