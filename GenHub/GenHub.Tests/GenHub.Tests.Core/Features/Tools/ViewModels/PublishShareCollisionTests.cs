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

    /// <summary>
    /// Hosting state must record the resolved upload filename so later lookups
    /// and collision checks see the actual remote name.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_CollidingFileName_PersistsResolvedFileNameAsync()
    {
        var catalogA = CreateCatalog("a", "Alpha", "catalog.json");
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            Catalogs = [catalogA, CreateCatalog("b", "Beta", "catalog.json")],
        };
        var (vm, _) = CreateViewModel(project);

        PublisherHostingStates? savedStates = null;
        _mockHostingStateManager
            .Setup(m => m.SaveStatesAsync(It.IsAny<string>(), It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()))
            .Callback<string, PublisherHostingStates, CancellationToken>((_, states, _) => savedStates = states)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        await vm.InitializeAsync();
        await vm.PublishCatalogCommand.ExecuteAsync(catalogA);

        Assert.NotNull(savedStates);
        var entry = Assert.Single(savedStates.States[HostingConstants.Dropbox].Catalogs, c => c.CatalogId == "a");
        Assert.Equal("catalog-a.json", entry.FileName);
    }

    /// <summary>
    /// Two catalogs sharing one project filename must each display their own
    /// published URL instead of the first matching entry.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_SameFileName_EachCatalogShowsOwnUrlAsync()
    {
        var catalogA = CreateCatalog("a", "Alpha", "catalog.json");
        var catalogB = CreateCatalog("b", "Beta", "catalog.json");
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            Catalogs = [catalogA, catalogB],
        };
        var (vm, _) = CreateViewModel(project);

        await vm.InitializeAsync();
        await vm.PublishCatalogCommand.ExecuteAsync(catalogA);
        await vm.PublishCatalogCommand.ExecuteAsync(catalogB);

        var assetA = Assert.Single(vm.HostedAssets, a => a.AssetKind == HostedAssetKind.Catalog && a.CatalogId == "a");
        var assetB = Assert.Single(vm.HostedAssets, a => a.AssetKind == HostedAssetKind.Catalog && a.CatalogId == "b");
        Assert.Contains("catalog-a.json", assetA.Url);
        Assert.Contains("catalog-b.json", assetB.Url);
    }

    /// <summary>
    /// A catalog whose project filename equals another catalog's resolved remote
    /// name must upload under a different name instead of colliding remotely.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_FileNameMatchesPersistedRemoteName_UploadsUnderFallbackAsync()
    {
        var catalogAlpha = CreateCatalog("alpha", "Alpha", "foo.json");
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            Catalogs = [catalogAlpha, CreateCatalog("beta", "Beta", "foo.json"), CreateCatalog("gamma", "Gamma", "catalog-alpha.json")],
        };
        var hostingState = new HostingState
        {
            ProviderId = HostingConstants.Dropbox,
            Catalogs =
            [
                new() { CatalogId = "alpha", CatalogName = "Alpha", FileName = "catalog-alpha.json", FileId = "id-alpha", Url = "https://dl.dropboxusercontent.com/s/x/catalog-alpha.json" },
            ],
        };
        var container = new PublisherHostingStates
        {
            States = { [HostingConstants.Dropbox] = hostingState },
        };
        var (vm, _) = CreateViewModel(project, container);

        await vm.InitializeAsync();
        await vm.PublishCatalogCommand.ExecuteAsync(project.Catalogs[2]);

        Assert.Equal(["catalog-gamma.json"], _uploadedCatalogFiles);
    }

    /// <summary>
    /// A project-relative Assets file referenced by content metadata must be
    /// uploaded and rewritten to a hosted URL instead of skipped as built-in.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_ProjectRelativeAssetsFile_UploadsArtworkAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_artwork_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, "Assets"));
        await File.WriteAllTextAsync(Path.Combine(tempDir, "Assets", "custom-icon.png"), "fake-png-bytes");

        try
        {
            var content = new CatalogContentItem
            {
                Id = "icon-item",
                Name = "Icon Item",
                ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
                Metadata = new ContentRichMetadata { IconUrl = "Assets/custom-icon.png" },
            };
            var catalog = CreateCatalog("a", "Alpha", "catalog-a.json");
            catalog.Catalog.Content = [content];
            var project = new PublisherStudioProject
            {
                ProjectPath = Path.Combine(tempDir, "project.json"),
                Catalogs = [catalog],
            };
            var (vm, mockProvider) = CreateViewModel(project);

            await vm.InitializeAsync();
            await vm.PublishCatalogCommand.ExecuteAsync(catalog);

            mockProvider.Verify(
                p => p.UploadFileAsync(
                    It.IsAny<Stream>(),
                    It.Is<string>(name => name.EndsWith("custom-icon.png", StringComparison.Ordinal)),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.StartsWith("https://", content.Metadata!.IconUrl);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// An Assets reference without a matching local file must still be treated
    /// as a built-in resource and skipped during artwork upload.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_MissingAssetsFile_SkipsArtworkUploadAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_artwork_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var content = new CatalogContentItem
            {
                Id = "icon-item",
                Name = "Icon Item",
                ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
                Metadata = new ContentRichMetadata { IconUrl = "Assets/missing-icon.png" },
            };
            var catalog = CreateCatalog("a", "Alpha", "catalog-a.json");
            catalog.Catalog.Content = [content];
            var project = new PublisherStudioProject
            {
                ProjectPath = Path.Combine(tempDir, "project.json"),
                Catalogs = [catalog],
            };
            var (vm, mockProvider) = CreateViewModel(project);

            await vm.InitializeAsync();
            await vm.PublishCatalogCommand.ExecuteAsync(catalog);

            mockProvider.Verify(
                p => p.UploadFileAsync(
                    It.IsAny<Stream>(),
                    It.Is<string>(name => name.Contains("missing-icon", StringComparison.Ordinal)),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.Equal("Assets/missing-icon.png", content.Metadata!.IconUrl);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// A relative artwork path escaping the project directory must never be
    /// uploaded, even when the escaped file exists on the publisher machine.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_TraversalArtworkPath_SkipsUploadAsync()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"genhub_traversal_{Guid.NewGuid():N}");
        var projectDir = Path.Combine(rootDir, "proj");
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets"));
        await File.WriteAllTextAsync(Path.Combine(rootDir, "secret.txt"), "publisher-secret");
        await File.WriteAllTextAsync(Path.Combine(projectDir, "Assets", "ok-icon.png"), "fake-png-bytes");

        try
        {
            var evil = new CatalogContentItem
            {
                Id = "evil-item",
                Name = "Evil Item",
                ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
                Metadata = new ContentRichMetadata { IconUrl = "Assets/../../secret.txt" },
            };
            var good = new CatalogContentItem
            {
                Id = "good-item",
                Name = "Good Item",
                ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
                Metadata = new ContentRichMetadata { IconUrl = "Assets/ok-icon.png" },
            };
            var catalog = CreateCatalog("a", "Alpha", "catalog-a.json");
            catalog.Catalog.Content = [evil, good];
            var project = new PublisherStudioProject
            {
                ProjectPath = Path.Combine(projectDir, "project.json"),
                Catalogs = [catalog],
            };
            var (vm, mockProvider) = CreateViewModel(project);

            await vm.InitializeAsync();
            await vm.PublishCatalogCommand.ExecuteAsync(catalog);

            mockProvider.Verify(
                p => p.UploadFileAsync(
                    It.IsAny<Stream>(),
                    It.Is<string>(name => name.Contains("secret.txt", StringComparison.Ordinal)),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.Equal("Assets/../../secret.txt", evil.Metadata!.IconUrl);
            Assert.StartsWith("https://", good.Metadata!.IconUrl);
        }
        finally
        {
            Directory.Delete(rootDir, true);
        }
    }

    /// <summary>
    /// An artwork path that cannot be parsed as a path must be skipped without
    /// failing the publish, even when canonicalization would throw.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PublishCatalogCommand_UnparseableArtworkPath_SkipsUploadAsync()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"genhub_unparseable_{Guid.NewGuid():N}");
        var projectDir = Path.Combine(rootDir, "proj");
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets"));
        await File.WriteAllTextAsync(Path.Combine(projectDir, "Assets", "ok-icon.png"), "fake-png-bytes");

        try
        {
            const string badIconUrl = "Assets/\0evil.txt";
            var bad = new CatalogContentItem
            {
                Id = "bad-item",
                Name = "Bad Item",
                ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
                Metadata = new ContentRichMetadata { IconUrl = badIconUrl },
            };
            var good = new CatalogContentItem
            {
                Id = "good-item",
                Name = "Good Item",
                ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
                Metadata = new ContentRichMetadata { IconUrl = "Assets/ok-icon.png" },
            };
            var catalog = CreateCatalog("a", "Alpha", "catalog-a.json");
            catalog.Catalog.Content = [bad, good];
            var project = new PublisherStudioProject
            {
                ProjectPath = Path.Combine(projectDir, "project.json"),
                Catalogs = [catalog],
            };
            var (vm, mockProvider) = CreateViewModel(project);

            await vm.InitializeAsync();
            await vm.PublishCatalogCommand.ExecuteAsync(catalog);

            mockProvider.Verify(
                p => p.UploadFileAsync(
                    It.IsAny<Stream>(),
                    It.Is<string>(name => name.Contains("evil", StringComparison.Ordinal)),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.Equal(badIconUrl, bad.Metadata!.IconUrl);
            Assert.StartsWith("https://", good.Metadata!.IconUrl);
        }
        finally
        {
            Directory.Delete(rootDir, true);
        }
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
