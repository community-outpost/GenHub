using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for single-artifact upload guards, catalog rename cleanup,
/// cloud merge de-duplication, and deferred batch persistence in <see cref="PublishShareViewModel"/>.
/// </summary>
public class PublishShareUploadFixTests
{
    private readonly Mock<IPublisherStudioService> _mockStudioService = new();
    private readonly Mock<ILogger<PublishShareViewModel>> _mockPublishLogger = new();
    private readonly Mock<IHostingStateManager> _mockHostingStateManager = new();
    private readonly Mock<INotificationService> _mockNotificationService = new();

    /// <summary>
    /// The Content-Library single-upload path must refuse binary uploads on providers
    /// that only host catalog metadata, matching the batch publish pipeline guard.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadArtifactFromLibraryAsync_MetadataOnlyProvider_BlocksUploadAsync()
    {
        var artifact = new ReleaseArtifact
        {
            Filename = "mod.zip",
            LocalFilePath = "/nonexistent/mod.zip",
            DownloadUrl = string.Empty,
        };
        var project = CreateProjectWithArtifacts(artifact);

        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.GitHub);
        mockProvider.Setup(p => p.DisplayName).Returns("GitHub Gists");
        mockProvider.Setup(p => p.RequiresAuthentication).Returns(false);
        mockProvider.Setup(p => p.IsAuthenticated).Returns(true);
        mockProvider.Setup(p => p.SupportsArtifactHosting).Returns(false);
        mockProvider.Setup(p => p.SupportsCatalogHosting).Returns(true);

        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);
        vm.SelectedHostingProvider = mockProvider.Object;

        await vm.UploadArtifactFromLibraryAsync(artifact);

        mockProvider.Verify(
            p => p.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockNotificationService.Verify(
            n => n.ShowError("Incompatible Provider", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        Assert.True(string.IsNullOrEmpty(artifact.DownloadUrl));
    }

    /// <summary>
    /// Renaming a catalog must delete the pre-rename remote file on authenticated providers
    /// and reset the entry to pending so the next publish uploads fresh.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task RenameCatalogInHostingStateAsync_AuthenticatedProvider_DeletesOldRemoteFileAsync()
    {
        var hostingState = new HostingState
        {
            Catalogs =
            [
                new()
                {
                    CatalogId = "old-cat-id",
                    CatalogName = "Old Name",
                    FileName = "catalog-old.json",
                    FileId = "old-remote-id",
                    Url = "https://example.com/catalog-old.json",
                    FileSize = 42,
                },
            ],
        };
        var container = new PublisherHostingStates
        {
            States = { [HostingConstants.GoogleDrive] = hostingState },
        };
        SetupStateManager("/test/path/project.json", container);

        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        mockProvider.Setup(p => p.IsAuthenticated).Returns(true);
        mockProvider.Setup(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);
        vm.HostingProviders.Add(mockProvider.Object);

        await vm.RenameCatalogInHostingStateAsync("old-cat-id", "new-cat-id", "New Name", "catalog-new.json");

        mockProvider.Verify(p => p.DeleteFileAsync("old-remote-id", It.IsAny<CancellationToken>()), Times.Once);
        var renamed = hostingState.Catalogs.Find(c => c.CatalogId == "new-cat-id");
        Assert.NotNull(renamed);
        Assert.Equal("catalog-new.json", renamed.FileName);
        Assert.True(string.IsNullOrEmpty(renamed.FileId));
        Assert.True(string.IsNullOrEmpty(renamed.Url));
        _mockHostingStateManager.Verify(
            m => m.SaveStatesAsync("/test/path/project.json", It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// When the provider cannot be reached at rename time, no remote delete is attempted
    /// and the entry keeps its URL for orphan cleanup on the next publish.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task RenameCatalogInHostingStateAsync_UnauthenticatedProvider_SkipsRemoteDeleteAsync()
    {
        var hostingState = new HostingState
        {
            Catalogs =
            [
                new()
                {
                    CatalogId = "old-cat-id",
                    CatalogName = "Old Name",
                    FileName = "catalog-old.json",
                    FileId = "old-remote-id",
                    Url = "https://example.com/catalog-old.json",
                },
            ],
        };
        var container = new PublisherHostingStates
        {
            States = { [HostingConstants.GoogleDrive] = hostingState },
        };
        SetupStateManager("/test/path/project.json", container);

        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        mockProvider.Setup(p => p.IsAuthenticated).Returns(false);

        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);
        vm.HostingProviders.Add(mockProvider.Object);

        await vm.RenameCatalogInHostingStateAsync("old-cat-id", "new-cat-id", "New Name", "catalog-new.json");

        mockProvider.Verify(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var renamed = hostingState.Catalogs.Find(c => c.CatalogId == "new-cat-id");
        Assert.NotNull(renamed);
        Assert.Equal("https://example.com/catalog-old.json", renamed.Url);
        _mockHostingStateManager.Verify(
            m => m.SaveStatesAsync("/test/path/project.json", It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// A cloud scan that still finds the pre-rename catalog file must merge it into the
    /// renamed entry instead of resurrecting it as a ghost catalog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanCloudStorageCommand_PreRenameRemoteFile_DoesNotResurrectGhostAsync()
    {
        var catalog = new NamedCatalog
        {
            Id = "new-cat-id",
            Name = "New Name",
            FileName = "catalog-new.json",
            Catalog = new PublisherCatalog(),
        };
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            Catalogs = [catalog],
        };
        var hostingState = new HostingState
        {
            ProviderId = HostingConstants.Dropbox,
            Catalogs =
            [
                new()
                {
                    CatalogId = "new-cat-id",
                    CatalogName = "New Name",
                    FileName = "catalog-new.json",
                    FileId = "remote-file-1",
                    Url = "https://example.com/catalog-old.json",
                    LastUpdated = DateTime.UtcNow,
                },
            ],
        };
        var container = new PublisherHostingStates
        {
            States = { [HostingConstants.Dropbox] = hostingState },
        };
        SetupStateManager("/test/path/project.json", container);

        var cloudState = new HostingState
        {
            ProviderId = HostingConstants.Dropbox,
            Catalogs =
            [
                new()
                {
                    CatalogId = "old-cat-id",
                    CatalogName = "old-cat-id",
                    FileName = "catalog-old.json",
                    FileId = "remote-file-1",
                    Url = "https://example.com/catalog-old.json",
                    LastUpdated = DateTime.UtcNow.AddHours(-2),
                },
            ],
        };
        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.Dropbox);
        mockProvider.Setup(p => p.DisplayName).Returns("Dropbox");
        mockProvider.Setup(p => p.IsAuthenticated).Returns(true);
        mockProvider.Setup(p => p.RecoverHostingStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<HostingState?>.CreateSuccess(cloudState));

        _mockStudioService.Setup(m => m.ValidateCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);
        vm.HostingProviders.Add(mockProvider.Object);
        vm.SelectedHostingProvider = mockProvider.Object;
        await vm.InitializeAsync();

        await vm.ScanCloudStorageCommand.ExecuteAsync(null);

        var current = vm.CurrentHostingState;
        Assert.NotNull(current);
        Assert.Single(current.Catalogs);
        Assert.Equal("new-cat-id", current.Catalogs[0].CatalogId);
        Assert.Equal("catalog-new.json", current.Catalogs[0].FileName);
    }

    /// <summary>
    /// Publishing several pending artifacts must persist hosting state once for the whole
    /// batch instead of once per artifact.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_BatchArtifacts_SavesStateOncePerLoopAsync()
    {
        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile1, "artifact-one");
            await File.WriteAllTextAsync(tempFile2, "artifact-two");

            var artifact1 = new ReleaseArtifact { Filename = "one.zip", LocalFilePath = tempFile1, DownloadUrl = string.Empty };
            var artifact2 = new ReleaseArtifact { Filename = "two.zip", LocalFilePath = tempFile2, DownloadUrl = string.Empty };
            var catalog = new NamedCatalog
            {
                Id = "cat-id",
                Name = "Catalog",
                FileName = "catalog-cat-id.json",
                Catalog = new PublisherCatalog
                {
                    Content =
                    [
                        new CatalogContentItem
                        {
                            Id = "content-1",
                            Name = "Content",
                            Releases =
                            [
                                new ContentRelease
                                {
                                    Version = "1.0.0",
                                    Artifacts = [artifact1, artifact2],
                                },
                            ],
                        },
                    ],
                },
            };
            var project = new PublisherStudioProject
            {
                ProjectPath = "/test/path/project.json",
                Catalogs = [catalog],
            };
            var mockProvider = new Mock<IHostingProvider>();
            mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.Dropbox);
            mockProvider.Setup(p => p.DisplayName).Returns("Dropbox");
            mockProvider.Setup(p => p.RequiresAuthentication).Returns(false);
            mockProvider.Setup(p => p.IsAuthenticated).Returns(true);
            mockProvider.Setup(p => p.SupportsArtifactHosting).Returns(true);
            mockProvider.Setup(p => p.SupportsCatalogHosting).Returns(true);
            mockProvider.Setup(p => p.SupportsUpdate).Returns(false);
            mockProvider.Setup(p => p.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Stream s, string name, string? folder, IProgress<int>? progress, CancellationToken ct) =>
                    OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                    {
                        FileId = $"id-{name}",
                        PublicUrl = $"https://www.dropbox.com/s/x/{name}?dl=0",
                        DirectDownloadUrl = $"https://dl.dropboxusercontent.com/s/x/{name}",
                        FileSize = 12,
                    }));
            mockProvider.Setup(p => p.UploadCatalogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                {
                    FileId = "id-catalog",
                    PublicUrl = "https://www.dropbox.com/s/x/catalog-cat-id.json?dl=0",
                    DirectDownloadUrl = "https://dl.dropboxusercontent.com/s/x/catalog-cat-id.json",
                    FileSize = 10,
                }));

            _mockStudioService.Setup(m => m.ValidateCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
            _mockStudioService.Setup(m => m.ExportCatalogAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<NamedCatalog?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("{\"catalog\":true}"));
            _mockStudioService.Setup(m => m.ExportProviderDefinitionAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("{\"definition\":true}"));
            _mockStudioService.Setup(m => m.GenerateSubscriptionUrl(It.IsAny<string>())).Returns("genhub://subscribe/test");
            _mockHostingStateManager.Setup(m => m.SaveStatesAsync(It.IsAny<string>(), It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

            var vm = new PublishShareViewModel(
                project,
                _mockStudioService.Object,
                _mockPublishLogger.Object,
                null,
                _mockHostingStateManager.Object,
                _mockNotificationService.Object);
            vm.HostingProviders.Add(mockProvider.Object);
            vm.SelectedHostingProvider = mockProvider.Object;

            await vm.PublishCatalogCommand.ExecuteAsync(catalog);

            Assert.False(string.IsNullOrEmpty(artifact1.DownloadUrl));
            Assert.False(string.IsNullOrEmpty(artifact2.DownloadUrl));
            _mockHostingStateManager.Verify(
                m => m.SaveStatesAsync("/test/path/project.json", It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()),
                Times.Exactly(3));
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }

    private static PublisherStudioProject CreateProjectWithArtifacts(params ReleaseArtifact[] artifacts)
    {
        var catalog = new NamedCatalog
        {
            Id = "cat-id",
            Name = "Catalog",
            FileName = "catalog-cat-id.json",
            Catalog = new PublisherCatalog
            {
                Content =
                [
                    new CatalogContentItem
                    {
                        Id = "content-1",
                        Name = "Content",
                        Releases =
                        [
                            new ContentRelease
                            {
                                Version = "1.0.0",
                                Artifacts = new List<ReleaseArtifact>(artifacts),
                            },
                        ],
                    },
                ],
            },
        };

        return new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            Catalogs = [catalog],
        };
    }

    private void SetupStateManager(string projectPath, PublisherHostingStates container)
    {
        _mockHostingStateManager.Setup(m => m.LoadStatesAsync(projectPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherHostingStates>.CreateSuccess(container));
        _mockHostingStateManager.Setup(m => m.SaveStatesAsync(projectPath, It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
    }
}
