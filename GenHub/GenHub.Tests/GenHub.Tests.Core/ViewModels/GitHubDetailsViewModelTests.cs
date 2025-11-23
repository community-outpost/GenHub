using GenHub.Core.Models.GitHub;
using GenHub.Features.GitHub.ViewModels;
using GenHub.Features.GitHub.ViewModels.Items;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Tests for GitHubDetailsViewModel.
/// </summary>
public class GitHubDetailsViewModelTests
{
    private readonly Mock<ILogger<GitHubDetailsViewModel>> _loggerMock;
    private readonly GitHubDetailsViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubDetailsViewModelTests"/> class.
    /// </summary>
    public GitHubDetailsViewModelTests()
    {
        _loggerMock = new Mock<ILogger<GitHubDetailsViewModel>>();
        _viewModel = new GitHubDetailsViewModel(_loggerMock.Object);
    }

    /// <summary>
    /// Verifies that constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        // Assert
        Assert.NotNull(_viewModel.Details);
        Assert.Empty(_viewModel.Details);
        Assert.Null(_viewModel.SelectedItem);
        Assert.False(_viewModel.IsLoading);
        Assert.Equal("Select an item to view details", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Verifies that constructor throws exception when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Assert
        Assert.Throws<ArgumentNullException>(() => new GitHubDetailsViewModel(null!));
    }

    /// <summary>
    /// Tests that ClearSelection clears all details.
    /// </summary>
    [Fact]
    public void ClearSelection_ClearsAllDetails()
    {
        var itemMock = new Mock<GitHubDisplayItemViewModel>(_loggerMock.Object);
        _viewModel.SetSelectedItem(itemMock.Object);

        _viewModel.ClearSelection();

        Assert.Null(_viewModel.SelectedItem);
        Assert.Empty(_viewModel.Details);
        Assert.Equal("Select an item to view details", _viewModel.StatusMessage);
    }
}
