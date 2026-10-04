using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services.Hosting;
using GenHub.Features.Tools.ViewModels;
using GenHub.Tests.Core.Collections;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for the hosted asset inventory: media classification, catalog linkage,
/// sorting, expandable children, and delete-from-drive flows in <see cref="PublishShareViewModel"/>.
/// </summary>
[Collection(PublishShareStaticStateCollection.Name)]
public class PublisherStudioInventoryManagementTests
{
    private const string CloudArtifactUrl = "https://drive.google.com/uc?export=download&id=modfile";
    private const string CloudScreenshotUrl = "https://drive.google.com/uc?export=download&id=shot1";
    private const string CloudVideoUrl = "https://drive.google.com/uc?export=download&id=vid1";
    private const string ExternalScreenshotUrl = "https://cdn.example.com/shot2.png";
    private const string ExternalTrailerUrl = "https://cdn.example.com/trailer.mp4";

    /// <summary>
    /// Tests that <see cref="HostingConstants.IsScreenshotFileName"/> classifies image extensions.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <param name="expected">The expected classification result.</param>
    [Theory]
    [InlineData("shot.png", true)]
    [InlineData("cover.JPG", true)]
    [InlineData("banner.webp", true)]
    [InlineData("trailer.mp4", false)]
    [InlineData("mod.zip", false)]
    [InlineData("catalog-main.json", false)]
    [InlineData(null, false)]
    public void IsScreenshotFileName_ClassifiesCorrectly(string? fileName, bool expected)
    {
        Assert.Equal(expected, HostingConstants.IsScreenshotFileName(fileName));
    }

    /// <summary>
    /// Tests that <see cref="HostingConstants.IsVideoFileName"/> classifies video extensions.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <param name="expected">The expected classification result.</param>
    [Theory]
    [InlineData("trailer.mp4", true)]
    [InlineData("preview.WEBM", true)]
    [InlineData("shot.png", false)]
    [InlineData("mod.zip", false)]
    [InlineData(null, false)]
    public void IsVideoFileName_ClassifiesCorrectly(string? fileName, bool expected)
    {
        Assert.Equal(expected, HostingConstants.IsVideoFileName(fileName));
    }

    /// <summary>
    /// Tests that media asset kinds expose the expected UI flags and are distinct from artifacts.
    /// </summary>
    [Fact]
    public void HostedAssetItemViewModel_MediaKinds_SetCorrectFlags()
    {
        var screenshot = new HostedAssetItemViewModel { Name = "shot.png", AssetKind = HostedAssetKind.Screenshot };
        var video = new HostedAssetItemViewModel { Name = "trailer.mp4", AssetKind = HostedAssetKind.Video };

        Assert.True(screenshot.IsScreenshot);
        Assert.True(screenshot.IsMedia);
        Assert.False(screenshot.IsArtifact);
        Assert.False(screenshot.IsCatalog);
        Assert.False(screenshot.CanAddToCatalog);

        Assert.True(video.IsVideo);
        Assert.True(video.IsMedia);
        Assert.False(video.IsArtifact);
        Assert.False(video.IsCatalog);
    }

    /// <summary>
    /// Tests that catalog media URLs populate screenshot and video inventory rows with linkage.
    /// </summary>
    [Fact]
    public void RefreshHostedAssets_MediaUrls_PopulateScreenshotAndVideoRows()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project);

        vm.RefreshHostedAssets();

        Assert.Equal(2, vm.HostedScreenshotsCount);
        Assert.Equal(1, vm.HostedVideosCount);

        var screenshots = vm.HostedAssets.Where(a => a.IsScreenshot && !a.IsExternalCdn).ToList();
        Assert.Equal(2, screenshots.Count);
        Assert.All(screenshots, s =>
        {
            Assert.True(s.CanDelete);
            Assert.Contains("Main Catalog", s.LinkedToText, StringComparison.Ordinal);
            Assert.Contains("Cool Mod", s.LinkedToText, StringComparison.Ordinal);
        });

        var video = vm.HostedAssets.Single(a => a.IsVideo && !a.IsExternalCdn);
        Assert.Equal(CloudVideoUrl, video.Url);
        Assert.True(video.HasChildren);
    }

    /// <summary>
    /// Tests that artifact rows carry their catalog linkage for the Linked To column.
    /// </summary>
    [Fact]
    public void RefreshHostedAssets_Artifact_CarriesCatalogLinkage()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project);

        vm.RefreshHostedAssets();

        var artifact = vm.HostedAssets.Single(a => a.Name == "cool-mod.zip");
        Assert.Equal("main", artifact.CatalogId);
        Assert.Equal("Main Catalog", artifact.CatalogName);
        Assert.Equal("Test Publisher", artifact.DefinitionName);
        Assert.Contains("Main Catalog", artifact.LinkedToText, StringComparison.Ordinal);
        Assert.Contains("Cool Mod", artifact.LinkedToText, StringComparison.Ordinal);
        Assert.True(artifact.LinkCount >= 1);
        Assert.True(artifact.CanDelete);
        Assert.True(artifact.IsExpandable);
    }

    /// <summary>
    /// Tests that catalog rows expand to their content items, and empty catalogs report emptiness.
    /// </summary>
    [Fact]
    public void RefreshHostedAssets_CatalogRows_BuildContentChildren()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project);

        vm.RefreshHostedAssets();

        var main = vm.HostedAssets.Single(a => a.IsCatalog && a.CatalogId == "main");
        Assert.True(main.HasChildren);
        Assert.False(main.IsEmpty);
        Assert.True(main.IsExpandable);
        Assert.Single(main.Children);
        Assert.Equal("Cool Mod", main.Children[0].Name);
        Assert.Contains("Main Catalog", main.CatalogName, StringComparison.Ordinal);

        var maps = vm.HostedAssets.Single(a => a.IsCatalog && a.CatalogId == "maps");
        Assert.True(maps.IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(maps.EmptyStateText));
        Assert.True(maps.IsExpandable);
    }

    /// <summary>
    /// Tests that the definition row expands to the project's catalogs.
    /// </summary>
    [Fact]
    public void RefreshHostedAssets_DefinitionRow_BuildsCatalogChildren()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project);

        vm.RefreshHostedAssets();

        var definition = vm.HostedAssets.Single(a => a.IsDefinition);
        Assert.Equal(2, definition.Children.Count);
        Assert.True(definition.HasChildren);
        Assert.Contains("2 catalog", definition.LinkedToText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that discovered cloud files are classified by extension and marked unlinked.
    /// </summary>
    [Fact]
    public void RefreshHostedAssets_DiscoveredFiles_ClassifiedByExtension()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "shot-id",
            FileName = "orphan-shot.png",
            Url = "https://drive.google.com/uc?export=download&id=orphan-shot",
            FileSize = 512,
        });
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "trailer-id",
            FileName = "orphan-trailer.mp4",
            Url = "https://drive.google.com/uc?export=download&id=orphan-trailer",
            FileSize = 1024,
        });

        vm.RefreshHostedAssets();

        var shot = vm.HostedAssets.Single(a => a.Name == "orphan-shot.png");
        Assert.True(shot.IsScreenshot);
        Assert.Equal(0, shot.LinkCount);
        Assert.Equal("Not linked", shot.LinkedToText);

        var trailer = vm.HostedAssets.Single(a => a.Name == "orphan-trailer.mp4");
        Assert.True(trailer.IsVideo);
        Assert.Equal(0, trailer.LinkCount);
    }

    /// <summary>
    /// Tests that the screenshot and video filters isolate media rows.
    /// </summary>
    /// <param name="filter">The filter to apply.</param>
    /// <param name="expectedCount">The expected row count.</param>
    [Theory]
    [InlineData("Screenshot", 2)]
    [InlineData("Video", 1)]
    public void ApplyHostedAssetFilter_MediaFilters_IsolateMediaRows(string filter, int expectedCount)
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project);
        vm.RefreshHostedAssets();

        vm.SetInventoryFilterCommand.Execute(filter);

        Assert.Equal(expectedCount, vm.FilteredHostedAssets.Count);
        Assert.All(vm.FilteredHostedAssets, a => Assert.False(a.IsExternalCdn));
    }

    /// <summary>
    /// Tests that inventory sorting orders rows by name and size, toggling direction on repeat.
    /// </summary>
    [Fact]
    public void SetInventorySortCommand_SortsByNameThenSize()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project);
        vm.RefreshHostedAssets();

        vm.SetInventorySortCommand.Execute("Name");
        var names = vm.FilteredHostedAssets.Select(a => a.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
        Assert.Equal("▲", vm.NameSortGlyph);

        vm.SetInventorySortCommand.Execute("Name");
        var reversed = vm.FilteredHostedAssets.Select(a => a.Name).ToList();
        Assert.Equal(names.OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase), reversed);
        Assert.Equal("▼", vm.NameSortGlyph);

        vm.SetInventorySortCommand.Execute("Size");
        var sizes = vm.FilteredHostedAssets.Select(a => a.FileSize).ToList();
        Assert.Equal(sizes.OrderBy(s => s), sizes);
        Assert.Equal("▲", vm.SizeSortGlyph);
        Assert.Equal(string.Empty, vm.NameSortGlyph);
    }

    /// <summary>
    /// Tests that deletes are refused when no confirmation mechanism is available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_WithoutConfirmation_RefusesDeleteAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        vm.RefreshHostedAssets();
        var artifact = vm.HostedAssets.Single(a => a.Name == "cool-mod.zip");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(artifact);

        provider.Verify(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts);
    }

    /// <summary>
    /// Tests that deleting a linked artifact removes the remote file and every catalog reference.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_Artifact_RemovesRemoteAndReferencesAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        var notifications = new Mock<INotificationService>();
        using var vm = CreateViewModel(project, provider, notifications);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "modfile-id",
            FileName = "cool-mod.zip",
            Url = CloudArtifactUrl,
            FileSize = 1024,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var artifact = vm.HostedAssets.Single(a => a.Name == "cool-mod.zip");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(artifact);

        provider.Verify(p => p.DeleteFileAsync("modfile-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts);
        Assert.DoesNotContain(state.Artifacts, a => a.Url == CloudArtifactUrl);
        notifications.Verify(n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Tests that a failed remote delete aborts the operation without touching local catalogs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_RemoteDeleteFails_AbortsWithoutLocalChangesAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        provider.Setup(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Access denied"));
        var notifications = new Mock<INotificationService>();
        using var vm = CreateViewModel(project, provider, notifications);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "modfile-id",
            FileName = "cool-mod.zip",
            Url = CloudArtifactUrl,
            FileSize = 1024,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var artifact = vm.HostedAssets.Single(a => a.Name == "cool-mod.zip");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(artifact);

        Assert.Single(project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts);
        notifications.Verify(n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Tests that deleting an external CDN artifact unlinks it without any remote delete call.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_ExternalArtifact_UnlinksWithoutRemoteDeleteAsync()
    {
        var project = CreateInventoryProject();
        const string externalUrl = "https://cdn.example.com/external-mod.zip";
        project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts.Add(new ReleaseArtifact
        {
            Filename = "external-mod.zip",
            DownloadUrl = externalUrl,
            Size = 2048,
        });
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var external = vm.HostedAssets.Single(a => a.Name == "external-mod.zip");
        Assert.True(external.IsExternalCdn);

        await vm.DeleteHostedAssetCommand.ExecuteAsync(external);

        provider.Verify(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.DoesNotContain(
            project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts,
            a => a.DownloadUrl == externalUrl);
    }

    /// <summary>
    /// Tests that deleting media removes its links from metadata and release media lists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_Media_RemovesLinksFromMetadataAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "shot1-id",
            FileName = "shot1",
            Url = CloudScreenshotUrl,
            FileSize = 256,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var media = vm.HostedAssets.Single(a => a.Url == CloudScreenshotUrl);

        await vm.DeleteHostedAssetCommand.ExecuteAsync(media);

        provider.Verify(p => p.DeleteFileAsync("shot1-id", It.IsAny<CancellationToken>()), Times.Once);
        var metadata = project.Catalogs[0].Catalog.Content[0].Metadata;
        Assert.NotNull(metadata);
        Assert.DoesNotContain(CloudScreenshotUrl, metadata.ScreenshotUrls);
    }

    /// <summary>
    /// Tests that deleting external media unlinks it without any remote delete call.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_ExternalMedia_UnlinksWithoutRemoteDeleteAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var media = vm.HostedAssets.Single(a => a.Url == ExternalScreenshotUrl);
        Assert.True(media.IsExternalCdn);

        await vm.DeleteHostedAssetCommand.ExecuteAsync(media);

        provider.Verify(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var metadata = project.Catalogs[0].Catalog.Content[0].Metadata;
        Assert.NotNull(metadata);
        Assert.DoesNotContain(ExternalScreenshotUrl, metadata.ScreenshotUrls);
    }

    /// <summary>
    /// Tests that deleting a project catalog removes it locally and prunes hosting state.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_Catalog_RemovesProjectCatalogAndStateAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "maps",
            CatalogName = "Maps",
            FileId = "maps-file-id",
            FileName = "catalog-maps.json",
            Url = "https://drive.google.com/uc?export=download&id=mapsfile",
            FileSize = 128,
        });
        vm.RefreshHostedAssets();
        var savedCount = 0;
        var reloadedCount = 0;
        vm.SaveProjectCallback = () =>
        {
            savedCount++;
            return Task.CompletedTask;
        };
        vm.ProjectReloadCallback = () =>
        {
            reloadedCount++;
            return Task.CompletedTask;
        };
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var catalogRow = vm.HostedAssets.Single(a => a.IsCatalog && a.CatalogId == "maps");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(catalogRow);

        provider.Verify(p => p.DeleteFileAsync("maps-file-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(project.Catalogs);
        Assert.DoesNotContain(state.Catalogs, c => c.CatalogId == "maps");
        Assert.Equal(1, savedCount);
        Assert.Equal(1, reloadedCount);
    }

    /// <summary>
    /// Tests that deleting the last project catalog is blocked with a warning.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_LastProjectCatalog_IsBlockedAsync()
    {
        var project = CreateInventoryProject();
        project.Catalogs.RemoveAt(1);
        var provider = CreateDriveProvider();
        var notifications = new Mock<INotificationService>();
        using var vm = CreateViewModel(project, provider, notifications);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "main",
            CatalogName = "Main Catalog",
            FileId = "main-file-id",
            FileName = "catalog-main.json",
            Url = "https://drive.google.com/uc?export=download&id=mainfile",
            FileSize = 128,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var catalogRow = vm.HostedAssets.Single(a => a.IsCatalog && a.CatalogId == "main");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(catalogRow);

        provider.Verify(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(project.Catalogs);
        notifications.Verify(n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Tests that the catalog delete confirmation names orphaned files but not shared ones.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_CatalogDelete_ConfirmationNamesOrphansAsync()
    {
        var project = CreateInventoryProject();
        const string sharedUrl = "https://drive.google.com/uc?export=download&id=shared";
        project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts.Add(new ReleaseArtifact
        {
            Filename = "shared.zip",
            DownloadUrl = sharedUrl,
            Size = 64,
        });
        project.Catalogs[1].Catalog.Content.Add(new CatalogContentItem
        {
            Id = "map-1",
            Name = "Shared Map",
            ContentType = GenHub.Core.Models.Enums.ContentType.Map,
            Releases =
            [
                new ContentRelease
                {
                    Version = "1.0.0",
                    Artifacts = [new ReleaseArtifact { Filename = "shared.zip", DownloadUrl = sharedUrl, Size = 64 }],
                },
            ],
        });
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "main",
            CatalogName = "Main Catalog",
            FileId = "main-file-id",
            FileName = "catalog-main.json",
            Url = "https://drive.google.com/uc?export=download&id=mainfile",
            FileSize = 128,
        });
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "modfile-id",
            FileName = "cool-mod.zip",
            Url = CloudArtifactUrl,
            FileSize = 1024,
        });
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "shared-id",
            FileName = "shared.zip",
            Url = sharedUrl,
            FileSize = 64,
        });
        vm.RefreshHostedAssets();
        string? capturedMessage = null;
        vm.ConfirmationCallback = (_, message) =>
        {
            capturedMessage = message;
            return Task.FromResult(true);
        };
        var catalogRow = vm.HostedAssets.Single(a => a.IsCatalog && a.CatalogId == "main");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(catalogRow);

        Assert.NotNull(capturedMessage);
        Assert.Contains("cool-mod.zip", capturedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("shared.zip", capturedMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that deleting an unlinked cloud file removes only the remote file and state entry.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_UnlinkedCloudFile_DeletesRemoteOnlyAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "orphan-id",
            FileName = "orphan.zip",
            Url = "https://drive.google.com/uc?export=download&id=orphan",
            FileSize = 4096,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var orphan = vm.HostedAssets.Single(a => a.Name == "orphan.zip");
        Assert.Equal(0, orphan.LinkCount);

        await vm.DeleteHostedAssetCommand.ExecuteAsync(orphan);

        provider.Verify(p => p.DeleteFileAsync("orphan-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(state.Artifacts, a => a.FileName == "orphan.zip");
        Assert.Single(project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts);
    }

    /// <summary>
    /// Tests that catalog remote cleanup keeps a remote file shared with another catalog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteCatalogRemotes_SharedFileId_KeepsRemoteFileAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "main",
            CatalogName = "Main Catalog",
            FileId = "shared-file-id",
            FileName = "catalog-main.json",
            Url = "https://drive.google.com/uc?export=download&id=sharedfile",
        });
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "maps",
            CatalogName = "Maps",
            FileId = "shared-file-id",
            FileName = "catalog-maps.json",
            Url = "https://drive.google.com/uc?export=download&id=sharedfile",
        });
        vm.RefreshHostedAssets();

        var cleaned = await vm.DeleteCatalogRemotesAsync("main");

        Assert.True(cleaned);
        provider.Verify(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.DoesNotContain(state.Catalogs, c => c.CatalogId == "main");
        Assert.Contains(state.Catalogs, c => c.CatalogId == "maps");
    }

    /// <summary>
    /// Tests that expanding a cloud-only catalog row previews its remote contents.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExpandCloudOnlyCatalog_PreviewsRemoteContentsAsync()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        const string remoteUrl = "https://example.com/catalog-remote.json";
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "remote",
            CatalogName = "Remote",
            FileId = "remote-id",
            FileName = "catalog-remote.json",
            Url = remoteUrl,
            FileSize = 128,
        });
        vm.RefreshHostedAssets();
        var row = vm.HostedAssets.Single(a => a.Name == "catalog-remote.json");
        Assert.True(row.NeedsRemotePreview);

        var catalog = new PublisherCatalog
        {
            Content =
            [
                new CatalogContentItem
                {
                    Id = "remote-mod",
                    Name = "Remote Mod",
                    ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
                },
            ],
        };
        var handler = new CountingJsonHandler(JsonSerializer.Serialize(catalog, PublisherJsonOptions.Definition));
        var client = new HttpClient(handler);
        PublishShareViewModel.HttpClientOverrideForTesting = client;
        PublishShareViewModel.AllowUnresolvableUrlsForTesting = true;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
        try
        {
            row.IsExpanded = true;
            await WaitForPreviewAsync(row);

            Assert.True(row.RemotePreviewLoaded);
            Assert.True(row.HasChildren);
            Assert.Single(row.Children);
            Assert.Equal("Remote Mod", row.Children[0].Name);
            Assert.Equal(remoteUrl, row.Children[0].CopyUrl);
        }
        finally
        {
            PublishShareViewModel.HttpClientOverrideForTesting = null;
            PublishShareViewModel.AllowUnresolvableUrlsForTesting = false;
            CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
            client.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Tests that expanding a cloud-only definition row previews its catalog entries.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExpandCloudOnlyDefinition_PreviewsCatalogEntriesAsync()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        const string remoteUrl = "https://example.com/publisher-remote.json";
        state.Definitions.Add(new HostedFileInfo
        {
            FileId = "remote-def-id",
            FileName = "publisher-remote.json",
            Url = remoteUrl,
            FileSize = 64,
        });
        vm.RefreshHostedAssets();
        var row = vm.HostedAssets.Single(a => a.Name == "publisher-remote.json");
        Assert.True(row.NeedsRemotePreview);

        var definition = new PublisherDefinition
        {
            Publisher = new PublisherProfile { Id = "remote-pub", Name = "Remote Pub" },
            Catalogs =
            [
                new CatalogEntry { Id = "c1", Name = "Remote Catalog", Url = "https://example.com/c1.json" },
            ],
        };
        var handler = new CountingJsonHandler(JsonSerializer.Serialize(definition, PublisherJsonOptions.Definition));
        var client = new HttpClient(handler);
        PublishShareViewModel.HttpClientOverrideForTesting = client;
        PublishShareViewModel.AllowUnresolvableUrlsForTesting = true;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
        try
        {
            row.IsExpanded = true;
            await WaitForPreviewAsync(row);

            Assert.True(row.RemotePreviewLoaded);
            Assert.Single(row.Children);
            Assert.Equal("Remote Catalog", row.Children[0].Name);
            Assert.Equal("https://example.com/c1.json", row.Children[0].CopyUrl);
        }
        finally
        {
            PublishShareViewModel.HttpClientOverrideForTesting = null;
            PublishShareViewModel.AllowUnresolvableUrlsForTesting = false;
            CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
            client.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Tests that a failed remote preview surfaces an inline error instead of children.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExpandCloudOnlyCatalog_DownloadFails_ShowsInlineErrorAsync()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "broken",
            CatalogName = "Broken",
            FileId = "broken-id",
            FileName = "catalog-broken.json",
            Url = "https://example.com/catalog-broken.json",
            FileSize = 128,
        });
        vm.RefreshHostedAssets();
        var row = vm.HostedAssets.Single(a => a.Name == "catalog-broken.json");

        var handler = new CountingJsonHandler("not valid json {{{");
        var client = new HttpClient(handler);
        PublishShareViewModel.HttpClientOverrideForTesting = client;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
        try
        {
            row.IsExpanded = true;
            await WaitForPreviewAsync(row);

            Assert.False(row.RemotePreviewLoaded);
            Assert.False(string.IsNullOrWhiteSpace(row.ChildrenLoadError));
            Assert.False(row.HasChildren);
        }
        finally
        {
            PublishShareViewModel.HttpClientOverrideForTesting = null;
            CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
            client.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Tests that remote previews are cached per URL across collapse and expand cycles.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExpandCloudOnlyCatalog_Twice_DownloadsOnceAsync()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "cached",
            CatalogName = "Cached",
            FileId = "cached-id",
            FileName = "catalog-cached.json",
            Url = "https://example.com/catalog-cached.json",
            FileSize = 128,
        });
        vm.RefreshHostedAssets();
        var row = vm.HostedAssets.Single(a => a.Name == "catalog-cached.json");

        var catalog = new PublisherCatalog
        {
            Content = [new CatalogContentItem { Id = "c", Name = "Cached Mod", ContentType = GenHub.Core.Models.Enums.ContentType.Mod }],
        };
        var handler = new CountingJsonHandler(JsonSerializer.Serialize(catalog, PublisherJsonOptions.Definition), "catalog-cached.json");
        var client = new HttpClient(handler);
        PublishShareViewModel.HttpClientOverrideForTesting = client;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
        try
        {
            row.IsExpanded = true;
            await WaitForPreviewAsync(row);
            Assert.Single(row.Children);

            vm.RefreshHostedAssets();
            var freshRow = vm.HostedAssets.Single(a => a.Name == "catalog-cached.json");
            freshRow.IsExpanded = true;
            await WaitForPreviewAsync(freshRow);

            Assert.Equal(1, handler.CallCount);
            Assert.Single(freshRow.Children);
        }
        finally
        {
            PublishShareViewModel.HttpClientOverrideForTesting = null;
            CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
            client.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Tests that catalog content children carry the catalog URL for click-to-copy.
    /// </summary>
    [Fact]
    public void CatalogChildren_CarryCatalogUrlForCopy()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        const string catalogUrl = "https://drive.google.com/uc?export=download&id=mainfile";
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "main",
            CatalogName = "Main Catalog",
            FileId = "main-file-id",
            FileName = "catalog-main.json",
            Url = catalogUrl,
            FileSize = 128,
        });
        vm.RefreshHostedAssets();

        var main = vm.HostedAssets.Single(a => a.IsCatalog && a.CatalogId == "main");
        Assert.Equal(catalogUrl, main.Url);
        Assert.All(main.Children, c => Assert.Equal(catalogUrl, c.CopyUrl));

        var definition = vm.HostedAssets.Single(a => a.IsDefinition);
        var mainChild = definition.Children.Single(c => c.Name == "Main Catalog");
        Assert.Equal(catalogUrl, mainChild.CopyUrl);

        var artifact = vm.HostedAssets.Single(a => a.Name == "cool-mod.zip");
        Assert.All(artifact.Children, c => Assert.Equal(artifact.Url, c.CopyUrl));
    }

    /// <summary>
    /// Tests that deleting the active definition clears published state and marks it stale.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_ActiveDefinition_ClearsPublishedStateAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        const string definitionUrl = "https://drive.google.com/uc?export=download&id=def";
        state.Definition = new HostedFileInfo
        {
            FileId = "def-id",
            FileName = "publisher.json",
            Url = definitionUrl,
            FileSize = 256,
        };
        vm.ProviderDefinitionUrl = definitionUrl;
        var staleCount = 0;
        vm.DefinitionStaleCallback = () => staleCount++;
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var definition = vm.HostedAssets.Single(a => a.IsDefinition && a.Name == "publisher.json");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(definition);

        provider.Verify(p => p.DeleteFileAsync("def-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(state.Definition);
        Assert.Equal(string.Empty, vm.ProviderDefinitionUrl);
        Assert.True(vm.HasDefinitionChanges);
        Assert.Equal(1, staleCount);
    }

    /// <summary>
    /// Tests that deleting a non-active discovered definition leaves the active one intact.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_NonActiveDefinition_DeletesRemoteOnlyAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        const string activeUrl = "https://drive.google.com/uc?export=download&id=active-def";
        state.Definition = new HostedFileInfo
        {
            FileId = "active-def-id",
            FileName = "publisher.json",
            Url = activeUrl,
            FileSize = 256,
        };
        state.Definitions.Add(new HostedFileInfo
        {
            FileId = "other-def-id",
            FileName = "publisher-old.json",
            Url = "https://drive.google.com/uc?export=download&id=other-def",
            FileSize = 128,
        });
        vm.ProviderDefinitionUrl = activeUrl;
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var other = vm.HostedAssets.Single(a => a.Name == "publisher-old.json");
        vm.HasDefinitionChanges = false;

        await vm.DeleteHostedAssetCommand.ExecuteAsync(other);

        provider.Verify(p => p.DeleteFileAsync("other-def-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(state.Definition);
        Assert.Equal(activeUrl, vm.ProviderDefinitionUrl);
        Assert.False(vm.HasDefinitionChanges);
    }

    /// <summary>
    /// Tests that deleting an artifact row keeps the remote file when media still references the URL.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_SharedArtifactMediaUrl_KeepsRemoteFileAsync()
    {
        var project = CreateInventoryProject();
        var metadata = project.Catalogs[0].Catalog.Content[0].Metadata;
        Assert.NotNull(metadata);
        metadata.ScreenshotUrls.Add(CloudArtifactUrl);
        var provider = CreateDriveProvider();
        var notifications = new Mock<INotificationService>();
        string? successMessage = null;
        notifications.Setup(n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()))
            .Callback<string, string, int?, bool>((_, message, _, _) => successMessage = message);
        using var vm = CreateViewModel(project, provider, notifications);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "modfile-id",
            FileName = "cool-mod.zip",
            Url = CloudArtifactUrl,
            FileSize = 1024,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var artifact = vm.HostedAssets.Single(a => a.Name == "cool-mod.zip");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(artifact);

        provider.Verify(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts);
        Assert.Contains(CloudArtifactUrl, metadata.ScreenshotUrls);
        Assert.NotNull(successMessage);
        Assert.Contains("kept", successMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that expanding a cloud catalog whose JSON has null content shows an empty
    /// preview instead of faulting the background load.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExpandCloudOnlyCatalog_NullContent_ShowsEmptyWithoutThrowingAsync()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "nullcontent",
            CatalogName = "Null Content",
            FileId = "nullcontent-id",
            FileName = "catalog-null.json",
            Url = "https://example.com/catalog-null.json",
            FileSize = 64,
        });
        vm.RefreshHostedAssets();
        var row = vm.HostedAssets.Single(a => a.Name == "catalog-null.json");

        var catalog = new PublisherCatalog { Content = null! };
        var handler = new CountingJsonHandler(JsonSerializer.Serialize(catalog, PublisherJsonOptions.Definition));
        var client = new HttpClient(handler);
        PublishShareViewModel.HttpClientOverrideForTesting = client;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
        try
        {
            row.IsExpanded = true;
            await WaitForPreviewAsync(row);

            Assert.Empty(row.Children);
            Assert.True(row.RemotePreviewLoaded);
            Assert.Null(row.ChildrenLoadError);
        }
        finally
        {
            PublishShareViewModel.HttpClientOverrideForTesting = null;
            CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
            client.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Tests that uploading an addon-release artifact row uploads the file and links the
    /// returned URL instead of silently doing nothing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadHostedAsset_AddonReleaseArtifact_UploadsAndLinksAsync()
    {
        var project = CreateInventoryProject();
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "addon-payload");
            project.Catalogs[0].Catalog.Content[0].AddonReleases.Add(new ContentRelease
            {
                Version = "1.0.1",
                Artifacts =
                [
                    new ReleaseArtifact
                    {
                        Filename = "addon-patch.zip",
                        LocalFilePath = tempFile,
                        Size = 13,
                    },
                ],
            });
            var provider = CreateDriveProvider();
            const string directUrl = "https://drive.google.com/uc?export=download&id=addon-patch";
            provider.Setup(p => p.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                {
                    FileId = "addon-patch-id",
                    PublicUrl = directUrl,
                    DirectDownloadUrl = directUrl,
                    FileSize = 13,
                }));
            using var vm = CreateViewModel(project, provider);
            vm.RefreshHostedAssets();
            var row = vm.HostedAssets.Single(a => a.Name == "addon-patch.zip");

            await vm.UploadHostedAssetCommand.ExecuteAsync(row);

            provider.Verify(p => p.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(directUrl, project.Catalogs[0].Catalog.Content[0].AddonReleases[0].Artifacts[0].DownloadUrl);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Tests that an artifact row keeps the persisted file ID and size when the only
    /// matching hosting entry has no resolved URL.
    /// </summary>
    [Fact]
    public void RefreshHostedAssets_ArtifactWithUrlAndUnresolvedStateEntry_KeepsFileIdAndSize()
    {
        var project = CreateInventoryProject();
        project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts.Add(new ReleaseArtifact
        {
            Filename = "dropbox-mod.zip",
            DownloadUrl = "https://www.dropbox.com/s/x/dropbox-mod.zip?dl=0",
            Size = 0,
        });
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "dropbox-file-id",
            FileName = "dropbox-mod.zip",
            Url = string.Empty,
            FileSize = 777,
        });
        vm.RefreshHostedAssets();

        var row = vm.HostedAssets.Single(a => a.Name == "dropbox-mod.zip");
        Assert.Equal("dropbox-file-id", row.FileId);
        Assert.Equal(777, row.FileSize);
    }

    /// <summary>
    /// Tests that several unresolved same-named entries leave the row without a file ID
    /// instead of resolving to another entry's remote file.
    /// </summary>
    [Fact]
    public void RefreshHostedAssets_AmbiguousUnresolvedEntries_LeavesFileIdEmpty()
    {
        var project = CreateInventoryProject();
        project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts.Add(new ReleaseArtifact
        {
            Filename = "dup.zip",
            DownloadUrl = "https://www.dropbox.com/s/x/dup.zip?dl=0",
            Size = 0,
        });
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "dup-id-one",
            FileName = "dup.zip",
            Url = string.Empty,
            FileSize = 100,
        });
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "dup-id-two",
            FileName = "dup.zip",
            Url = string.Empty,
            FileSize = 200,
        });
        vm.RefreshHostedAssets();

        var row = vm.HostedAssets.Single(a => a.Name == "dup.zip");
        Assert.Equal(string.Empty, row.FileId);
    }

    /// <summary>
    /// Tests that a throwing preview download surfaces an inline error instead of
    /// faulting the background load without feedback.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExpandCloudOnlyCatalog_DownloadThrows_ShowsInlineErrorAsync()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "throwing",
            CatalogName = "Throwing",
            FileId = "throwing-id",
            FileName = "catalog-throwing.json",
            Url = "https://example.com/catalog-throwing.json",
            FileSize = 64,
        });
        vm.RefreshHostedAssets();
        var row = vm.HostedAssets.Single(a => a.Name == "catalog-throwing.json");

        var handler = new ThrowingHandler(new HttpRequestException("boom"));
        var client = new HttpClient(handler);
        PublishShareViewModel.HttpClientOverrideForTesting = client;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
        try
        {
            row.IsExpanded = true;
            await WaitForPreviewAsync(row);

            Assert.Empty(row.Children);
            Assert.False(row.RemotePreviewLoaded);
            Assert.NotNull(row.ChildrenLoadError);
        }
        finally
        {
            PublishShareViewModel.HttpClientOverrideForTesting = null;
            CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
            client.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Tests that a size mismatch keeps an unresolved same-named entry from resolving
    /// to another file's remote file ID.
    /// </summary>
    [Fact]
    public void RefreshHostedAssets_SizeMismatch_DoesNotResolveUnresolvedEntry()
    {
        var project = CreateInventoryProject();
        project.Catalogs[0].Catalog.Content[0].Releases[0].Artifacts.Add(new ReleaseArtifact
        {
            Filename = "dropbox-mod.zip",
            DownloadUrl = "https://www.dropbox.com/s/x/dropbox-mod.zip?dl=0",
            Size = 50,
        });
        using var vm = CreateViewModel(project, CreateDriveProvider());
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "dropbox-file-id",
            FileName = "dropbox-mod.zip",
            Url = string.Empty,
            FileSize = 777,
        });
        vm.RefreshHostedAssets();

        var row = vm.HostedAssets.Single(a => a.Name == "dropbox-mod.zip");
        Assert.Equal(string.Empty, row.FileId);
    }

    /// <summary>
    /// Tests that deleting an artifact row keeps the hosting-state entry when the remote
    /// file is kept for remaining media references.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_SharedArtifactMediaUrl_KeepsStateEntryAsync()
    {
        var project = CreateInventoryProject();
        var metadata = project.Catalogs[0].Catalog.Content[0].Metadata;
        Assert.NotNull(metadata);
        metadata.ScreenshotUrls.Add(CloudArtifactUrl);
        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "modfile-id",
            FileName = "cool-mod.zip",
            Url = CloudArtifactUrl,
            FileSize = 1024,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var artifact = vm.HostedAssets.Single(a => a.Name == "cool-mod.zip");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(artifact);

        provider.Verify(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(state.Artifacts, a => a.FileId == "modfile-id");
    }

    /// <summary>
    /// Tests that an artifact delete warns instead of reporting success when the hosting
    /// state cannot be saved.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_ArtifactDeleteSaveFails_WarnsAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        var notifications = new Mock<INotificationService>();
        string? warningMessage = null;
        notifications.Setup(n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()))
            .Callback<string, string, int?, bool>((_, message, _, _) => warningMessage = message);
        using var vm = CreateViewModel(project, provider, notifications, saveSucceeds: false);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "modfile-id",
            FileName = "cool-mod.zip",
            Url = CloudArtifactUrl,
            FileSize = 1024,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var artifact = vm.HostedAssets.Single(a => a.Name == "cool-mod.zip");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(artifact);

        Assert.NotNull(warningMessage);
        Assert.Contains("could not be saved", warningMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that a definition delete warns instead of reporting success when the hosting
    /// state cannot be saved.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_DefinitionDeleteSaveFails_WarnsAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        var notifications = new Mock<INotificationService>();
        string? warningMessage = null;
        notifications.Setup(n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()))
            .Callback<string, string, int?, bool>((_, message, _, _) => warningMessage = message);
        using var vm = CreateViewModel(project, provider, notifications, saveSucceeds: false);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        const string activeUrl = "https://drive.google.com/uc?export=download&id=active-def";
        state.Definition = new HostedFileInfo
        {
            FileId = "active-def-id",
            FileName = "publisher.json",
            Url = activeUrl,
            FileSize = 256,
        };
        vm.ProviderDefinitionUrl = activeUrl;
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var definition = vm.HostedAssets.Single(a => a.Name == "publisher.json");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(definition);

        provider.Verify(p => p.DeleteFileAsync("active-def-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(warningMessage);
        Assert.Contains("could not be saved", warningMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that an unlinked file delete warns instead of reporting success when the
    /// hosting state cannot be saved.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_UnlinkedDeleteSaveFails_WarnsAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        var notifications = new Mock<INotificationService>();
        string? warningMessage = null;
        notifications.Setup(n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()))
            .Callback<string, string, int?, bool>((_, message, _, _) => warningMessage = message);
        using var vm = CreateViewModel(project, provider, notifications, saveSucceeds: false);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "orphan-id",
            FileName = "orphan.zip",
            Url = "https://drive.google.com/uc?export=download&id=orphan",
            FileSize = 64,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var orphan = vm.HostedAssets.Single(a => a.Name == "orphan.zip");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(orphan);

        provider.Verify(p => p.DeleteFileAsync("orphan-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(warningMessage);
        Assert.Contains("could not be saved", warningMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that a catalog delete keeps the local project catalog when remote cleanup fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_CatalogRemoteFailure_KeepsProjectCatalogAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        provider.Setup(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("offline"));
        var notifications = new Mock<INotificationService>();
        using var vm = CreateViewModel(project, provider, notifications);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "maps",
            CatalogName = "Maps",
            FileId = "maps-file-id",
            FileName = "catalog-maps.json",
            Url = "https://drive.google.com/uc?export=download&id=mapsfile",
            FileSize = 128,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var catalogRow = vm.HostedAssets.Single(a => a.IsCatalog && a.CatalogId == "maps");

        await vm.DeleteHostedAssetCommand.ExecuteAsync(catalogRow);

        Assert.Equal(2, project.Catalogs.Count);
        Assert.Contains(state.Catalogs, c => c.CatalogId == "maps");
        notifications.Verify(n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Tests that catalog child rows show the localized content-type display name
    /// instead of the raw enum value.
    /// </summary>
    [Fact]
    public void RefreshHostedAssets_CatalogChildren_UseContentTypeDisplayName()
    {
        var project = CreateInventoryProject();
        using var vm = CreateViewModel(project, CreateDriveProvider());
        vm.RefreshHostedAssets();

        var row = vm.HostedAssets.Single(a => a.Name == "catalog-main.json");
        var child = Assert.Single(row.Children, c => c.Name == "Cool Mod");
        Assert.StartsWith("Mods ·", child.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that the remote preview cache bounds its memory usage and evicts the oldest entry in FIFO order.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task RemotePreviewCache_ExceedsCapacity_EvictsOldestFifoAsync()
    {
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            ProjectName = "Test",
            Catalog = new PublisherCatalog(),
        };

        var sampleCatalog = new PublisherCatalog
        {
            Content = [new CatalogContentItem { Id = "c", Name = "Mod", ContentType = GenHub.Core.Models.Enums.ContentType.Mod }],
        };
        var catalogJson = JsonSerializer.Serialize(sampleCatalog, PublisherJsonOptions.Definition);

        var handler = new CountingJsonHandler(catalogJson);
        var client = new HttpClient(handler);
        PublishShareViewModel.HttpClientOverrideForTesting = client;
        PublishShareViewModel.AllowUnresolvableUrlsForTesting = true;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;

        try
        {
            using var vm = CreateViewModel(project, CreateDriveProvider());
            var state = vm.CurrentHostingState;
            Assert.NotNull(state);
            for (var i = 0; i <= HostingConstants.MaxRemotePreviewCacheEntries; i++)
            {
                state.Catalogs.Add(new CatalogHostingInfo
                {
                    CatalogId = $"cat-{i}",
                    CatalogName = $"Catalog {i}",
                    FileName = $"catalog-{i}.json",
                    Url = $"https://example.com/catalog-{i}.json",
                    FileSize = 100,
                });
            }

            vm.RefreshHostedAssets();

            for (var i = 0; i <= HostingConstants.MaxRemotePreviewCacheEntries; i++)
            {
                var row = vm.HostedAssets.Single(a => a.Name == $"catalog-{i}.json");
                row.IsExpanded = true;
                await WaitForPreviewAsync(row);
            }

            var callsAfterFill = handler.CallCount;
            Assert.Equal(HostingConstants.MaxRemotePreviewCacheEntries + 1, callsAfterFill);

            // Re-expanding entry 1 (which should still be in cache) must NOT trigger a new fetch
            var row1 = vm.HostedAssets.Single(a => a.Name == "catalog-1.json");
            row1.IsExpanded = false;
            row1.RemotePreviewLoaded = false;
            row1.IsExpanded = true;
            await WaitForPreviewAsync(row1);
            Assert.Equal(callsAfterFill, handler.CallCount);

            // Re-expanding entry 0 (which was evicted) MUST trigger a new fetch
            var row0 = vm.HostedAssets.Single(a => a.Name == "catalog-0.json");
            row0.IsExpanded = false;
            row0.RemotePreviewLoaded = false;
            row0.IsExpanded = true;
            await WaitForPreviewAsync(row0);
            Assert.Equal(callsAfterFill + 1, handler.CallCount);
        }
        finally
        {
            PublishShareViewModel.HttpClientOverrideForTesting = null;
            PublishShareViewModel.AllowUnresolvableUrlsForTesting = false;
            CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
            client.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Tests that multiple external assets with size 0 referencing the same URL trigger only a single probe.
    /// </summary>
    [Fact]
    public void TrackExternalProbe_DeduplicatesConcurrentProbesForSameUrl()
    {
        var project = CreateInventoryProject();
        var sharedUrl = "https://example.com/shared-probe-target.zip";
        var content = project.Catalogs[0].Catalog.Content[0];
        content.Releases[0].Artifacts =
        [
            new ReleaseArtifact { Filename = "part1.zip", DownloadUrl = sharedUrl, Size = 0 },
            new ReleaseArtifact { Filename = "part2.zip", DownloadUrl = sharedUrl, Size = 0 },
        ];

        var handler = new CountingJsonHandler("{}", "shared-probe-target.zip", "application/zip");
        var client = new HttpClient(handler);
        PublishShareViewModel.HttpClientOverrideForTesting = client;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
        try
        {
            using var vm = CreateViewModel(project, CreateDriveProvider());

            Assert.NotEmpty(vm.HostedAssets);
            Assert.Equal(1, handler.CallCount);
        }
        finally
        {
            PublishShareViewModel.HttpClientOverrideForTesting = null;
            CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
            client.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Tests that ContainsLinkedDirectory rejects paths when parent directories are symlinks.
    /// </summary>
    [Fact]
    public void ContainsLinkedDirectory_DetectsSymlinkedParentDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var projectRoot = Path.Combine(tempDir, "project") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(projectRoot);

            var realTarget = Path.Combine(tempDir, "outside_target");
            Directory.CreateDirectory(realTarget);

            var symlinkDir = Path.Combine(projectRoot, "symlink_dir");
            try
            {
                Directory.CreateSymbolicLink(symlinkDir, realTarget);
            }
            catch (UnauthorizedAccessException)
            {
                // Windows runners without developer mode or SeCreateSymbolicLinkPrivilege cannot create symlinks
                return;
            }
            catch (IOException)
            {
                return;
            }

            var fileUnderSymlink = Path.Combine(symlinkDir, "art.png");
            File.WriteAllText(Path.Combine(realTarget, "art.png"), "dummy");

            var normalSubdir = Path.Combine(projectRoot, "normal_dir");
            Directory.CreateDirectory(normalSubdir);
            var normalFile = Path.Combine(normalSubdir, "art.png");
            File.WriteAllText(normalFile, "dummy");

            Assert.True(PublishShareViewModel.ContainsLinkedDirectory(projectRoot, fileUnderSymlink));
            Assert.False(PublishShareViewModel.ContainsLinkedDirectory(projectRoot, normalFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Tests that updating project artifact sizes propagates probed sizes to addon releases.
    /// </summary>
    [Fact]
    public void UpdateProjectArtifactSizes_PropagatesSizeToAddonReleases()
    {
        var project = new PublisherStudioProject
        {
            ProjectName = "Addon Test",
            ProjectPath = "/dummy/project",
        };
        var catalog = new PublisherCatalog();
        var item = new CatalogContentItem
        {
            Id = "mod1",
            Name = "Mod 1",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
        };
        var addonRelease = new ContentRelease
        {
            Version = "1.0.0",
            Artifacts =
            [
                new ReleaseArtifact
                {
                    Filename = "patch.zip",
                    DownloadUrl = "https://example.com/patch.zip",
                    Size = 0,
                },
            ],
        };
        item.AddonReleases.Add(addonRelease);
        catalog.Content.Add(item);
        project.Catalogs.Add(new NamedCatalog { Name = "Cat1", Catalog = catalog });

        using var vm = CreateViewModel(project, CreateDriveProvider());
        vm.UpdateProjectArtifactSizesForTesting("https://example.com/patch.zip", 4242);

        Assert.Equal(4242, addonRelease.Artifacts[0].Size);
    }

    /// <summary>
    /// Tests that <see cref="PublishShareViewModel.AllowUnresolvableUrlsForTesting"/> defaults to false and can be toggled.
    /// </summary>
    [Fact]
    public void AllowUnresolvableUrlsForTesting_DefaultsToFalse_CanBeToggled()
    {
        Assert.False(PublishShareViewModel.AllowUnresolvableUrlsForTesting);
        PublishShareViewModel.AllowUnresolvableUrlsForTesting = true;
        try
        {
            Assert.True(PublishShareViewModel.AllowUnresolvableUrlsForTesting);
        }
        finally
        {
            PublishShareViewModel.AllowUnresolvableUrlsForTesting = false;
        }
    }

    /// <summary>
    /// Tests that Publisher.AvatarUrl and NamedCatalog.IconUrl are indexed in the linkage index,
    /// showing as linked in the inventory rather than unlinked.
    /// </summary>
    [Fact]
    public void BuildUrlReferenceIndex_IndexesPublisherAvatarAndCatalogIcons()
    {
        var project = CreateInventoryProject();
        const string avatarUrl = "https://drive.google.com/uc?export=download&id=pub-avatar";
        const string iconUrl = "https://drive.google.com/uc?export=download&id=cat-icon";

        project.Catalog!.Publisher!.AvatarUrl = avatarUrl;
        project.Catalogs[0].IconUrl = iconUrl;

        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);

        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "avatar-file-id",
            FileName = "avatar.png",
            Url = avatarUrl,
            FileSize = 2048,
        });
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "icon-file-id",
            FileName = "icon.png",
            Url = iconUrl,
            FileSize = 1024,
        });

        vm.RefreshHostedAssets();

        var avatarAsset = vm.HostedAssets.Single(a => a.Name == "avatar.png");
        var iconAsset = vm.HostedAssets.Single(a => a.Name == "icon.png");

        Assert.Equal(1, avatarAsset.LinkCount);
        Assert.NotEqual("Not linked", avatarAsset.LinkedToText);

        Assert.Equal(1, iconAsset.LinkCount);
        Assert.NotEqual("Not linked", iconAsset.LinkedToText);
    }

    /// <summary>
    /// Tests that deleting a hosted asset linked to Publisher.AvatarUrl or NamedCatalog.IconUrl
    /// calls DeleteFileAsync and clears the respective URL references in the project.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_PublisherAvatarAndCatalogIcon_UnlinksAndReplacesUrlsAsync()
    {
        var project = CreateInventoryProject();
        const string avatarUrl = "https://drive.google.com/uc?export=download&id=pub-avatar";
        const string iconUrl = "https://drive.google.com/uc?export=download&id=cat-icon";

        project.Catalog!.Publisher!.AvatarUrl = avatarUrl;
        project.Catalogs[0].IconUrl = iconUrl;

        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);

        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "avatar-file-id",
            FileName = "avatar.png",
            Url = avatarUrl,
            FileSize = 2048,
        });
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "icon-file-id",
            FileName = "icon.png",
            Url = iconUrl,
            FileSize = 1024,
        });

        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);

        var avatarAsset = vm.HostedAssets.Single(a => a.Name == "avatar.png");
        await vm.DeleteHostedAssetCommand.ExecuteAsync(avatarAsset);

        provider.Verify(p => p.DeleteFileAsync("avatar-file-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(project.Catalog.Publisher.AvatarUrl);
        Assert.Null(vm.UploadHierarchy.AvatarUrl);

        var iconAsset = vm.HostedAssets.Single(a => a.Name == "icon.png");
        await vm.DeleteHostedAssetCommand.ExecuteAsync(iconAsset);

        provider.Verify(p => p.DeleteFileAsync("icon-file-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(project.Catalogs[0].IconUrl);
    }

    /// <summary>
    /// Tests that catalog remote cleanup keeps a remote icon file shared with the publisher avatar.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteCatalogRemotes_SharedPublisherAvatar_KeepsRemoteFileAsync()
    {
        var project = CreateInventoryProject();
        const string sharedUrl = "https://drive.google.com/uc?export=download&id=shared-icon";
        project.Catalog!.Publisher!.AvatarUrl = sharedUrl;
        project.Catalogs[0].IconUrl = sharedUrl;

        var provider = CreateDriveProvider();
        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Catalogs.Add(new CatalogHostingInfo
        {
            CatalogId = "main",
            CatalogName = "Main Catalog",
            FileId = "catalog-file-id",
            FileName = "catalog-main.json",
            Url = "https://drive.google.com/uc?export=download&id=catfile",
        });
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "shared-icon-file-id",
            FileName = "icon.png",
            Url = sharedUrl,
            FileSize = 1024,
        });
        vm.RefreshHostedAssets();

        var cleaned = await vm.DeleteCatalogRemotesAsync("main");

        Assert.True(cleaned);
        provider.Verify(p => p.DeleteFileAsync("catalog-file-id", It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(p => p.DeleteFileAsync("shared-icon-file-id", It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(state.Artifacts, a => a.FileId == "shared-icon-file-id");
    }

    /// <summary>
    /// Tests that deleting a hosted asset holds the publish gate, preventing concurrent publish operations.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteHostedAsset_HoldsPublishGateDuringDeleteAsync()
    {
        var project = CreateInventoryProject();
        var provider = CreateDriveProvider();
        var deleteStarted = new TaskCompletionSource<bool>();
        var allowDeleteToComplete = new TaskCompletionSource<bool>();
        var gateAcquiredDuringDelete = false;

        provider
            .Setup(p => p.DeleteFileAsync("modfile-id", It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken _) =>
            {
                deleteStarted.TrySetResult(true);
                await allowDeleteToComplete.Task;
                return OperationResult<bool>.CreateSuccess(true);
            });

        using var vm = CreateViewModel(project, provider);
        var state = vm.CurrentHostingState;
        Assert.NotNull(state);
        state.Artifacts.Add(new ArtifactHostingInfo
        {
            FileId = "modfile-id",
            FileName = "cool-mod.zip",
            Url = CloudArtifactUrl,
            FileSize = 1024,
        });
        vm.RefreshHostedAssets();
        vm.ConfirmationCallback = (_, _) => Task.FromResult(true);
        var artifact = vm.HostedAssets.Single(a => a.Name == "cool-mod.zip");

        var deleteExecutionTask = vm.DeleteHostedAssetCommand.ExecuteAsync(artifact);

        await deleteStarted.Task;

        var (acquired, cts) = await vm.TryBeginPublishAsyncForTesting();
        gateAcquiredDuringDelete = acquired;
        cts?.Dispose();

        allowDeleteToComplete.TrySetResult(true);
        await deleteExecutionTask;

        Assert.False(gateAcquiredDuringDelete);
    }

    /// <summary>
    /// Tests that downloading catalog string from a loopback address is strictly rejected
    /// even if bypass flags are turned on.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExpandCloudOnlyCatalog_LoopbackUrl_StrictlyRejectedEvenWithBypassFlagAsync()
    {
        var project = CreateInventoryProject();
        var handler = new CountingJsonHandler("{}", "127.0.0.1");
        var client = new HttpClient(handler);

        try
        {
            using var vm = CreateViewModel(project, CreateDriveProvider());
            var state = vm.CurrentHostingState;
            Assert.NotNull(state);
            const string loopbackUrl = "https://127.0.0.1/catalog.json";
            state.Catalogs.Add(new CatalogHostingInfo
            {
                CatalogId = "loopback-cat",
                CatalogName = "Loopback Catalog",
                FileName = "catalog-loopback.json",
                Url = loopbackUrl,
                FileSize = 100,
            });

            vm.RefreshHostedAssets();

            var row = vm.HostedAssets.Single(a => a.Name == "catalog-loopback.json");
            PublishShareViewModel.HttpClientOverrideForTesting = client;
            PublishShareViewModel.AllowUnresolvableUrlsForTesting = true;
            CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;

            row.IsExpanded = true;
            await WaitForPreviewAsync(row);

            Assert.Equal(0, handler.CallCount);
            Assert.False(row.RemotePreviewLoaded);
        }
        finally
        {
            PublishShareViewModel.HttpClientOverrideForTesting = null;
            PublishShareViewModel.AllowUnresolvableUrlsForTesting = false;
            CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
            client.Dispose();
            handler.Dispose();
        }
    }

    private static async Task WaitForPreviewAsync(HostedAssetItemViewModel row)
    {
        for (var i = 0; i < 200 && !row.RemotePreviewLoaded && row.ChildrenLoadError == null; i++)
        {
            await Task.Delay(25);
        }
    }

    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw error;
        }
    }

    private sealed class CountingJsonHandler(string json, string? countedUrlSubstring = null, string mediaType = "application/json") : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (countedUrlSubstring == null ||
                uri.Contains(countedUrlSubstring, StringComparison.OrdinalIgnoreCase))
            {
                CallCount++;
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
            };

            if (request.Method == HttpMethod.Head)
            {
                response.Content = new ByteArrayContent([]);
                response.Content.Headers.ContentLength = 100;
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            }
            else
            {
                response.Content = new StringContent(json, Encoding.UTF8, mediaType);
                response.Content.Headers.ContentLength = 100;
            }

            return Task.FromResult(response);
        }
    }

    private static PublisherStudioProject CreateInventoryProject()
    {
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            ProjectName = "Test Publisher",
            ProviderDefinitionFileName = "publisher.json",
            Catalog = new PublisherCatalog
            {
                Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            },
        };

        var main = new NamedCatalog
        {
            Id = "main",
            Name = "Main Catalog",
            FileName = "catalog-main.json",
            Catalog = new PublisherCatalog
            {
                Content =
                [
                    new CatalogContentItem
                    {
                        Id = "mod-1",
                        Name = "Cool Mod",
                        Description = "A cool mod",
                        ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
                        Releases =
                        [
                            new ContentRelease
                            {
                                Version = "1.0.0",
                                Artifacts =
                                [
                                    new ReleaseArtifact
                                    {
                                        Filename = "cool-mod.zip",
                                        DownloadUrl = CloudArtifactUrl,
                                        Size = 1024,
                                        Sha256 = "abc",
                                    },
                                ],
                                ImageUrls = ["https://drive.google.com/uc?export=download&id=relimg"],
                                VideoUrls = [ExternalTrailerUrl],
                            },
                        ],
                        Metadata = new ContentRichMetadata
                        {
                            ScreenshotUrls = [CloudScreenshotUrl, ExternalScreenshotUrl],
                            VideoUrl = CloudVideoUrl,
                        },
                    },
                ],
            },
        };

        var maps = new NamedCatalog
        {
            Id = "maps",
            Name = "Maps",
            FileName = "catalog-maps.json",
            Catalog = new PublisherCatalog(),
        };

        project.Catalogs.Add(main);
        project.Catalogs.Add(maps);
        return project;
    }

    private static Mock<IHostingProvider> CreateDriveProvider()
    {
        var provider = new Mock<IHostingProvider>();
        provider.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        provider.Setup(p => p.DisplayName).Returns("Google Drive");
        provider.Setup(p => p.RequiresAuthentication).Returns(true);
        provider.Setup(p => p.IsAuthenticated).Returns(true);
        provider.Setup(p => p.SupportsArtifactHosting).Returns(true);
        provider.Setup(p => p.SupportsCatalogHosting).Returns(true);
        provider.Setup(p => p.SupportsUpdate).Returns(true);
        provider.Setup(p => p.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        return provider;
    }

    private static PublishShareViewModel CreateViewModel(
        PublisherStudioProject project,
        Mock<IHostingProvider>? provider = null,
        Mock<INotificationService>? notifications = null,
        bool saveSucceeds = true)
    {
        var studioService = new Mock<IPublisherStudioService>();
        studioService.Setup(m => m.ValidateCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var stateManager = new Mock<IHostingStateManager>();
        stateManager.Setup(m => m.SaveStatesAsync(It.IsAny<string>(), It.IsAny<PublisherHostingStates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(saveSucceeds
                ? OperationResult<bool>.CreateSuccess(true)
                : OperationResult<bool>.CreateFailure("disk full"));

        var vm = new PublishShareViewModel(
            project,
            studioService.Object,
            NullLogger.Instance,
            hostingStateManager: stateManager.Object,
            notificationService: (notifications ?? new Mock<INotificationService>()).Object);
        if (provider != null)
        {
            vm.HostingProviders.Add(provider.Object);
            vm.SelectedHostingProvider = provider.Object;
        }

        return vm;
    }
}
