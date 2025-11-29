using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Features.GitHub.Factories;
using GenHub.Features.GitHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Tests for GitHubManagerViewModel.
/// </summary>
public class GitHubManagerViewModelTests
{
    private readonly Mock<IGitHubApiClient> _gitHubApiClientMock;
    private readonly Mock<IGitHubTokenStorage> _tokenStorageMock;
    private readonly Mock<GitHubDisplayItemFactory> _factoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ILogger<GitHubManagerViewModel>> _loggerMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubManagerViewModelTests"/> class.
    /// </summary>
    public GitHubManagerViewModelTests()
    {
        _gitHubApiClientMock = new Mock<IGitHubApiClient>();
        _tokenStorageMock = new Mock<IGitHubTokenStorage>();
        _factoryMock = new Mock<GitHubDisplayItemFactory>(_gitHubApiClientMock.Object, Mock.Of<ILogger<GitHubDisplayItemFactory>>());
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<GitHubManagerViewModel>>();

        // Create proper mocks for the child view models
        var repositoryControlViewModelMock = new Mock<RepositoryControlViewModel>(
            Mock.Of<ILogger<RepositoryControlViewModel>>());

        var itemsTreeViewModelMock = new Mock<GitHubItemsTreeViewModel>(
            Mock.Of<ILogger<GitHubItemsTreeViewModel>>(),
            repositoryControlViewModelMock.Object);

        var detailsViewModelMock = new Mock<GitHubDetailsViewModel>(
            Mock.Of<ILogger<GitHubDetailsViewModel>>());

        var installationViewModelMock = new Mock<InstallationViewModel>(
            Mock.Of<IContentStorageService>(),
            Mock.Of<IContentOrchestrator>(),
            Mock.Of<IDependencyResolver>(),
            Mock.Of<ILogger<InstallationViewModel>>());

        _serviceProviderMock.Setup(x => x.GetService(typeof(GitHubItemsTreeViewModel)))
            .Returns(itemsTreeViewModelMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(GitHubDetailsViewModel)))
            .Returns(detailsViewModelMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(InstallationViewModel)))
            .Returns(installationViewModelMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(RepositoryControlViewModel)))
            .Returns(repositoryControlViewModelMock.Object);
    }

    /// <summary>
    /// Tests that the constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        var viewModel = new GitHubManagerViewModel(
            _gitHubApiClientMock.Object,
            _tokenStorageMock.Object,
            _factoryMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object);

        Assert.NotNull(viewModel.Items);
        Assert.Empty(viewModel.Items);
        Assert.False(viewModel.IsLoading);
    }

    /// <summary>
    /// Tests that the constructor throws ArgumentNullException when apiClient is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullApiClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GitHubManagerViewModel(null!, _tokenStorageMock.Object, _factoryMock.Object, _serviceProviderMock.Object, _loggerMock.Object));
    }
}
