using GenHub.Features.GitHub.ViewModels;
using GenHub.Features.GitHub.ViewModels.Items;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.ObjectModel;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Tests for GitHubItemsTreeViewModel.
/// </summary>
public class GitHubItemsTreeViewModelTests
{
    private readonly Mock<ILogger<GitHubItemsTreeViewModel>> _loggerMock;
    private readonly Mock<RepositoryControlViewModel> _repositoryControlMock;
    private readonly GitHubItemsTreeViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubItemsTreeViewModelTests"/> class.
    /// </summary>
    public GitHubItemsTreeViewModelTests()
    {
        _loggerMock = new Mock<ILogger<GitHubItemsTreeViewModel>>();
        var repoLoggerMock = new Mock<ILogger<RepositoryControlViewModel>>();
        _repositoryControlMock = new Mock<RepositoryControlViewModel>(repoLoggerMock.Object);
        _viewModel = new GitHubItemsTreeViewModel(_loggerMock.Object, _repositoryControlMock.Object);
    }

    /// <summary>
    /// Verifies that constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        Assert.NotNull(_viewModel.Items);
        Assert.Empty(_viewModel.Items);
        Assert.Null(_viewModel.SelectedItem);
    }

    /// <summary>
    /// Verifies that constructor throws exception when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GitHubItemsTreeViewModel(null!, _repositoryControlMock.Object));
    }

    /// <summary>
    /// Tests that ClearItems clears all items.
    /// </summary>
    [Fact]
    public void ClearItems_ClearsAllItems()
    {
        _viewModel.ClearItems();

        Assert.Empty(_viewModel.Items);
        Assert.Null(_viewModel.SelectedItem);
    }
}
