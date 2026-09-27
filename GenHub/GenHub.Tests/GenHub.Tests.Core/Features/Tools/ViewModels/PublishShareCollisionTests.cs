using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services.Hosting;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Tests that catalog publishing resolves remote filename collisions and never
/// deletes remote files still referenced by another catalog in <see cref="PublishShareViewModel"/>.
/// </summary>
public class PublishShareCollisionTests
{
    private readonly Mock<IPublisherStudioService> _mockStudioService = new();
    private readonly Mock<ILogger<PublishShareViewModel>> _mockPublishLogger = new();
    private readonly Mock<IHostingStateManager> _mockHostingStateManager = new();
    private readonly Mock<INotificationService> _mockNotificationService = new();
    private readonly Mock<IPublisherSubscriptionStore> _mockSubscriptionStore = new();
    private readonly List<string?> _uploadedCatalogFiles = [];

    /// <summary>
    /// A catalog whose filename collides with another catalog must upload under
    /// its catalog-ID fallback instead of overwriting the other catalog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_CollidingFileName_UploadsUnderCatalogIdFallbackAsync()
    {
        var catalogA = CreateCatalog("a", "Alpha", "catalog.json");
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            Catalogs = [catalogA, CreateCatalog("b", "Beta", "catalog.json")],
        };
        var (vm, _) = CreateViewModel(project);

        await vm.PublishCatalogCommand.ExecuteAsync(catalogA);

        Assert.Equal(["catalog-a.json"], _uploadedCatalogFiles);
    }

    /// <summary>
    /// When both the original filename and the catalog-ID fallback are taken,
    /// the upload filename must increment until it is unused.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_FallbackFileNameTaken_IncrementsUntilUnusedAsync()
    {
        var catalogA = CreateCatalog("a", "Alpha", "shared.json");
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            Catalogs =
            [
                catalogA,
                CreateCatalog("b", "Beta", "shared.json"),
                CreateCatalog("c", "Gamma", "catalog-a.json"),
                CreateCatalog("d", "Delta", "catalog-a-2.json"),
            ],
        };
        var (vm, _) = CreateViewModel(project);

        await vm.PublishCatalogCommand.ExecuteAsync(catalogA);

        Assert.Equal(["catalog-a-3.json"], _uploadedCatalogFiles);
    }

    /// <summary>
    /// Publishing a catalog whose remote file ID is still shared with another
    /// catalog must upload fresh without updating or deleting the shared file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_SharedFileId_SkipsOrphanDeletionAsync()
    {
        const string sharedFileId = "shared-remote-id";
        var catalogA = CreateCatalog("a", "Alpha", "catalog-a.json");
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            Catalogs = [catalogA, CreateCatalog("b", "Beta", "catalog-b.json")],
        };
        var hostingState = new HostingState
        {
            ProviderId = HostingConstants.Dropbox,
            Catalogs =
            [
                new() { CatalogId = "a", CatalogName = "Alpha", FileName = "catalog-a.json", FileId = sharedFileId },
                new() { CatalogId = "b", CatalogName = "Beta", FileName = "catalog-b.json", FileId = sharedFileId },
            ],
        };
        var container = new PublisherHostingStates
        {
            States = { [HostingConstants.Dropbox] = hostingState },
        };
        var (vm, mockProvider) = CreateViewModel(project, container, supportsUpdate: true);

        await vm.InitializeAsync();
        await vm.PublishCatalogCommand.ExecuteAsync(catalogA);

        mockProvider.Verify(
            p => p.UpdateFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mockProvider.Verify(p => p.DeleteFileAsync(sharedFileId, It.IsAny<CancellationToken>()), Times.Never);
        mockProvider.Verify(
            p => p.UploadCatalogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(sharedFileId, hostingState.Catalogs.Single(c => c.CatalogId == "b").FileId);
        Assert.NotEqual(sharedFileId, hostingState.Catalogs.Single(c => c.CatalogId == "a").FileId);
    }

    private static NamedCatalog CreateCatalog(string id, string name, string fileName) => new()
    {
        Id = id,
        Name = name,
        FileName = fileName,
        Catalog = new PublisherCatalog(),
    };

    private (PublishShareViewModel ViewModel, Mock<IHostingProvider> Provider) CreateViewModel(
        PublisherStudioProject project,
        PublisherHostingStates? hostingStates = null,
        bool supportsUpdate = false)
    {
        project.Catalog.Publisher.Id = "publisher-test";
        _mockSubscriptionStore.Setup(store => store.GetSubscriptionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(null));

        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.Dropbox);
        mockProvider.Setup(p => p.DisplayName).Returns("Dropbox");
        mockProvider.Setup(p => p.RequiresAuthentication).Returns(false);
        mockProvider.Setup(p => p.IsAuthenticated).Returns(true);
        mockProvider.Setup(p => p.SupportsArtifactHosting).Returns(true);
        mockProvider.Setup(p => p.SupportsCatalogHosting).Returns(true);
        mockProvider.Setup(p => p.SupportsUpdate).Returns(supportsUpdate);
        mockProvider.Setup(p => p.UploadCatalogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string json, string publisherId, string? fileName, IProgress<int>? progress, CancellationToken ct) =>
            {
                _uploadedCatalogFiles.Add(fileName);
                return OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                {
                    FileId = $"id-{fileName}",
                    PublicUrl = $"https://dl.dropboxusercontent.com/s/x/{fileName}",
                    DirectDownloadUrl = $"https://dl.dropboxusercontent.com/s/x/{fileName}",
                    FileSize = 10,
                });
            });
        mockProvider.Setup(p => p.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream s, string name, string? folder, IProgress<int>? progress, CancellationToken ct) =>
                OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                {
                    FileId = $"id-{name}",
                    PublicUrl = $"https://dl.dropboxusercontent.com/s/x/{name}",
                    DirectDownloadUrl = $"https://dl.dropboxusercontent.com/s/x/{name}",
                    FileSize = 8,
                }));
        mockProvider.Setup(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _mockStudioService.Setup(m => m.ValidateCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _mockStudioService.Setup(m => m.ExportCatalogAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<NamedCatalog?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("{\"catalog\":true}"));
        _mockStudioService.Setup(m => m.ExportProviderDefinitionAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("{\"definition\":true}"));
        _mockStudioService.Setup(m => m.GenerateSubscriptionUrl(It.IsAny<string>())).Returns("genhub://subscribe/test");
        _mockHostingStateManager.Setup(m => m.SaveStatesAsync(It.IsAny<string>(), It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        if (hostingStates != null)
        {
            _mockHostingStateManager.Setup(m => m.LoadStatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<PublisherHostingStates>.CreateSuccess(hostingStates));
        }

        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object,
            subscriptionStore: _mockSubscriptionStore.Object);
        vm.HostingProviders.Add(mockProvider.Object);
        vm.SelectedHostingProvider = mockProvider.Object;
        return (vm, mockProvider);
    }
}
