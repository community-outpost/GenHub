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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Regression tests for gallery media (screenshots and videos) uploads in <see cref="PublishShareViewModel"/>.
/// Local media referenced by content metadata must be hosted on publish, matching release artifacts and artwork.
/// </summary>
public class PublishShareMediaUploadTests
{
    private readonly Mock<IPublisherStudioService> _mockStudioService = new();
    private readonly Mock<ILogger<PublishShareViewModel>> _mockPublishLogger = new();
    private readonly Mock<IHostingStateManager> _mockHostingStateManager = new();
    private readonly Mock<INotificationService> _mockNotificationService = new();

    /// <summary>
    /// Local screenshot and video entries (absolute paths and file:// URIs) must be uploaded
    /// to the hosting provider and rewritten to remote URLs so subscribers can play them.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_LocalGalleryMedia_UploadedAndRewrittenAsync()
    {
        var projectDir = Directory.CreateTempSubdirectory("genhub-media-").FullName;
        var outsideVideo = Path.Combine(Path.GetTempPath(), $"genhub-media-{Guid.NewGuid():N}.mp4");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(projectDir, "shot.png"), [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(outsideVideo, [5, 6, 7, 8]);
            var item = new CatalogContentItem
            {
                Id = "content-1",
                Name = "Content",
                Metadata = new ContentRichMetadata
                {
                    ScreenshotUrls = ["shot.png"],
                    VideoUrls = [new Uri(outsideVideo).AbsoluteUri],
                },
            };
            var catalog = new NamedCatalog
            {
                Id = "cat",
                Name = "Cat",
                Catalog = new PublisherCatalog { Content = [item] },
            };
            var project = new PublisherStudioProject
            {
                ProjectPath = Path.Combine(projectDir, "project.json"),
                Catalogs = [catalog],
            };

            var callOrder = new List<string>();
            var mockProvider = CreateRecordingMediaProvider(callOrder);
            SetupCatalogServiceMocks();
            var vm = CreatePublishShareViewModel(project, mockProvider.Object);

            await vm.PublishCatalogCommand.ExecuteAsync(catalog);

            Assert.Contains("catalog", callOrder);
            var catalogIndex = callOrder.IndexOf("catalog");
            Assert.True(catalogIndex > 0);
            Assert.All(callOrder.Take(catalogIndex), entry => Assert.StartsWith("media:", entry, StringComparison.OrdinalIgnoreCase));

            mockProvider.Verify(
                p => p.UploadFileAsync(
                    It.IsAny<Stream>(),
                    It.Is<string>(name => name.Contains("shot.png", StringComparison.OrdinalIgnoreCase)),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            mockProvider.Verify(
                p => p.UploadFileAsync(
                    It.IsAny<Stream>(),
                    It.Is<string>(name => name.Contains(Path.GetFileName(outsideVideo), StringComparison.OrdinalIgnoreCase)),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.StartsWith("https://", item.Metadata?.ScreenshotUrls[0]);
            Assert.StartsWith("https://", item.Metadata?.VideoUrls[0]);
        }
        finally
        {
            File.Delete(outsideVideo);
            Directory.Delete(projectDir, true);
        }
    }

    /// <summary>
    /// Providers that only host catalog metadata must refuse to publish while gallery media
    /// is still local, instead of shipping file:// URLs that subscribers cannot open.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_PendingMediaOnMetadataOnlyProvider_BlocksUploadAsync()
    {
        var projectDir = Directory.CreateTempSubdirectory("genhub-media-").FullName;
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(projectDir, "shot.png"), [1, 2, 3, 4]);
            var item = new CatalogContentItem
            {
                Id = "content-1",
                Name = "Content",
                Metadata = new ContentRichMetadata { ScreenshotUrls = ["shot.png"] },
            };
            var catalog = new NamedCatalog
            {
                Id = "cat",
                Name = "Cat",
                Catalog = new PublisherCatalog { Content = [item] },
            };
            var project = new PublisherStudioProject
            {
                ProjectPath = Path.Combine(projectDir, "project.json"),
                Catalogs = [catalog],
            };

            var mockProvider = new Mock<IHostingProvider>();
            mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.GitHub);
            mockProvider.Setup(p => p.DisplayName).Returns("GitHub Gists");
            mockProvider.Setup(p => p.RequiresAuthentication).Returns(false);
            mockProvider.Setup(p => p.IsAuthenticated).Returns(true);
            mockProvider.Setup(p => p.SupportsArtifactHosting).Returns(false);
            mockProvider.Setup(p => p.SupportsCatalogHosting).Returns(true);
            SetupCatalogServiceMocks();
            var vm = CreatePublishShareViewModel(project, mockProvider.Object);

            await vm.PublishCatalogCommand.ExecuteAsync(catalog);

            mockProvider.Verify(
                p => p.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            mockProvider.Verify(
                p => p.UploadCatalogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.StartsWith("shot.png", item.Metadata?.ScreenshotUrls[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectDir, true);
        }
    }

    /// <summary>
    /// Null media lists (for example from catalog JSON that explicitly nulls the arrays)
    /// must be skipped by the media stage instead of failing the publish.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_NullMediaLists_PublishesWithoutThrowingAsync()
    {
        var projectDir = Directory.CreateTempSubdirectory("genhub-media-").FullName;
        try
        {
            var item = new CatalogContentItem
            {
                Id = "content-1",
                Name = "Content",
                Metadata = new ContentRichMetadata { ScreenshotUrls = null!, VideoUrls = null! },
                Releases = [new ContentRelease { ImageUrls = null!, VideoUrls = null! }],
            };
            var catalog = new NamedCatalog
            {
                Id = "cat",
                Name = "Cat",
                Catalog = new PublisherCatalog { Content = [item] },
            };
            var project = new PublisherStudioProject
            {
                ProjectPath = Path.Combine(projectDir, "project.json"),
                Catalogs = [catalog],
            };

            var mockProvider = CreateRecordingMediaProvider(new List<string>());
            SetupCatalogServiceMocks();
            var vm = CreatePublishShareViewModel(project, mockProvider.Object);

            var exception = await Record.ExceptionAsync(() => vm.PublishCatalogCommand.ExecuteAsync(catalog));

            Assert.Null(exception);
            mockProvider.Verify(
                p => p.UploadFileAsync(It.IsAny<Stream>(), It.Is<string>(name => name.Contains("-media-", StringComparison.OrdinalIgnoreCase)), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Directory.Delete(projectDir, true);
        }
    }

    private Mock<IHostingProvider> CreateRecordingMediaProvider(List<string> callOrder)
    {
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
            {
                callOrder.Add($"media:{name}");
                return OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                {
                    FileId = $"id-{name}",
                    PublicUrl = $"https://dl.dropboxusercontent.com/s/x/{name}",
                    DirectDownloadUrl = $"https://dl.dropboxusercontent.com/s/x/{name}",
                    FileSize = 8,
                });
            });
        mockProvider.Setup(p => p.UploadCatalogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string json, string publisherId, string? fileName, IProgress<int>? progress, CancellationToken ct) =>
            {
                callOrder.Add("catalog");
                return OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                {
                    FileId = "id-catalog",
                    PublicUrl = "https://dl.dropboxusercontent.com/s/x/catalog.json",
                    DirectDownloadUrl = "https://dl.dropboxusercontent.com/s/x/catalog.json",
                    FileSize = 10,
                });
            });
        return mockProvider;
    }

    private void SetupCatalogServiceMocks()
    {
        _mockStudioService.Setup(m => m.ValidateCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _mockStudioService.Setup(m => m.ExportCatalogAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<NamedCatalog?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("{\"catalog\":true}"));
        _mockStudioService.Setup(m => m.ExportProviderDefinitionAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("{\"definition\":true}"));
        _mockStudioService.Setup(m => m.GenerateSubscriptionUrl(It.IsAny<string>())).Returns("genhub://subscribe/test");
        _mockHostingStateManager.Setup(m => m.SaveStatesAsync(It.IsAny<string>(), It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
    }

    private PublishShareViewModel CreatePublishShareViewModel(PublisherStudioProject project, IHostingProvider provider)
    {
        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);
        vm.HostingProviders.Add(provider);
        vm.SelectedHostingProvider = provider;
        return vm;
    }
}
