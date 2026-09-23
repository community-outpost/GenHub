using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for cloud hosting asset discovery, categorization, filtering, and project loading.
/// </summary>
public sealed class PublisherStudioHostingDiscoveryAndLoadTests : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherStudioHostingDiscoveryAndLoadTests"/> class.
    /// </summary>
    public PublisherStudioHostingDiscoveryAndLoadTests()
    {
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
    }

    /// <summary>
    /// Tests that <see cref="HostingConstants.IsPublisherDefinitionFileName"/> properly identifies publisher definition files.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <param name="expected">The expected classification result.</param>
    [Theory]
    [InlineData("publisher.json", true)]
    [InlineData("publisher-prod.json", true)]
    [InlineData("my-publisher.json", true)]
    [InlineData("definition.json", true)]
    [InlineData("publisher_definitions.json", true)]
    [InlineData("catalog.json", false)]
    [InlineData("catalog-main.json", false)]
    [InlineData("game-patch.zip", false)]
    [InlineData(null, false)]
    public void IsPublisherDefinitionFileName_ClassifiesCorrectly(string? fileName, bool expected)
    {
        var result = HostingConstants.IsPublisherDefinitionFileName(fileName);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that <see cref="HostingConstants.IsCatalogFileName"/> properly identifies catalog manifest files.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <param name="expected">The expected classification result.</param>
    [Theory]
    [InlineData("catalog.json", true)]
    [InlineData("catalog-main.json", true)]
    [InlineData("catalog-1.json", true)]
    [InlineData("publisher.json", false)]
    [InlineData("definition.json", false)]
    [InlineData("file.zip", false)]
    [InlineData(null, false)]
    public void IsCatalogFileName_ClassifiesCorrectly(string? fileName, bool expected)
    {
        var result = HostingConstants.IsCatalogFileName(fileName);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that <see cref="HostedAssetItemViewModel"/> configured as a Definition sets expected UI flags.
    /// </summary>
    [Fact]
    public void HostedAssetItemViewModel_DefinitionKind_SetsCorrectProperties()
    {
        var vm = new HostedAssetItemViewModel
        {
            Name = "publisher.json",
            AssetKind = HostedAssetKind.Definition,
            Url = "https://example.com/publisher.json",
            CanLoadToProject = true,
        };

        Assert.True(vm.IsDefinition);
        Assert.False(vm.IsCatalog);
        Assert.False(vm.IsArtifact);
        Assert.True(vm.CanLoadToProject);
        Assert.False(vm.CanAddToCatalog);
    }

    /// <summary>
    /// Tests that <see cref="HostedAssetItemViewModel"/> configured as a Catalog sets expected UI flags.
    /// </summary>
    [Fact]
    public void HostedAssetItemViewModel_CatalogKind_SetsCorrectProperties()
    {
        var vm = new HostedAssetItemViewModel
        {
            Name = "catalog-5.json",
            AssetKind = HostedAssetKind.Catalog,
            Url = "https://example.com/catalog-5.json",
            CanLoadToProject = true,
        };

        Assert.False(vm.IsDefinition);
        Assert.True(vm.IsCatalog);
        Assert.False(vm.IsArtifact);
        Assert.True(vm.CanLoadToProject);
        Assert.False(vm.CanAddToCatalog);
    }

    /// <summary>
    /// Tests that <see cref="HostedAssetItemViewModel"/> configured as an Artifact sets expected UI flags.
    /// </summary>
    [Fact]
    public void HostedAssetItemViewModel_ArtifactKind_SetsCorrectProperties()
    {
        var vm = new HostedAssetItemViewModel
        {
            Name = "mod-v1.zip",
            AssetKind = HostedAssetKind.Artifact,
            Url = "https://example.com/mod-v1.zip",
            CanAddToCatalog = true,
        };

        Assert.False(vm.IsDefinition);
        Assert.False(vm.IsCatalog);
        Assert.True(vm.IsArtifact);
        Assert.False(vm.CanLoadToProject);
        Assert.True(vm.CanAddToCatalog);
    }

    /// <summary>
    /// Tests that <see cref="HostingState"/> preserves and exposes multiple discovered definition files.
    /// </summary>
    [Fact]
    public void HostingState_DefinitionsList_TracksMultipleDefinitions()
    {
        var state = new HostingState
        {
            ProviderId = "dropbox",
            Definition = new HostedFileInfo { FileName = "publisher.json", Url = "https://dropbox.com/p1" },
            Definitions =
            [
                new HostedFileInfo { FileName = "publisher.json", Url = "https://dropbox.com/p1" },
                new HostedFileInfo { FileName = "publisher-beta.json", Url = "https://dropbox.com/p2" },
                new HostedFileInfo { FileName = "publisher-legacy.json", Url = "https://dropbox.com/p3" },
            ],
        };

        Assert.Equal(3, state.Definitions.Count);
        Assert.Equal("publisher.json", state.Definition.FileName);
        Assert.Contains(state.Definitions, d => d.FileName == "publisher-beta.json");
    }

    /// <summary>
    /// Tests that attempting to load a cloud definition from an unsafe or loopback URL is rejected by SSRF protection.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadAssetToProject_UnsafeUrl_IsRejectedBySsrfProtection()
    {
        var mockNotificationService = new Mock<INotificationService>();
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        using var vm = new PublishShareViewModel(
            project,
            Mock.Of<IPublisherStudioService>(),
            NullLogger.Instance,
            notificationService: mockNotificationService.Object);

        var unsafeAsset = new HostedAssetItemViewModel
        {
            Name = "publisher.json",
            AssetKind = HostedAssetKind.Definition,
            Url = "http://127.0.0.1:8080/secret/publisher.json",
            CanLoadToProject = true,
        };

        await vm.LoadAssetToProjectCommand.ExecuteAsync(unsafeAsset);

        mockNotificationService.Verify(
            n => n.ShowError(
                It.IsAny<string>(),
                It.Is<string>(msg => msg.Contains("empty", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that loading a definition follows HTTP 303 SeeOther redirects (such as Google Drive download URLs).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadAssetToProject_FollowsHttpSeeOtherRedirect_SuccessfullyLoadsDefinitionAsync()
    {
        var handler = new RedirectingHttpMessageHandler();
        var client = new HttpClient(handler);
        PublishShareViewModel.HttpClientOverrideForTesting = client;

        try
        {
            var mockNotificationService = new Mock<INotificationService>();
            var project = new PublisherStudioProject
            {
                ProjectPath = "/test/path/project.json",
                Catalog = new PublisherCatalog(),
            };

            using var vm = new PublishShareViewModel(
                project,
                Mock.Of<IPublisherStudioService>(),
                NullLogger.Instance,
                notificationService: mockNotificationService.Object);

            var asset = new HostedAssetItemViewModel
            {
                Name = "publisher.json",
                AssetKind = HostedAssetKind.Definition,
                Url = "https://drive.google.com/uc?export=download&id=redirect_test_id",
                CanLoadToProject = true,
            };

            await vm.LoadAssetToProjectCommand.ExecuteAsync(asset);

            Assert.Equal("Redirected Publisher", project.Catalog?.Publisher?.Name);
            Assert.Equal("redirected_pub", project.Catalog?.Publisher?.Id);
            mockNotificationService.Verify(
                n => n.ShowSuccess(
                    It.IsAny<string>(),
                    It.Is<string>(msg => msg.Contains("Successfully loaded", StringComparison.OrdinalIgnoreCase)),
                    It.IsAny<int?>(),
                    It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            PublishShareViewModel.HttpClientOverrideForTesting = null;
            client.Dispose();
            handler.Dispose();
        }
    }

    private sealed class RedirectingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.ToString() == "https://drive.google.com/uc?export=download&id=redirect_test_id")
            {
                var redirectResponse = new HttpResponseMessage(System.Net.HttpStatusCode.SeeOther);
                redirectResponse.Headers.Location = new Uri("https://drive.google.com/download/redirected_publisher.json");
                return Task.FromResult(redirectResponse);
            }

            var definitionJson = """
                {
                    "publisher": {
                        "id": "redirected_pub",
                        "name": "Redirected Publisher"
                    }
                }
                """;

            var successResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(definitionJson, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(successResponse);
        }
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void HostedAssetItemViewModel_IsPending_ReflectsState(bool isOnline, bool isExternalCdn, bool expectedIsPending)
    {
        var vm = new HostedAssetItemViewModel
        {
            IsOnline = isOnline,
            IsExternalCdn = isExternalCdn,
        };

        Assert.Equal(expectedIsPending, vm.IsPending);
    }

    [Fact]
    public void HostedAssetItemViewModel_FileSize_UpdatesFormattedString()
    {
        var vm = new HostedAssetItemViewModel
        {
            FileSize = 1048576,
        };

        Assert.Equal("1 MB", vm.FileSizeFormatted);
    }

    [Fact]
    public void PublisherProfileViewModel_ApplyToProject_SynchronizesPublisherData()
    {
        var project = new PublisherStudioProject
        {
            Catalog = new PublisherCatalog(),
        };

        var vm = new PublisherProfileViewModel(
            project,
            parentViewModel: null!,
            logger: NullLogger.Instance)
        {
            PublisherId = "my-test-id",
            PublisherName = "My Test Publisher",
            AvatarUrl = "https://example.com/avatar.png",
            WebsiteUrl = "https://example.com",
            TagsString = "maps, mods",
        };

        vm.ApplyToProject();

        Assert.NotNull(project.Catalog.Publisher);
        Assert.Equal("my-test-id", project.Catalog.Publisher.Id);
        Assert.Equal("My Test Publisher", project.Catalog.Publisher.Name);
        Assert.Equal("https://example.com/avatar.png", project.Catalog.Publisher.AvatarUrl);
        Assert.Equal("https://example.com", project.Catalog.Publisher.WebsiteUrl);
        Assert.Contains("maps", project.Tags);
        Assert.Contains("mods", project.Tags);
    }
}
