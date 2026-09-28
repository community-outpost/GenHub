using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// Tests for <see cref="GitHubImportViewModel"/>.
/// </summary>
public sealed class GitHubImportViewModelTests
{
    /// <summary>
    /// Valid input validates to a repository reference.
    /// </summary>
    [Fact]
    public void Validate_WithValidInput_ReturnsReference()
    {
        var viewModel = new GitHubImportViewModel(Mock.Of<ILocalizationService>())
        {
            RepositoryText = "owner/repo",
            BranchText = "develop",
        };

        var reference = viewModel.Validate();

        Assert.NotNull(reference);
        Assert.Equal("owner", reference.Owner);
        Assert.Equal("repo", reference.Repo);
        Assert.Equal("develop", reference.Branch);
        Assert.False(viewModel.HasError);
    }

    /// <summary>
    /// An empty branch falls back to the default branch.
    /// </summary>
    [Fact]
    public void Validate_WithEmptyBranch_UsesDefaultBranch()
    {
        var viewModel = new GitHubImportViewModel(Mock.Of<ILocalizationService>())
        {
            RepositoryText = "owner/repo",
            BranchText = "  ",
        };

        var reference = viewModel.Validate();

        Assert.NotNull(reference);
        Assert.Equal(ModBuilderConstants.GitHubDefaultBranch, reference.Branch);
    }

    /// <summary>
    /// Invalid input surfaces a localized error and clears on edit.
    /// </summary>
    [Fact]
    public void Validate_WithInvalidInput_ShowsError()
    {
        var mockLocalization = new Mock<ILocalizationService>();
        mockLocalization.Setup(x => x.GetString(It.IsAny<string>())).Returns("INVALID");
        var viewModel = new GitHubImportViewModel(mockLocalization.Object)
        {
            RepositoryText = "not a repo",
        };

        var reference = viewModel.Validate();

        Assert.Null(reference);
        Assert.True(viewModel.HasError);
        Assert.Equal("INVALID", viewModel.ErrorText);

        viewModel.RepositoryText = "owner/repo";

        Assert.False(viewModel.HasError);
    }

    /// <summary>
    /// Pasting a tree URL preserves the embedded branch even when BranchText is untouched.
    /// </summary>
    [Fact]
    public void Validate_WithTreeUrlAndUntouchedBranchText_PreservesEmbeddedBranch()
    {
        var viewModel = new GitHubImportViewModel(Mock.Of<ILocalizationService>())
        {
            RepositoryText = "https://github.com/owner/repo/tree/develop",
        };

        var reference = viewModel.Validate();

        Assert.NotNull(reference);
        Assert.Equal("develop", reference.Branch);
        Assert.Equal("develop", viewModel.BranchText);
    }
}
