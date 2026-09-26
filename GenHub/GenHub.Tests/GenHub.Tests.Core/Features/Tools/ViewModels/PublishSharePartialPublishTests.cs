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
/// Tests that catalog publishing reports partial and total failures honestly and never
/// uploads a stale provider definition in <see cref="PublishShareViewModel"/>.
/// </summary>
public class PublishSharePartialPublishTests
{
    private const string StaleDefinitionJson = "{\"stale\":true}";

    private readonly Mock<IPublisherStudioService> _mockStudioService = new();
    private readonly Mock<ILogger<PublishShareViewModel>> _mockPublishLogger = new();
    private readonly Mock<IHostingStateManager> _mockHostingStateManager = new();
    private readonly Mock<INotificationService> _mockNotificationService = new();
    private readonly List<string> _definitionUploads = [];

    /// <summary>
    /// A cascade where one of three catalogs fails must warn with the real count, still upload
    /// the definition, and return a failure naming the failed catalog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadProviderDefinitionAsync_OneCatalogFails_ReportsPartialFailureAsync()
    {
        var vm = CreateViewModel(failingCatalogFiles: ["catalog-b.json"]);
        vm.HasDefinitionChanges = false;

        var result = await vm.UploadProviderDefinitionAsync();

        Assert.False(result.Success);
        Assert.Contains("Beta", result.FirstError);
        Assert.Single(_definitionUploads);
        Assert.Contains("Published 2 of 3 catalog(s). Failed: Beta", vm.UploadStatusMessage);
        Assert.True(vm.HasDefinitionChanges);
        Assert.True(vm.CatalogStatuses.Single(s => s.Catalog.Name == "Beta").NeedsPublish);
        Assert.False(vm.IsUploading);
        _mockNotificationService.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.Is<string>(m => m.Contains("Published 2 of 3 catalog(s)")), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// A cascade where every catalog fails must show an error, skip the definition upload,
    /// and return a failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadProviderDefinitionAsync_AllCatalogsFail_SkipsDefinitionUploadAsync()
    {
        var vm = CreateViewModel(failingCatalogFiles: ["catalog-a.json", "catalog-b.json", "catalog-c.json"]);

        var result = await vm.UploadProviderDefinitionAsync();

        Assert.False(result.Success);
        Assert.Empty(_definitionUploads);
        Assert.False(vm.IsUploading);
        _mockNotificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// When the definition cannot be regenerated after the cascade, the previous JSON must not
    /// be uploaded and the definition must stay stale.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadProviderDefinitionAsync_DefinitionGenerationFails_DoesNotUploadStaleJsonAsync()
    {
        var vm = CreateViewModel(failingCatalogFiles: [], definitionGenerates: false);
        vm.ProviderDefinitionJson = StaleDefinitionJson;
        vm.HasDefinitionChanges = false;

        var result = await vm.UploadProviderDefinitionAsync();

        Assert.False(result.Success);
        Assert.Empty(_definitionUploads);
        Assert.True(vm.HasDefinitionChanges);
        _mockNotificationService.Verify(
            n => n.ShowSuccess("Definition Published", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Publish All must not upload the previous definition JSON when regeneration fails,
    /// and must report the definition problem.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishAllCatalogsCommand_DefinitionGenerationFails_DoesNotUploadStaleJsonAsync()
    {
        var vm = CreateViewModel(failingCatalogFiles: [], definitionGenerates: false);
        vm.ProviderDefinitionJson = StaleDefinitionJson;
        vm.HasDefinitionChanges = false;

        await vm.PublishAllCatalogsCommand.ExecuteAsync(null);

        Assert.Empty(_definitionUploads);
        Assert.True(vm.HasDefinitionChanges);
        Assert.Contains("provider definition upload failed", vm.UploadStatusMessage);
        Assert.Contains("Failed to generate definition", vm.UploadStatusMessage);
    }

    /// <summary>
    /// A partial Publish All with a definition generation failure must name both problems.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishAllCatalogsCommand_PartialFailureAndDefinitionGenerationFails_ReportsBothAsync()
    {
        var vm = CreateViewModel(failingCatalogFiles: ["catalog-b.json"], definitionGenerates: false);
        vm.ProviderDefinitionJson = StaleDefinitionJson;

        await vm.PublishAllCatalogsCommand.ExecuteAsync(null);

        Assert.Empty(_definitionUploads);
        Assert.Contains("Published 2 of 3 catalog(s). Failed: Beta", vm.UploadStatusMessage);
        Assert.Contains("Provider definition failed", vm.UploadStatusMessage);
        Assert.True(vm.PublishCompleted);
        Assert.False(vm.IsUploading);
    }

    /// <summary>
    /// A cascade where every catalog succeeds keeps the existing success notifications.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadProviderDefinitionAsync_AllCatalogsSucceed_ReportsSuccessAsync()
    {
        var vm = CreateViewModel(failingCatalogFiles: []);
        vm.HasDefinitionChanges = true;

        var result = await vm.UploadProviderDefinitionAsync();

        Assert.True(result.Success);
        Assert.Single(_definitionUploads);
        Assert.False(vm.HasDefinitionChanges);
        Assert.False(vm.IsUploading);
        _mockNotificationService.Verify(
            n => n.ShowSuccess("Catalogs Published", "Successfully published 3 catalog(s).", It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _mockNotificationService.Verify(
            n => n.ShowSuccess("Definition Published", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _mockNotificationService.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    private static NamedCatalog CreateCatalog(string id, string name) => new()
    {
        Id = id,
        Name = name,
        FileName = $"catalog-{id}.json",
        Catalog = new PublisherCatalog(),
    };

    private PublishShareViewModel CreateViewModel(string[] failingCatalogFiles, bool definitionGenerates = true)
    {
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            Catalogs = [CreateCatalog("a", "Alpha"), CreateCatalog("b", "Beta"), CreateCatalog("c", "Gamma")],
        };

        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns(HostingConstants.Dropbox);
        mockProvider.Setup(p => p.DisplayName).Returns("Dropbox");
        mockProvider.Setup(p => p.RequiresAuthentication).Returns(false);
        mockProvider.Setup(p => p.IsAuthenticated).Returns(true);
        mockProvider.Setup(p => p.SupportsArtifactHosting).Returns(true);
        mockProvider.Setup(p => p.SupportsCatalogHosting).Returns(true);
        mockProvider.Setup(p => p.SupportsUpdate).Returns(false);
        mockProvider.Setup(p => p.UploadCatalogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string json, string publisherId, string? fileName, IProgress<int>? progress, CancellationToken ct) =>
                Array.IndexOf(failingCatalogFiles, fileName) >= 0
                    ? OperationResult<HostingUploadResult>.CreateFailure("Quota exceeded")
                    : OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                    {
                        FileId = $"id-{fileName}",
                        PublicUrl = $"https://dl.dropboxusercontent.com/s/x/{fileName}",
                        DirectDownloadUrl = $"https://dl.dropboxusercontent.com/s/x/{fileName}",
                        FileSize = 10,
                    }));
        mockProvider.Setup(p => p.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream s, string name, string? folder, IProgress<int>? progress, CancellationToken ct) =>
            {
                using var reader = new StreamReader(s);
                _definitionUploads.Add(reader.ReadToEnd());
                return OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                {
                    FileId = $"id-{name}",
                    PublicUrl = $"https://dl.dropboxusercontent.com/s/x/{name}",
                    DirectDownloadUrl = $"https://dl.dropboxusercontent.com/s/x/{name}",
                    FileSize = 8,
                });
            });

        _mockStudioService.Setup(m => m.ValidateCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _mockStudioService.Setup(m => m.ExportCatalogAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<NamedCatalog?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("{\"catalog\":true}"));
        _mockStudioService.Setup(m => m.ExportProviderDefinitionAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(definitionGenerates
                ? OperationResult<string>.CreateSuccess("{\"definition\":true}")
                : OperationResult<string>.CreateFailure("Export failed"));
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
        return vm;
    }
}
