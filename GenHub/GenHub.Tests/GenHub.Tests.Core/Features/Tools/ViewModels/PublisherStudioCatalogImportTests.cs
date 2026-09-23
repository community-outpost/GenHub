using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Regression tests for importing third-party catalog files into Publisher Studio.
/// </summary>
public sealed class PublisherStudioCatalogImportTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IPublisherStudioService> _studioServiceMock = new();
    private readonly Mock<IPublisherStudioDialogService> _dialogServiceMock = new();
    private readonly PublisherStudioViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherStudioCatalogImportTests"/> class.
    /// </summary>
    public PublisherStudioCatalogImportTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "GenHubPublisherImportTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        var configProviderMock = new Mock<IConfigurationProviderService>();
        configProviderMock.Setup(x => x.GetApplicationDataPath()).Returns(_testDirectory);

        _studioServiceMock
            .Setup(x => x.CreateProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => OperationResult<PublisherStudioProject>.CreateSuccess(new PublisherStudioProject
            {
                ProjectName = name,
                ProjectPath = string.Empty,
                Catalog = new PublisherCatalog
                {
                    Publisher = new PublisherProfile { Id = string.Empty, Name = string.Empty },
                },
            }));
        _studioServiceMock
            .Setup(x => x.SaveProjectAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _dialogServiceMock
            .Setup(x => x.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        _viewModel = new PublisherStudioViewModel(
            new Mock<ILogger<PublisherStudioViewModel>>().Object,
            _studioServiceMock.Object,
            _dialogServiceMock.Object,
            notificationService: new Mock<INotificationService>().Object,
            configurationProvider: configProviderMock.Object,
            catalogParser: new JsonPublisherCatalogParser(NullLogger<JsonPublisherCatalogParser>.Instance));
    }

    /// <summary>
    /// Disposes of the test resources.
    /// </summary>
    public void Dispose()
    {
        _viewModel.Dispose();
        try
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Verifies that a community catalog using string enums and a custom publisher type
    /// imports successfully with the publisher type normalized to generic.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ImportCatalogFromFileAsync_ThirdPartyCatalog_NormalizesPublisherTypeAsync()
    {
        await _viewModel.CreateNewProjectCommand.ExecuteAsync(null);
        Assert.NotNull(_viewModel.CurrentProject);

        const string json = """
            {
              "$schema": "https://genhub.net/schemas/publisher-catalog.json",
              "formatVersion": "1.0.0",
              "publisher": { "id": "dominator", "name": "Dominator Mappacks" },
              "content": [
                {
                  "id": "gla-campaign-by-tklyo",
                  "name": "GLA Campaign by TKlyo",
                  "description": "Custom singleplayer GLA campaign missions.",
                  "contentType": "MapPack",
                  "publisherType": "dominator",
                  "targetGame": "ZeroHour",
                  "releases": [
                    {
                      "version": "1.0.0",
                      "isLatest": true,
                      "artifacts": [
                        { "filename": "gla-campaign.rar", "downloadUrl": "https://example.com/gla-campaign.rar", "isPrimary": true }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
        var filePath = Path.Combine(_testDirectory, "CATALOG.json");
        await File.WriteAllTextAsync(filePath, json);

        var imported = await _viewModel.ImportCatalogFromFileAsync(filePath, announceFailures: true);

        Assert.True(imported);
        Assert.NotNull(_viewModel.CurrentProject.Catalogs);
        var named = Assert.Single(_viewModel.CurrentProject.Catalogs, c => c.FileName == "CATALOG.json");
        var item = Assert.Single(named.Catalog.Content);
        Assert.Equal("gla-campaign-by-tklyo", item.Id);
        Assert.True(string.IsNullOrEmpty(item.PublisherType));
    }

    /// <summary>
    /// Verifies that importing a file that is not a catalog fails gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ImportCatalogFromFileAsync_NonCatalogJson_ReturnsFalseAsync()
    {
        await _viewModel.CreateNewProjectCommand.ExecuteAsync(null);
        Assert.NotNull(_viewModel.CurrentProject);

        var filePath = Path.Combine(_testDirectory, "notes.json");
        await File.WriteAllTextAsync(filePath, """{ "hello": "world" }""");

        var imported = await _viewModel.ImportCatalogFromFileAsync(filePath, announceFailures: true);

        Assert.False(imported);
    }

    /// <summary>
    /// Verifies that importing a catalog into an existing project with established catalogs
    /// does not overwrite the project's existing publisher identity or project name.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ImportCatalogFromFileAsync_ExistingProjectWithCatalogs_DoesNotOverwriteProjectPublisher()
    {
        await _viewModel.CreateNewProjectCommand.ExecuteAsync(null);
        Assert.NotNull(_viewModel.CurrentProject);
        _viewModel.CurrentProject.ProjectName = "Original Project";
        _viewModel.CurrentProject.Catalog.Publisher = new PublisherProfile { Id = "my-publisher", Name = "My Custom Publisher" };
        _viewModel.CurrentProject.Catalogs =
        [
            new NamedCatalog
            {
                Id = "existing-catalog",
                Name = "Existing Catalog",
                Catalog = new PublisherCatalog { Content = [new CatalogContentItem { Id = "existing-item" }] },
            }
        ];

        const string json = """
            {
              "": "https://genhub.net/schemas/publisher-catalog.json",
              "formatVersion": "1.0.0",
              "publisher": { "id": "dominator", "name": "Dominator Mappacks" },
              "content": [
                {
                  "id": "new-item",
                  "name": "New Item",
                  "contentType": "MapPack",
                  "releases": []
                }
              ]
            }
            """;
        var filePath = Path.Combine(_testDirectory, "imported.json");
        await File.WriteAllTextAsync(filePath, json);

        var imported = await _viewModel.ImportCatalogFromFileAsync(filePath, announceFailures: true);

        Assert.True(imported);
        Assert.Equal("Original Project", _viewModel.CurrentProject.ProjectName);
        Assert.Equal("my-publisher", _viewModel.CurrentProject.Catalog.Publisher.Id);
        Assert.Equal("My Custom Publisher", _viewModel.CurrentProject.Catalog.Publisher.Name);
    }
}
