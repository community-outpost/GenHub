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
using System.Collections.Generic;
using System.Linq;
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
    private HttpMessageHandler? _httpHandler;
    private HttpClient? _httpClient;

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient?.Dispose();
        _httpHandler?.Dispose();
        _httpClient = null;
        _httpHandler = null;
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

    /// <summary>
    /// A scan that discovers a publisher definition without a shareable URL (for example a
    /// Dropbox file with no shared link yet) must resolve a download URL on demand and still
    /// restore the publisher profile instead of leaving it empty.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanCloudStorage_DefinitionWithoutShareableUrl_ResolvesUrlAndAutoRestoresAsync()
    {
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        project.Catalogs.Add(new NamedCatalog
        {
            Id = "default",
            Name = "Content",
            FileName = "catalog.json",
            Catalog = project.Catalog,
        });
        var vm = await CreateScannedViewModelAsync(
            project,
            serveValidContent: true,
            discoveredDefinitionUrl: string.Empty,
            setupUrlResolution: true);

        Assert.Equal("restored-pub", project.Catalog.Publisher.Id);
        Assert.Equal("Restored Publisher", project.Catalog.Publisher.Name);
        Assert.Equal(DefinitionUrl, vm.ProviderDefinitionUrl);
        Assert.False(vm.ShowNoDefinitionBanner);
        var restored = project.Catalogs.Find(c => c.Id == "main");
        Assert.NotNull(restored);
    }

    /// <summary>
    /// A URL-less discovered definition must still surface the restore banner so the synced
    /// provider definition can be pulled into an empty project.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanCloudStorage_DefinitionWithoutShareableUrl_SurfacesRestoreBannerAsync()
    {
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var vm = await CreateScannedViewModelAsync(
            project,
            serveValidContent: false,
            discoveredDefinitionUrl: string.Empty,
            setupUrlResolution: true);

        Assert.True(vm.HasDiscoveredCloudDefinition);
        Assert.True(vm.ShowDiscoveredDefinitionBanner);
        Assert.NotNull(vm.DiscoveredCloudDefinition);
        Assert.True(vm.DiscoveredCloudDefinition.CanLoadToProject);
    }

    /// <summary>
    /// Restoring cloud catalogs must only drop the migrated empty "default" placeholder,
    /// never a legitimate empty catalog with its own identity.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanCloudStorage_AutoRestore_PreservesNonDefaultEmptyCatalogAsync()
    {
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        project.Catalogs.Add(new NamedCatalog
        {
            Id = "custom-empty",
            Name = "Custom Empty",
            FileName = "catalog-custom.json",
            Catalog = project.Catalog,
        });
        await CreateScannedViewModelAsync(project, serveValidContent: true);

        Assert.Equal("restored-pub", project.Catalog.Publisher.Id);
        Assert.Contains(project.Catalogs, c => c.Id == "custom-empty");
        Assert.NotNull(project.Catalogs.Find(c => c.Id == "main"));
    }

    /// <summary>
    /// A resolved definition URL must survive a rescan that again reports the definition
    /// without a URL, so the restore does not flap between scans.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanCloudStorage_ResolvedDefinitionUrlSurvivesRescanAsync()
    {
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        project.Catalogs.Add(new NamedCatalog
        {
            Id = "default",
            Name = "Content",
            FileName = "catalog.json",
            Catalog = project.Catalog,
        });
        var savedDefinitionUrls = new List<string>();
        var vm = await CreateScannedViewModelAsync(
            project,
            serveValidContent: true,
            discoveredDefinitionUrl: string.Empty,
            setupUrlResolution: true,
            onSave: states =>
            {
                if (states.States.TryGetValue(HostingConstants.GoogleDrive, out var state))
                {
                    savedDefinitionUrls.Add(state.Definitions.FirstOrDefault()?.Url ?? string.Empty);
                }
            });

        await vm.ScanCloudStorageCommand.ExecuteAsync(null);

        Assert.Equal("restored-pub", project.Catalog.Publisher.Id);
        Assert.Equal(
            new[] { string.Empty, DefinitionUrl, DefinitionUrl },
            savedDefinitionUrls);
    }

    private async Task<PublishShareViewModel> CreateScannedViewModelAsync(
        PublisherStudioProject project,
        bool serveValidContent,
        string discoveredDefinitionUrl = DefinitionUrl,
        bool setupUrlResolution = false,
        Action<PublisherHostingStates>? onSave = null)
    {
        var container = new PublisherHostingStates();
        _mockHostingStateManager.Setup(m => m.LoadStatesAsync(project.ProjectPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherHostingStates>.CreateSuccess(container));
        _mockHostingStateManager.Setup(m => m.SaveStatesAsync(project.ProjectPath, It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()))
            .Callback((string _, PublisherHostingStates states, CancellationToken _) => onSave?.Invoke(states))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var cloudState = new HostingState
        {
            ProviderId = HostingConstants.GoogleDrive,
            Definition = new HostedFileInfo
            {
                FileName = HostingConstants.DefaultDefinitionFileName,
                Url = discoveredDefinitionUrl,
                LastUpdated = DateTime.UtcNow,
            },
            Definitions =
            [
                new HostedFileInfo
                {
                    FileId = "id:publisher-definition",
                    FileName = HostingConstants.DefaultDefinitionFileName,
                    Url = discoveredDefinitionUrl,
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
        if (setupUrlResolution)
        {
            mockProvider.Setup(p => p.EnsureShareableDownloadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess(DefinitionUrl));
        }

        _mockStudioService.Setup(m => m.ValidateCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // The view model holds the override across scans, so the handler and client stay
        // alive for the whole test and are disposed in Dispose.
        _httpClient?.Dispose();
        _httpHandler?.Dispose();
        _httpHandler = new ServeDefinitionAndCatalogHandler(serveValidContent);
        _httpClient = new HttpClient(_httpHandler);
        PublishShareViewModel.HttpClientOverrideForTesting = _httpClient;
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
