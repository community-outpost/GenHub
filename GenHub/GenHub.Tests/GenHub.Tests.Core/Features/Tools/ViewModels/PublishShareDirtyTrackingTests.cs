using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services.Hosting;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for per-catalog dirty tracking, remote catalog cleanup, and
/// Google Drive session restore in <see cref="PublishShareViewModel"/>.
/// </summary>
public class PublishShareDirtyTrackingTests
{
    private readonly Mock<IPublisherStudioService> _mockStudioService = new();
    private readonly Mock<IHostingStateManager> _mockHostingStateManager = new();
    private readonly Mock<INotificationService> _mockNotificationService = new();

    /// <summary>
    /// Marking a catalog changed must flag its status so its publish action re-enables.
    /// </summary>
    [Fact]
    public void MarkCatalogChanged_SetsHasChangesAndNeedsPublish()
    {
        var project = CreateProject("cat-1", "cat-2");
        var vm = CreateViewModel(project);
        var status = vm.CatalogStatuses.First(s => s.Catalog.Id == "cat-1");
        status.IsPublished = true;
        status.HasChanges = false;
        Assert.False(status.NeedsPublish);

        vm.MarkCatalogChanged("cat-1");

        Assert.True(status.HasChanges);
        Assert.True(status.NeedsPublish);
        Assert.True(vm.AnyCatalogNeedsPublish);
        Assert.True(vm.CatalogNeedsPublish("cat-1"));
        Assert.True(vm.CatalogNeedsPublish("unknown-catalog"));
    }

    /// <summary>
    /// Project-wide edits must mark every catalog changed.
    /// </summary>
    [Fact]
    public void MarkAllCatalogsChanged_MarksEveryCatalog()
    {
        var project = CreateProject("cat-1", "cat-2");
        var vm = CreateViewModel(project);
        foreach (var status in vm.CatalogStatuses)
        {
            status.IsPublished = true;
            status.HasChanges = false;
        }

        vm.MarkAllCatalogsChanged();

        Assert.All(vm.CatalogStatuses, s => Assert.True(s.HasChanges));
        Assert.True(vm.AnyCatalogNeedsPublish);
    }

    /// <summary>
    /// Removing a catalog must delete its remote file on authenticated providers
    /// and prune the hosting-state entry.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteCatalogRemotes_AuthenticatedProvider_DeletesAndPrunesEntryAsync()
    {
        var hostingState = new HostingState
        {
            Catalogs =
            [
                new()
                {
                    CatalogId = "cat-1",
                    CatalogName = "Catalog 1",
                    FileName = "catalog-1.json",
                    FileId = "remote-file-id",
                    Url = "https://example.com/catalog-1.json",
                },
            ],
        };
        SetupStateManager("/test/path/project.json", hostingState, HostingConstants.GoogleDrive);

        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        mockProvider.Setup(p => p.IsAuthenticated).Returns(true);
        mockProvider.Setup(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var vm = CreateViewModel(project);
        vm.HostingProviders.Add(mockProvider.Object);

        var cleaned = await vm.DeleteCatalogRemotesAsync("cat-1");

        Assert.True(cleaned);
        mockProvider.Verify(p => p.DeleteFileAsync("remote-file-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(hostingState.Catalogs);
    }

    /// <summary>
    /// When the provider is offline at removal time, the remote file survives,
    /// the entry is kept, and the caller is told cleanup failed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteCatalogRemotes_UnauthenticatedProvider_ReturnsFalseKeepsEntryAsync()
    {
        var hostingState = new HostingState
        {
            Catalogs =
            [
                new()
                {
                    CatalogId = "cat-1",
                    CatalogName = "Catalog 1",
                    FileName = "catalog-1.json",
                    FileId = "remote-file-id",
                    Url = "https://example.com/catalog-1.json",
                },
            ],
        };
        SetupStateManager("/test/path/project.json", hostingState, HostingConstants.GoogleDrive);

        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        mockProvider.Setup(p => p.IsAuthenticated).Returns(false);

        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var vm = CreateViewModel(project);
        vm.HostingProviders.Add(mockProvider.Object);

        var cleaned = await vm.DeleteCatalogRemotesAsync("cat-1");

        Assert.False(cleaned);
        mockProvider.Verify(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(hostingState.Catalogs);
    }

    /// <summary>
    /// Never-published catalog entries are pruned locally without a remote call.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteCatalogRemotes_NeverPublishedEntry_PrunesWithoutDeleteAsync()
    {
        var hostingState = new HostingState
        {
            Catalogs =
            [
                new()
                {
                    CatalogId = "cat-1",
                    CatalogName = "Catalog 1",
                    FileName = "catalog-1.json",
                },
            ],
        };
        SetupStateManager("/test/path/project.json", hostingState, HostingConstants.GoogleDrive);

        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        mockProvider.Setup(p => p.IsAuthenticated).Returns(true);

        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var vm = CreateViewModel(project);
        vm.HostingProviders.Add(mockProvider.Object);

        var cleaned = await vm.DeleteCatalogRemotesAsync("cat-1");

        Assert.True(cleaned);
        mockProvider.Verify(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(hostingState.Catalogs);
    }

    /// <summary>
    /// Stored Google Drive client credentials must be restored into the provider
    /// without popping the browser when no OAuth tokens were persisted.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SelectGoogleDrive_WithStoredClientCreds_PrefillsWithoutBrowserAsync()
    {
        var mockCredentialStore = new Mock<IHostingCredentialStore>();
        mockCredentialStore
            .Setup(s => s.GetCredentialAsync(HostingConstants.GoogleDriveClientCredentialKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"ClientId\":\"test-client-id\",\"ClientSecret\":\"test-secret\"}");
        mockCredentialStore
            .Setup(s => s.GetCredentialAsync(
                CredentialStoreDataStore.DefaultKeyPrefix + CredentialStoreDataStore.BrokerUserKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var vm = CreateViewModel(project, mockCredentialStore.Object);
        var drive = new GoogleDriveHostingProvider(NullLogger<GoogleDriveHostingProvider>.Instance);

        vm.SelectedHostingProvider = drive;

        var restored = await WaitForAsync(() => vm.GoogleClientId == "test-client-id", TimeSpan.FromSeconds(5));

        Assert.True(restored);
        Assert.Equal("test-secret", vm.GoogleClientSecret);
        Assert.Equal("test-client-id", drive.CustomClientId);
        Assert.Equal("test-secret", drive.CustomClientSecret);
        Assert.False(drive.IsAuthenticated);
    }

    private static PublisherStudioProject CreateProject(params string[] catalogIds)
    {
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        foreach (var id in catalogIds)
        {
            project.Catalogs.Add(new NamedCatalog
            {
                Id = id,
                Name = id,
                FileName = $"{id}.json",
                Catalog = new PublisherCatalog { Content = [] },
            });
        }

        return project;
    }

    private PublishShareViewModel CreateViewModel(PublisherStudioProject project, IHostingCredentialStore? credentialStore = null)
    {
        return new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            NullLogger<PublishShareViewModel>.Instance,
            hostingStateManager: _mockHostingStateManager.Object,
            notificationService: _mockNotificationService.Object,
            credentialStore: credentialStore);
    }

    private void SetupStateManager(string projectPath, HostingState hostingState, string providerId)
    {
        var container = new PublisherHostingStates
        {
            States = { [providerId] = hostingState },
        };
        _mockHostingStateManager
            .Setup(m => m.LoadStatesAsync(projectPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherHostingStates>.CreateSuccess(container));
        _mockHostingStateManager
            .Setup(m => m.SaveStatesAsync(projectPath, It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
    }

    private async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!condition() && DateTime.UtcNow - start < timeout)
        {
            await Task.Delay(50);
        }

        return condition();
    }
}
