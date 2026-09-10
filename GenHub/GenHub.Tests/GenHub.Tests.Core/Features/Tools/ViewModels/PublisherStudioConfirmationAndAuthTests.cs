using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services;
using GenHub.Features.Tools.Services.Hosting;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests verifying catalog deletion confirmation, provider authentication handling, and dialog forwarding.
/// </summary>
public class PublisherStudioConfirmationAndAuthTests
{
    private readonly Mock<IPublisherStudioService> _mockStudioService = new();
    private readonly Mock<IPublisherStudioDialogService> _mockDialogService = new();
    private readonly Mock<ILogger<PublisherStudioViewModel>> _mockStudioLogger = new();
    private readonly Mock<ILogger<PublishShareViewModel>> _mockPublishLogger = new();
    private readonly Mock<IHostingStateManager> _mockHostingStateManager = new();
    private readonly Mock<INotificationService> _mockNotificationService = new();

    /// <summary>
    /// Tests that confirming catalog deletion removes the catalog from the collection and prompts with confirmation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test operation.</returns>
    [Fact]
    public async Task RemoveCatalogCommand_WhenConfirmed_RemovesCatalog()
    {
        // Arrange
        _mockDialogService
            .Setup(d => d.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(true);

        var vm = new PublisherStudioViewModel(
            _mockStudioLogger.Object,
            _mockStudioService.Object,
            _mockDialogService.Object);

        var project = new PublisherStudioProject { ProjectPath = "test/project.json" };
        var cat1 = new NamedCatalog { Id = "cat1", Name = "Catalog 1" };
        var cat2 = new NamedCatalog { Id = "cat2", Name = "Catalog 2" };
        project.Catalogs.Add(cat1);
        project.Catalogs.Add(cat2);

        vm.CurrentProject = project;
        vm.Catalogs.Clear();
        vm.Catalogs.Add(cat1);
        vm.Catalogs.Add(cat2);
        Assert.Equal(2, vm.Catalogs.Count);

        // Act
        await vm.RemoveCatalogCommand.ExecuteAsync(cat2);

        // Assert
        Assert.Single(vm.Catalogs);
        Assert.DoesNotContain(cat2, vm.Catalogs);
        _mockDialogService.Verify(
            d => d.ShowConfirmationAsync(
                "Delete Catalog",
                It.Is<string>(s => s.Contains("Catalog 2")),
                "Delete",
                "Cancel",
                "DeleteCatalogConfirmation"),
            Times.Once);
    }

    /// <summary>
    /// Tests that cancelling catalog deletion leaves the catalog in the collection.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test operation.</returns>
    [Fact]
    public async Task RemoveCatalogCommand_WhenCancelled_PreservesCatalog()
    {
        // Arrange
        _mockDialogService
            .Setup(d => d.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(false);

        var vm = new PublisherStudioViewModel(
            _mockStudioLogger.Object,
            _mockStudioService.Object,
            _mockDialogService.Object);

        var project = new PublisherStudioProject { ProjectPath = "test/project.json" };
        var cat1 = new NamedCatalog { Id = "cat1", Name = "Catalog 1" };
        var cat2 = new NamedCatalog { Id = "cat2", Name = "Catalog 2" };
        project.Catalogs.Add(cat1);
        project.Catalogs.Add(cat2);

        vm.CurrentProject = project;
        vm.Catalogs.Clear();
        vm.Catalogs.Add(cat1);
        vm.Catalogs.Add(cat2);

        // Act
        await vm.RemoveCatalogCommand.ExecuteAsync(cat2);

        // Assert
        Assert.Equal(2, vm.Catalogs.Count);
        Assert.Contains(cat2, vm.Catalogs);
    }

    /// <summary>
    /// Tests that switching hosting providers does not clear user entered tokens.
    /// </summary>
    [Fact]
    public void SwitchSelectedHostingProvider_DoesNotWipeUserEnteredTokens()
    {
        // Arrange
        var project = new PublisherStudioProject();
        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);

        var githubMock = new Mock<IHostingProvider>();
        githubMock.Setup(p => p.ProviderId).Returns(HostingConstants.GitHub);

        var dropboxMock = new Mock<IHostingProvider>();
        dropboxMock.Setup(p => p.ProviderId).Returns(HostingConstants.Dropbox);

        vm.GitHubPersonalAccessToken = "ghp_secret123";
        vm.DropboxAccessToken = "sl.dropboxsecret456";

        // Act - switch to Dropbox then GitHub
        vm.SelectedHostingProvider = dropboxMock.Object;
        vm.SelectedHostingProvider = githubMock.Object;

        // Assert - tokens remain intact and are not cleared on switch
        Assert.Equal("ghp_secret123", vm.GitHubPersonalAccessToken);
        Assert.Equal("sl.dropboxsecret456", vm.DropboxAccessToken);
    }

    /// <summary>
    /// Tests that attempting to authenticate with Google Drive without credentials fails gracefully with warning.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test operation.</returns>
    [Fact]
    public async Task AuthenticateAsync_GoogleDriveWithNoCredentials_SetsFriendlyMessageAndWarns()
    {
        // Clear environment variables for test isolation
        var originalId = System.Environment.GetEnvironmentVariable("GENHUB_GOOGLE_CLIENT_ID");
        var originalSecret = System.Environment.GetEnvironmentVariable("GENHUB_GOOGLE_CLIENT_SECRET");
        System.Environment.SetEnvironmentVariable("GENHUB_GOOGLE_CLIENT_ID", null);
        System.Environment.SetEnvironmentVariable("GENHUB_GOOGLE_CLIENT_SECRET", null);

        try
        {
            var project = new PublisherStudioProject();
            var vm = new PublishShareViewModel(
                project,
                _mockStudioService.Object,
                _mockPublishLogger.Object,
                null,
                _mockHostingStateManager.Object,
                _mockNotificationService.Object);

            var googleProvider = new GoogleDriveHostingProvider(new Mock<ILogger<GoogleDriveHostingProvider>>().Object);
            vm.SelectedHostingProvider = googleProvider;
            vm.GoogleClientId = string.Empty;
            vm.GoogleClientSecret = string.Empty;

            // Act
            await vm.AuthenticateCommand.ExecuteAsync(null);

            // Assert
            Assert.False(vm.IsAuthenticating);
            Assert.Contains("Google Drive requires client credentials", vm.AuthenticationStatusMessage);
            _mockNotificationService.Verify(
                n => n.ShowWarning("Google Drive Credentials Needed", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("GENHUB_GOOGLE_CLIENT_ID", originalId);
            System.Environment.SetEnvironmentVariable("GENHUB_GOOGLE_CLIENT_SECRET", originalSecret);
        }
    }

    /// <summary>
    /// Tests that PublisherStudioDialogService delegates confirmation to IDialogService.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test operation.</returns>
    [Fact]
    public async Task PublisherStudioDialogService_ShowConfirmationAsync_DelegatesToIDialogService()
    {
        // Arrange
        var mockAppDialogService = new Mock<IDialogService>();
        mockAppDialogService
            .Setup(d => d.ShowConfirmationAsync(
                "Test Title",
                "Test Message",
                "Yes",
                "No",
                "SessionKey"))
            .ReturnsAsync(true);

        var dialogService = new PublisherStudioDialogService(mockAppDialogService.Object);

        // Act
        var result = await dialogService.ShowConfirmationAsync(
            "Test Title",
            "Test Message",
            "Yes",
            "No",
            "SessionKey");

        // Assert
        Assert.True(result);
        mockAppDialogService.Verify(
            d => d.ShowConfirmationAsync(
                "Test Title",
                "Test Message",
                "Yes",
                "No",
                "SessionKey"),
            Times.Once);
    }
}
