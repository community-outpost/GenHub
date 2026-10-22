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
/// Regression tests proving that explicit null media collections from deserialized
/// catalog JSON never break the publish pipeline.
/// </summary>
public class PublishShareExplicitNullMediaTests
{
    private readonly Mock<IPublisherStudioService> _mockStudioService = new();
    private readonly Mock<ILogger<PublishShareViewModel>> _mockPublishLogger = new();
    private readonly Mock<IHostingStateManager> _mockHostingStateManager = new();
    private readonly Mock<INotificationService> _mockNotificationService = new();

    /// <summary>
    /// Content items whose release and metadata media collections were explicitly nulled
    /// (for example by catalog JSON containing null arrays) must publish without throwing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_ExplicitNullMediaCollections_PublishesCatalogAsync()
    {
        var projectDir = Directory.CreateTempSubdirectory("genhub-null-media-").FullName;
        try
        {
            var item = new CatalogContentItem
            {
                Id = "content-null",
                Name = "Content",
                Releases = null!,
                AddonReleases = null!,
                Metadata = null,
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
            var viewModel = CreatePublishShareViewModel(project, mockProvider.Object);

            await viewModel.PublishCatalogCommand.ExecuteAsync(catalog);

            Assert.Contains("catalog", callOrder);
            mockProvider.Verify(
                p => p.UploadFileAsync(
                    It.IsAny<Stream>(),
                    It.Is<string>(name => name.Contains("-media-", StringComparison.OrdinalIgnoreCase)),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()),
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
        var viewModel = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);
        viewModel.HostingProviders.Add(provider);
        viewModel.SelectedHostingProvider = provider;
        return viewModel;
    }
}
