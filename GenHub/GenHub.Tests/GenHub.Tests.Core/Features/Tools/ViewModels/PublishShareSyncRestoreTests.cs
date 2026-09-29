using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services.Hosting;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Regression tests for cloud sync restoring a publisher definition and its catalogs
/// into an empty project (wipe-and-reconnect scenario).
/// </summary>
public sealed class PublishShareSyncRestoreTests : IDisposable
{
    private const string DefinitionUrl = "https://example.com/publisher.json";
    private const string CatalogUrl = "https://example.com/catalog-main.json";

    private readonly Mock<IPublisherStudioService> _mockStudioService = new();
    private readonly Mock<ILogger<PublishShareViewModel>> _mockPublishLogger = new();
    private readonly Mock<IHostingStateManager> _mockHostingStateManager = new();
    private readonly Mock<INotificationService> _mockNotificationService = new();

    /// <inheritdoc />
    public void Dispose()
    {
        PublishShareViewModel.HttpClientOverrideForTesting = null;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
    }

    /// <summary>
    /// After a scan discovers a cloud publisher definition while the local profile is empty,
    /// the definition must surface as loadable so the restore banner and pull command work.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanCloudStorage_EmptyProfileWithCloudDefinition_SurfacesLoadableDefinitionAsync()
    {
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var vm = await CreateScannedViewModelAsync(project, serveValidContent: false);

        Assert.True(vm.HasDiscoveredCloudDefinition);
        Assert.True(vm.ShowDiscoveredDefinitionBanner);
        Assert.NotNull(vm.DiscoveredCloudDefinition);
        Assert.True(vm.DiscoveredCloudDefinition.CanLoadToProject);
    }

    /// <summary>
    /// A scan that finds a cloud publisher definition while the local profile is empty must
    /// automatically restore the publisher profile and referenced catalogs into the project.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanCloudStorage_EmptyProfileWithCloudDefinition_AutoRestoresProfileAndCatalogsAsync()
    {
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        project.Catalogs.Add(new NamedCatalog
        {
            Id = "default",
            Name = "Content",
            FileName = "catalog.json",
            Catalog = project.Catalog,
        });
        await CreateScannedViewModelAsync(project, serveValidContent: true);

        Assert.Equal("restored-pub", project.Catalog.Publisher.Id);
        Assert.Equal("Restored Publisher", project.Catalog.Publisher.Name);
        var restored = project.Catalogs.Find(c => c.Id == "main");
        Assert.NotNull(restored);
        Assert.Single(restored.Catalog.Content);
        Assert.DoesNotContain(project.Catalogs, c => c.Id == "default");
    }

    private async Task<PublishShareViewModel> CreateScannedViewModelAsync(
        PublisherStudioProject project,
        bool serveValidContent)
    {
        var container = new PublisherHostingStates();
        _mockHostingStateManager.Setup(m => m.LoadStatesAsync(project.ProjectPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherHostingStates>.CreateSuccess(container));
        _mockHostingStateManager.Setup(m => m.SaveStatesAsync(project.ProjectPath, It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var cloudState = new HostingState
        {
            ProviderId = HostingConstants.GoogleDrive,
            Definition = new HostedFileInfo
            {
                FileName = HostingConstants.DefaultDefinitionFileName,
                Url = DefinitionUrl,
                LastUpdated = DateTime.UtcNow,
            },
            Definitions =
            [
                new HostedFileInfo
                {
                    FileName = HostingConstants.DefaultDefinitionFileName,
                    Url = DefinitionUrl,
                    LastUpdated = DateTime.UtcNow,
                },
            ],
            Catalogs =
            [
                new CatalogHostingInfo
                {
                    CatalogId = "main",
                    CatalogName = "main",
                    FileName = "catalog-main.json",
                    Url = CatalogUrl,
                    LastUpdated = DateTime.UtcNow,
                },
            ],
        };

        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        mockProvider.Setup(p => p.DisplayName).Returns("Google Drive");
        mockProvider.Setup(p => p.IsAuthenticated).Returns(true);
        mockProvider.Setup(p => p.RequiresAuthentication).Returns(true);
        mockProvider.Setup(p => p.SupportsCatalogHosting).Returns(true);
        mockProvider.Setup(p => p.RecoverHostingStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<HostingState?>.CreateSuccess(cloudState));

        _mockStudioService.Setup(m => m.ValidateCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        using var handler = new ServeDefinitionAndCatalogHandler(serveValidContent);
        using var client = new HttpClient(handler);
        PublishShareViewModel.HttpClientOverrideForTesting = client;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;

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

        return vm;
    }

    private sealed class ServeDefinitionAndCatalogHandler(bool serveValidContent) : HttpMessageHandler
    {
        private const string DefinitionJson = """
            {
                "publisher": { "id": "restored-pub", "name": "Restored Publisher" },
                "catalogs": [ { "id": "main", "name": "Main", "url": "https://example.com/catalog-main.json" } ]
            }
            """;

        private const string CatalogJson = """
            {
                "publisher": { "id": "restored-pub", "name": "Restored Publisher" },
                "content": [ { "id": "content-1", "name": "Restored Map Pack" } ]
            }
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = serveValidContent
                ? (request.RequestUri?.ToString() == CatalogUrl ? CatalogJson : DefinitionJson)
                : string.Empty;
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
