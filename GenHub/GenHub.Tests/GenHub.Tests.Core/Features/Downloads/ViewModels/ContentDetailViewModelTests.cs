using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Messages;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads.ViewModels;

/// <summary>
/// Regression tests for independent release/addon row downloads in the content detail view.
/// </summary>
public sealed class ContentDetailViewModelTests
{
    /// <summary>
    /// Verifies that downloading a release row preserves the typed Data payload and sets parentContentId.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReleaseRowDownload_PreservesDataPayloadAndSetsParentContentIdAsync()
    {
        // Arrange
        const string parentId = "GeneralsOnline_082826_QFE1";
        var releaseData = new GeneralsOnlineRelease
        {
            Version = "082826_QFE1",
            PortableUrl = "https://cdn.playgenerals.online/releases/GeneralsOnline_portable_082826_QFE1.zip",
        };

        var parent = new ContentSearchResult
        {
            Id = parentId,
            Name = "Generals Online",
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            ResolverId = GeneralsOnlineConstants.ResolverId,
            RequiresResolution = true,
            SourceUrl = "https://www.playgenerals.online/#download",
        };
        parent.SetData(releaseData);

        ContentSearchResult? coordinatorInput = null;
        var coordinator = new Mock<IContentDownloadCoordinator>();
        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken, bool>(
                (content, _, _, _) => coordinatorInput = content)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.828261.generalsonline.gameclient.60hz"),
                Name = "Generals Online",
                ContentType = ContentType.GameClient,
            }));

        var viewModel = CreateViewModel(parent, coordinator.Object);
        var releaseFile = new DownloadableFile(
            Name: "Generals Online",
            DownloadUrl: releaseData.PortableUrl,
            FileSectionType: FileSectionType.Downloads);
        viewModel.PopulateReleases([releaseFile]);

        var release = Assert.Single(viewModel.Releases);

        // Act
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(release.DownloadCommand).ExecuteAsync(null);

        // Assert
        Assert.NotNull(coordinatorInput);
        Assert.NotNull(coordinatorInput.Data);
        var extractedRelease = coordinatorInput.GetData<GeneralsOnlineRelease>();
        Assert.NotNull(extractedRelease);
        Assert.Equal("082826_QFE1", extractedRelease.Version);
        Assert.True(coordinatorInput.ResolverMetadata.TryGetValue(ContentConstants.ParentContentIdMetadataKey, out var recordedParentId));
        Assert.Equal(parentId, recordedParentId);
    }

    /// <summary>
    /// Verifies downloading a release row does not mark the parent card downloaded and adding it
    /// to a profile sends the exact child manifest produced by that row.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReleaseRowDownloadAndAddToProfile_UsesChildManifestWithoutUpdatingParentAsync()
    {
        // Arrange
        const string parentCatalogId = "moddb-parent-catalog-id";
        const string childManifestId = "1.20260102.moddb.map.lemuria";
        var parent = new ContentSearchResult
        {
            Id = parentCatalogId,
            Name = "Lemuria parent page",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverId = "ModDB",
            RequiresResolution = true,
            SourceUrl = "https://www.moddb.com/games/cc-generals-zero-hour/addons/lemuria-2026-fixes",
        };
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create(childManifestId),
            Name = "Lemuria 2026",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
        };
        ContentSearchResult? coordinatorInput = null;
        var coordinator = new Mock<IContentDownloadCoordinator>();
        coordinator
            .Setup(service => service.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken, bool>(
                (content, _, _, _) => coordinatorInput = content)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));
        var viewModel = CreateViewModel(parent, coordinator.Object);
        var releaseFile = new DownloadableFile(
            Name: "Lemuria_2026_Fixes.rar",
            Category: "Singleplayer Map",
            DownloadUrl: "https://www.moddb.com/addons/start/302328",
            FileSectionType: FileSectionType.Downloads);
        viewModel.PopulateReleases([releaseFile]);
        var release = Assert.Single(viewModel.Releases);

        // Act
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(release.DownloadCommand).ExecuteAsync(null);
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(release.AddToProfileCommand).ExecuteAsync(null);

        // Assert
        Assert.NotNull(coordinatorInput);
        Assert.NotEqual(parentCatalogId, coordinatorInput.Id);
        Assert.StartsWith(ContentConstants.FileContentIdPrefix, coordinatorInput.Id, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(coordinatorInput.Version));
        Assert.True(release.IsDownloaded);
        Assert.Equal(childManifestId, release.DownloadedManifestId);
        Assert.False(viewModel.IsDownloaded);
        Assert.Equal(childManifestId, viewModel.ProfileManifestId);
        Assert.Equal(releaseFile.Name, viewModel.ProfileContentName);
        Assert.Equal(GameType.ZeroHour, viewModel.ProfileTargetGame);
    }

    /// <summary>
    /// Verifies that when a release row matches a variant search result, the coordinator input
    /// uses the row's synthesized file ID (not the variant ID), inherits resolver metadata, and
    /// flips the row to downloaded upon completion.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReleaseRowDownload_WithVariantMatch_UsesRowFileIdAndInheritsResolverMetadataAsync()
    {
        // Arrange
        const string parentCatalogId = "genlauncher-parent";
        const string variantManifestId = "1.20260101.genlauncher.mod.variant1";
        const string childManifestId = "1.20260101.genlauncher.mod.shockwave";
        const string testResolver = "GenLauncher";

        var parent = new ContentSearchResult
        {
            Id = parentCatalogId,
            Name = "Shockwave",
            ProviderName = "GenLauncher",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverId = testResolver,
            RequiresResolution = true,
            SourceUrl = "https://example.com/shockwave",
        };

        var variant = new ContentSearchResult
        {
            Id = variantManifestId,
            Name = "Shockwave 1.2",
            Version = "1.2",
            ProviderName = "GenLauncher",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverId = testResolver,
            RequiresResolution = true,
            SourceUrl = "https://example.com/shockwave/1.2",
            SelectedDownloadUrl = "https://example.com/shockwave/1.2.zip",
        };
        variant.ResolverMetadata["variantKey"] = "variantValue";

        var variants = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            [variantManifestId] = variant,
        };

        ContentSearchResult? coordinatorInput = null;
        var coordinator = new Mock<IContentDownloadCoordinator>();
        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken, bool>(
                (content, _, _, _) => coordinatorInput = content)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create(childManifestId),
                Name = "Shockwave 1.2",
                ContentType = ContentType.Mod,
            }));

        var viewModel = CreateViewModel(parent, coordinator.Object, variantSearchResults: variants);
        var releaseFile = new DownloadableFile(
            Name: "Shockwave 1.2",
            DownloadUrl: "https://example.com/shockwave/1.2.zip",
            FileSectionType: FileSectionType.Downloads,
            Version: "1.2");
        viewModel.PopulateReleases([releaseFile]);
        var release = Assert.Single(viewModel.Releases);

        // Act
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(release.DownloadCommand).ExecuteAsync(null);

        // Assert
        Assert.NotNull(coordinatorInput);
        Assert.StartsWith(ContentConstants.FileContentIdPrefix, coordinatorInput.Id, StringComparison.Ordinal);
        Assert.NotEqual(variantManifestId, coordinatorInput.Id);
        Assert.NotEqual(parentCatalogId, coordinatorInput.Id);

        Assert.Equal(testResolver, coordinatorInput.ResolverId);
        Assert.True(coordinatorInput.ResolverMetadata.TryGetValue("variantKey", out var metadataVal));
        Assert.Equal("variantValue", metadataVal);

        Assert.True(release.IsDownloaded);
        Assert.Equal(childManifestId, release.DownloadedManifestId);
    }

    /// <summary>
    /// Verifies that a row whose file has no download URL can still download if the matching variant
    /// has a non-empty ResolverId, even if RequiresResolution is false.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReleaseRowDownload_WhenVariantHasResolverIdWithoutRequiresResolution_DownloadsSuccessfullyAsync()
    {
        // Arrange
        const string parentCatalogId = "test-parent";
        const string variantManifestId = "test.variant.1";
        const string childManifestId = "test.manifest.1";
        const string testResolver = "CustomResolver";

        var parent = new ContentSearchResult
        {
            Id = parentCatalogId,
            Name = "Parent",
            ProviderName = "Custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            RequiresResolution = false,
        };

        var variant = new ContentSearchResult
        {
            Id = variantManifestId,
            Name = "Variant File",
            ProviderName = "Custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverId = testResolver,
            RequiresResolution = false,
            SourceUrl = "https://example.com/details",
        };

        var variants = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            [variantManifestId] = variant,
        };

        ContentSearchResult? coordinatorInput = null;
        var coordinator = new Mock<IContentDownloadCoordinator>();
        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken, bool>(
                (content, _, _, _) => coordinatorInput = content)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create(childManifestId),
                Name = "Variant File",
                ContentType = ContentType.Mod,
            }));

        var viewModel = CreateViewModel(parent, coordinator.Object, variantSearchResults: variants);
        var fileWithoutUrl = new DownloadableFile(
            Name: "Variant File",
            DetailsUrl: "https://example.com/details",
            FileSectionType: FileSectionType.Downloads);
        viewModel.PopulateReleases([fileWithoutUrl]);
        var release = Assert.Single(viewModel.Releases);

        // Act
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(release.DownloadCommand).ExecuteAsync(null);

        // Assert
        Assert.NotNull(coordinatorInput);
        Assert.Equal(testResolver, coordinatorInput.ResolverId);
        Assert.True(release.IsDownloaded);
        Assert.Equal(childManifestId, release.DownloadedManifestId);
    }

    /// <summary>
    /// Verifies that attempting to select an item while a download is active is ignored.
    /// </summary>
    [Fact]
    public void SelectDownloadableItem_WhileDownloading_DoesNotChangeSelection()
    {
        // Arrange
        var parent = new ContentSearchResult
        {
            Id = "test-parent",
            Name = "Test",
            ProviderName = "communityoutpost",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };
        var viewModel = CreateViewModel(parent, new Mock<IContentDownloadCoordinator>().Object);
        var file1 = new DownloadableFile(Name: "Release 1", DownloadUrl: "https://example.com/1");
        var file2 = new DownloadableFile(Name: "Release 2", DownloadUrl: "https://example.com/2");
        viewModel.PopulateReleases([file1, file2]);

        Assert.Equal(2, viewModel.Releases.Count);
        var release1 = viewModel.Releases[0];
        var release2 = viewModel.Releases[1];

        // Release 1 is selected initially
        Assert.True(release1.IsSelected);
        Assert.True(release2.SelectCommand?.CanExecute(null));

        // Simulate downloading active
        viewModel.IsDownloading = true;
        Assert.False(release2.SelectCommand?.CanExecute(null));

        // Try selecting release 2
        release2.SelectCommand?.Execute(null);

        // Assert selection did not change
        Assert.True(release1.IsSelected);
        Assert.False(release2.IsSelected);
        Assert.Same(release1, viewModel.SelectedDownloadableItem);

        // Simulate downloading finished
        viewModel.IsDownloading = false;
        Assert.True(release2.SelectCommand?.CanExecute(null));
    }

    /// <summary>
    /// Verifies that PopulateReleases orders releases from newest to oldest.
    /// </summary>
    [Fact]
    public void PopulateReleases_OrdersReleasesNewestFirst()
    {
        // Arrange
        var parent = new ContentSearchResult
        {
            Id = "moddb-test-parent",
            Name = "Test Mod",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        var viewModel = CreateViewModel(parent, new Mock<IContentDownloadCoordinator>().Object);

        var oldFile = new DownloadableFile(
            Name: "Mod_v1.0.zip",
            UploadDate: new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/downloads/start/1",
            FileSectionType: FileSectionType.Downloads);

        var middleFile = new DownloadableFile(
            Name: "Mod_v2.0.zip",
            UploadDate: new DateTime(2018, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/downloads/start/2",
            FileSectionType: FileSectionType.Downloads);

        var newestFile = new DownloadableFile(
            Name: "Mod_v3.0.zip",
            UploadDate: new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/downloads/start/3",
            FileSectionType: FileSectionType.Downloads);

        // Act - populate in random/oldest-first order
        viewModel.PopulateReleases([oldFile, newestFile, middleFile]);

        // Assert - should be sorted newest first (v3.0, v2.0, v1.0)
        Assert.Equal(3, viewModel.Releases.Count);
        Assert.Equal("Mod_v3.0.zip", viewModel.Releases[0].Name);
        Assert.Equal("Mod_v2.0.zip", viewModel.Releases[1].Name);
        Assert.Equal("Mod_v1.0.zip", viewModel.Releases[2].Name);
    }

    /// <summary>
    /// Verifies that PopulateAddons orders addons from newest to oldest.
    /// </summary>
    [Fact]
    public void PopulateAddons_OrdersAddonsNewestFirst()
    {
        // Arrange
        var parent = new ContentSearchResult
        {
            Id = "moddb-test-parent",
            Name = "Test Mod",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        var viewModel = CreateViewModel(parent, new Mock<IContentDownloadCoordinator>().Object);

        var oldAddon = new DownloadableFile(
            Name: "MapPack_2012.zip",
            UploadDate: new DateTime(2012, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/addons/start/10",
            FileSectionType: FileSectionType.Addons);

        var newestAddon = new DownloadableFile(
            Name: "MapPack_2025.zip",
            UploadDate: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/addons/start/20",
            FileSectionType: FileSectionType.Addons);

        // Act
        viewModel.PopulateAddons([oldAddon, newestAddon]);

        // Assert - should be sorted newest first
        Assert.Equal(2, viewModel.Addons.Count);
        Assert.Equal("MapPack_2025.zip", viewModel.Addons[0].Name);
        Assert.Equal("MapPack_2012.zip", viewModel.Addons[1].Name);
    }

    /// <summary>
    /// Verifies that releases sharing an extensionless endpoint file name (for example
    /// OneDrive "/embed" links) are not collapsed into a single row.
    /// </summary>
    [Fact]
    public void PopulateReleases_WithSharedExtensionlessFilename_KeepsBothRows()
    {
        // Arrange
        var parent = new ContentSearchResult
        {
            Id = "genlauncher-zerohour-rise-of-the-reds",
            Name = "Rise of the Reds",
            ProviderName = "genlauncher",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        var viewModel = CreateViewModel(parent, new Mock<IContentDownloadCoordinator>().Object);

        var parentFile = new DownloadableFile(
            Name: "Rise Of The Reds 1.87 Public Build 2.0",
            Version: "1.87 Public Build 2.0",
            DownloadUrl: "https://onedrive.live.com/embed?cid=AFB01C08E053A64E&resid=AFB01C08E053A64E%21593",
            FileSectionType: FileSectionType.Downloads,
            Filename: "embed");

        var patchFile = new DownloadableFile(
            Name: "Balance Patch",
            Version: "2.999.06.5",
            DownloadUrl: "https://onedrive.live.com/embed?cid=0A88C98986A457EB&resid=A88C98986A457EB%21135",
            FileSectionType: FileSectionType.Downloads,
            Filename: "embed");

        // Act
        viewModel.PopulateReleases([parentFile, patchFile]);

        // Assert
        Assert.Equal(2, viewModel.Releases.Count);
    }

    /// <summary>
    /// After download, changing the Type dropdown must rewrite the stored manifest so tools
    /// misclassified as Addon become Executable/ModdingTool and lose game-install requirements.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SelectedContentType_WhenDownloaded_PersistsTypeAndClearsGameInstallDepsAsync()
    {
        // Arrange
        const string manifestId = "1.20260530.moddb.addon.genbigeditbigeditor";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "GenBigEdit(big editor)",
            ProviderName = "ModDB",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };
        var storedManifest = new ContentManifest
        {
            Id = ManifestId.Create(manifestId),
            Name = "GenBigEdit(big editor)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create("1.104.any.gameinstallation.zerohour"),
                    Name = "Zero Hour Installation",
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                },
            ],
        };
        ContentManifest? savedManifest = null;
        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(storedManifest));
        manifestPool
            .Setup(pool => pool.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .Callback<ContentManifest, CancellationToken>((manifest, _) => savedManifest = manifest)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool.Object,
            notifications.Object);
        viewModel.IsDownloaded = true;

        // Act
        viewModel.SelectedContentType = ContentType.Executable;
        await viewModel.AwaitContentTypePersistAsync();

        // Assert
        Assert.NotNull(savedManifest);
        Assert.Equal(ContentType.Executable, savedManifest.ContentType);
        Assert.Empty(savedManifest.Dependencies);
        Assert.False(viewModel.HasRequiredDependencies);
        Assert.Equal(ContentType.Executable, searchResult.ContentType);
        notifications.Verify(
            service => service.ShowSuccess(
                "Content Type Updated",
                It.Is<string>(message => message.Contains("Executable", StringComparison.Ordinal)),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies selecting a downloadable item updates selection state and dynamic sidebar properties.
    /// </summary>
    [Fact]
    public void SelectDownloadableItem_UpdatesSelectionStateAndSidebarProperties()
    {
        // Arrange
        var parent = new ContentSearchResult
        {
            Id = "moddb-parent-id",
            Name = "Parent Mod",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        var viewModel = CreateViewModel(parent, new Mock<IContentDownloadCoordinator>().Object);
        var releaseFile = new DownloadableFile(
            Name: "Release_v1.0.zip",
            Category: "Release",
            DownloadUrl: "https://www.moddb.com/downloads/start/101",
            FileSectionType: FileSectionType.Downloads,
            Version: "1.0",
            SizeBytes: 1048576);
        var addonFile = new DownloadableFile(
            Name: "Addon_SkinPack.zip",
            Category: "Texture Pack",
            DownloadUrl: "https://www.moddb.com/addons/start/201",
            FileSectionType: FileSectionType.Addons,
            Version: "2.0",
            SizeBytes: 2097152);

        viewModel.PopulateReleases([releaseFile]);
        viewModel.PopulateAddons([addonFile]);

        var release = viewModel.Releases[0];
        var addon = viewModel.Addons[0];

        // Act - Select the release item
        viewModel.SelectDownloadableItemCommand.Execute(release);

        // Assert
        Assert.True(viewModel.HasSelectedDownloadableItem);
        Assert.Same(release, viewModel.SelectedDownloadableItem);
        Assert.True(release.IsSelected);
        Assert.False(addon.IsSelected);
        Assert.Equal("Release_v1.0.zip", viewModel.SelectedTargetTitle);
        Assert.Equal("Release", viewModel.SelectedTargetCategory);
        Assert.Equal(1048576, viewModel.DownloadSize);
        Assert.Equal("1.0", viewModel.Version);

        // Act - Clear selection
        viewModel.ClearSelectedDownloadableItemCommand.Execute(null);

        // Assert after clear
        Assert.False(viewModel.HasSelectedDownloadableItem);
        Assert.Null(viewModel.SelectedDownloadableItem);
        Assert.False(release.IsSelected);
        Assert.False(addon.IsSelected);
        Assert.Equal("Parent Mod", viewModel.SelectedTargetTitle);
        Assert.Equal("Mods", viewModel.SelectedTargetCategory);
    }

    /// <summary>
    /// Verifies that main action Download and AddToProfile buttons route to the selected downloadable item.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MainActionCard_RoutesToSelectedDownloadableItemAsync()
    {
        // Arrange
        const string releaseManifestId = "1.20260102.moddb.mod.v104";
        var parent = new ContentSearchResult
        {
            Id = "moddb-parent-id",
            Name = "Generals Mod",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create(releaseManifestId),
            Name = "Release Patch v1.04",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        ContentSearchResult? downloadedContent = null;
        var coordinator = new Mock<IContentDownloadCoordinator>();
        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken, bool>(
                (content, _, _, _) => downloadedContent = content)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var viewModel = CreateViewModel(parent, coordinator.Object);
        var releaseFile = new DownloadableFile(
            Name: "Patch_v104.zip",
            Category: "Patch",
            DownloadUrl: "https://www.moddb.com/downloads/start/555",
            FileSectionType: FileSectionType.Downloads);
        viewModel.PopulateReleases([releaseFile]);
        var release = viewModel.Releases[0];

        // Act 1: Select the release item
        viewModel.SelectDownloadableItemCommand.Execute(release);

        // Act 2: Execute main Download command
        await viewModel.DownloadCommand.ExecuteAsync(null);

        // Assert 1: Download went through selected release file
        Assert.NotNull(downloadedContent);
        Assert.Equal(releaseFile.DownloadUrl, downloadedContent.SelectedDownloadUrl);
        Assert.True(release.IsDownloaded);
        Assert.Equal(releaseManifestId, release.DownloadedManifestId);

        // Act 3: Execute main AddToProfile command
        await viewModel.AddToProfileCommand.ExecuteAsync(null);

        // Assert 2: Added to profile using selected item manifest
        Assert.Equal(releaseManifestId, viewModel.ProfileManifestId);
        Assert.Equal(releaseFile.Name, viewModel.ProfileContentName);
    }

    /// <summary>
    /// Verifies that PopulateReleases prioritizes a full mod release over a patch file.
    /// </summary>
    [Fact]
    public void PopulateReleases_PrioritizesModReleaseOverPatch()
    {
        // Arrange
        var parent = new ContentSearchResult
        {
            Id = "moddb-parent-id",
            Name = "Parent Mod",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        var viewModel = CreateViewModel(parent, new Mock<IContentDownloadCoordinator>().Object);
        var releaseFile1 = new DownloadableFile(
            Name: "Generals Undone v1.01 Patch",
            Category: "Patch",
            DownloadUrl: "https://www.moddb.com/downloads/start/101",
            FileSectionType: FileSectionType.Downloads,
            UploadDate: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var releaseFile2 = new DownloadableFile(
            Name: "C&C Generals Undone",
            Category: "Full Version",
            DownloadUrl: "https://www.moddb.com/downloads/start/102",
            FileSectionType: FileSectionType.Downloads,
            UploadDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        viewModel.PopulateReleases([releaseFile1, releaseFile2]);

        // Assert - Releases[0] is the newer Patch, Releases[1] is the older Full Version
        Assert.True(viewModel.HasSelectedDownloadableItem);
        Assert.Equal(2, viewModel.Releases.Count);
        Assert.Same(viewModel.Releases[1], viewModel.SelectedDownloadableItem);
        Assert.False(viewModel.Releases[0].IsSelected);
        Assert.True(viewModel.Releases[1].IsSelected);
        Assert.True(viewModel.ShowSelectedTargetBanner);
    }

    /// <summary>
    /// Verifies that PopulateReleases falls back to selecting the first release when all available items are patches.
    /// </summary>
    [Fact]
    public void PopulateReleases_WhenOnlyPatchesExist_SelectsFirstPatch()
    {
        // Arrange
        var parent = new ContentSearchResult
        {
            Id = "moddb-parent-id",
            Name = "Parent Mod",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        var viewModel = CreateViewModel(parent, new Mock<IContentDownloadCoordinator>().Object);
        var patch1 = new DownloadableFile(
            Name: "Mod Patch v1.2",
            Category: "Patch",
            DownloadUrl: "https://www.moddb.com/downloads/start/102",
            FileSectionType: FileSectionType.Downloads,
            UploadDate: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var patch2 = new DownloadableFile(
            Name: "Mod Patch v1.1",
            Category: "Patch",
            DownloadUrl: "https://www.moddb.com/downloads/start/101",
            FileSectionType: FileSectionType.Downloads,
            UploadDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        viewModel.PopulateReleases([patch1, patch2]);

        // Assert
        Assert.True(viewModel.HasSelectedDownloadableItem);
        Assert.Same(viewModel.Releases[0], viewModel.SelectedDownloadableItem);
        Assert.True(viewModel.Releases[0].IsSelected);
        Assert.False(viewModel.Releases[1].IsSelected);
    }

    /// <summary>
    /// Verifies that TriggerPreloadRecentItemDetailsAsync fetches extended details and updates file sizes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TriggerPreloadRecentItemDetailsAsync_PopulatesRecentReleasesAndAddonsAsync()
    {
        // Arrange
        var parent = new ContentSearchResult
        {
            Id = "moddb-parent-id",
            Name = "Parent Mod",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        const string patchUrl = "https://www.moddb.com/mods/test/downloads/patch-101";
        var mockParser = new Mock<IWebPageParser>();
        mockParser.Setup(p => p.CanParse(patchUrl)).Returns(true);
        var parsedFile = new DownloadableFile(
            Name: "Generals Undone v1.01 Patch",
            Category: "Patch",
            DownloadUrl: "https://www.moddb.com/downloads/start/999",
            SizeBytes: 15728640,
            SizeDisplay: "15.0 MB",
            Filename: "patch_101.zip",
            Description: "Bug fixes and balance adjustments.",
            Md5Hash: "abcdef1234567890",
            FileSectionType: FileSectionType.Downloads);
        var parsedPage = new ParsedWebPage(
            Url: new Uri(patchUrl),
            Context: new GlobalContext("Generals Undone v1.01 Patch", "Developer", null),
            Sections: [parsedFile],
            PageType: PageType.FileDetail);
        mockParser.Setup(p => p.ParseFileDetailAsync(patchUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parsedPage);
        mockParser.Setup(p => p.ParseFileDetailsManyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ParsedWebPage>(StringComparer.OrdinalIgnoreCase) { [patchUrl] = parsedPage });

        var viewModel = CreateViewModel(parent, new Mock<IContentDownloadCoordinator>().Object, parsers: [mockParser.Object]);

        var shallowRelease = new DownloadableFile(
            Name: "Generals Undone v1.01 Patch",
            Category: "Patch",
            DownloadUrl: patchUrl,
            DetailsUrl: patchUrl,
            FileSectionType: FileSectionType.Downloads,
            UploadDate: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        viewModel.PopulateReleases([shallowRelease]);

        // Before preload: FormattedSize is empty because SizeBytes is 0/null
        Assert.Empty(viewModel.Releases[0].FormattedSize);
        Assert.False(viewModel.Releases[0].IsDetailsLoaded);

        // Act
        await viewModel.TriggerPreloadRecentItemDetailsAsync(CancellationToken.None);

        // Assert - details populated
        Assert.True(viewModel.Releases[0].IsDetailsLoaded);
        Assert.Equal("15.0 MB", viewModel.Releases[0].FormattedSize);
        Assert.Equal(15728640, viewModel.Releases[0].FileSize);
        Assert.Equal("patch_101.zip", viewModel.Releases[0].Filename);
        Assert.Equal("abcdef1234567890", viewModel.Releases[0].Md5Hash);
        Assert.Equal("Bug fixes and balance adjustments.", viewModel.Releases[0].FullDescription);
        Assert.Equal("https://www.moddb.com/downloads/start/999", viewModel.Releases[0].DownloadUrl);
    }

    /// <summary>
    /// Verifies that when releases and addons are already populated with detailed metadata (from ParseAsync enrichment),
    /// TriggerPreloadRecentItemDetailsAsync detects that details are loaded and does not call ParseFileDetailsManyAsync.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TriggerPreloadRecentItemDetailsAsync_WhenReleasesAndAddonsAlreadyDetailed_DoesNotCallParserAsync()
    {
        // Arrange
        const string releaseUrl = "https://www.moddb.com/mods/some-mod/downloads/release-1";
        const string addonUrl = "https://www.moddb.com/mods/some-mod/addons/addon-1";
        var parent = new ContentSearchResult
        {
            Id = "moddb-parent-id",
            Name = "Some Mod",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/some-mod",
        };

        var mockParser = new Mock<IWebPageParser>(MockBehavior.Strict);
        mockParser.Setup(p => p.CanParse(It.IsAny<string>())).Returns(true);

        var viewModel = CreateViewModel(parent, new Mock<IContentDownloadCoordinator>().Object, parsers: [mockParser.Object]);

        var detailedRelease = new DownloadableFile(
            Name: "Release 1",
            Category: "Full Version",
            DownloadUrl: "https://www.moddb.com/downloads/start/1001",
            DetailsUrl: releaseUrl,
            SizeBytes: 524288000,
            Filename: "release_1.zip",
            Md5Hash: "md5_release",
            Description: "Full release description",
            FileSectionType: FileSectionType.Downloads,
            UploadDate: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var detailedAddon = new DownloadableFile(
            Name: "Addon 1",
            Category: "Map",
            DownloadUrl: "https://www.moddb.com/addons/start/2001",
            DetailsUrl: addonUrl,
            SizeBytes: 10485760,
            Filename: "addon_1.zip",
            Md5Hash: "md5_addon",
            Description: "Addon map description",
            FileSectionType: FileSectionType.Addons,
            UploadDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        viewModel.PopulateReleases([detailedRelease]);
        viewModel.PopulateAddons([detailedAddon]);

        // Assert - both are marked as already loaded
        Assert.True(viewModel.Releases[0].IsDetailsLoaded);
        Assert.True(viewModel.Addons[0].IsDetailsLoaded);

        // Act - Trigger background preload
        await viewModel.TriggerPreloadRecentItemDetailsAsync(CancellationToken.None);

        // Assert - ParseFileDetailsManyAsync or ParseFileDetailAsync was never invoked because all items are loaded
        mockParser.Verify(p => p.ParseFileDetailsManyAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        mockParser.Verify(p => p.ParseFileDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that executing a row's DownloadCommand after its details have loaded uses the
    /// detailed file with the direct /start/ download URL.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RowDownloadCommand_UsesUpdatedDetailedFileAfterDetailsLoadedAsync()
    {
        // Arrange
        const string addonPageUrl = "https://www.moddb.com/mods/some-mod/addons/lost-warlord";
        const string directDownloadUrl = "https://www.moddb.com/addons/start/305556";
        var parent = new ContentSearchResult
        {
            Id = "moddb-parent-id",
            Name = "Some Mod",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/some-mod",
        };

        var mockParser = new Mock<IWebPageParser>();
        mockParser.Setup(p => p.CanParse(addonPageUrl)).Returns(true);
        var detailedFile = new DownloadableFile(
            Name: "Lost Warlord - by Lebi",
            Category: "Singleplayer Map",
            DownloadUrl: directDownloadUrl,
            SizeBytes: 268025,
            Filename: "Lost_Warlord.rar",
            FileSectionType: FileSectionType.Addons);
        var parsedPage = new ParsedWebPage(
            Url: new Uri(addonPageUrl),
            Context: new GlobalContext("Lost Warlord", "Lebi182", null),
            Sections: [detailedFile],
            PageType: PageType.FileDetail);
        mockParser.Setup(p => p.ParseFileDetailAsync(addonPageUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parsedPage);

        ContentSearchResult? downloadedResult = null;
        var coordinator = new Mock<IContentDownloadCoordinator>();
        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken, bool>(
                (content, _, _, _) => downloadedResult = content)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.20260308.moddb.map.lostwarlord"),
                Name = "Lost Warlord - by Lebi",
                ContentType = ContentType.Map,
            }));

        var viewModel = CreateViewModel(parent, coordinator.Object, parsers: [mockParser.Object]);

        var shallowAddon = new DownloadableFile(
            Name: "Lost Warlord - by Lebi",
            Category: "Singleplayer Map",
            DownloadUrl: addonPageUrl,
            DetailsUrl: addonPageUrl,
            FileSectionType: FileSectionType.Addons);

        viewModel.PopulateAddons([shallowAddon]);
        var addonItem = viewModel.Addons[0];

        // Act 1: Load details
        await addonItem.ToggleExpandCommand.ExecuteAsync(null);

        // Act 2: Execute download command on row
        addonItem.DownloadCommand!.Execute(null);

        // Assert: coordinator was invoked with the detailed file's direct download URL
        Assert.NotNull(downloadedResult);
        Assert.Equal(directDownloadUrl, downloadedResult.SelectedDownloadUrl);
    }

    /// <summary>
    /// Verifies that ShowSelectedTargetBanner is false for single-release items and true for multi-release items.
    /// </summary>
    [Fact]
    public void ShowSelectedTargetBanner_HiddenForSingleReleaseAndVisibleForMultiple()
    {
        // Arrange
        var parent = new ContentSearchResult
        {
            Id = "moddb-parent-id",
            Name = "Parent Mod",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        var viewModel = CreateViewModel(parent, new Mock<IContentDownloadCoordinator>().Object);
        var release1 = new DownloadableFile(
            Name: "Single_Release.zip",
            DownloadUrl: "https://www.moddb.com/downloads/start/101",
            FileSectionType: FileSectionType.Downloads);

        // Act 1: Populate single release
        viewModel.PopulateReleases([release1]);

        // Assert 1: Selected, but banner is hidden because there is only 1 choice
        Assert.True(viewModel.HasSelectedDownloadableItem);
        Assert.False(viewModel.ShowSelectedTargetBanner);

        // Act 2: Add a second release
        var release2 = new DownloadableFile(
            Name: "Second_Release.zip",
            DownloadUrl: "https://www.moddb.com/downloads/start/102",
            FileSectionType: FileSectionType.Downloads);
        viewModel.PopulateReleases([release1, release2]);

        // Assert 2: Banner becomes visible
        Assert.True(viewModel.ShowSelectedTargetBanner);
    }

    /// <summary>
    /// Verifies that changing the selected variant synchronizes the Releases list selection.
    /// </summary>
    [Fact]
    public void SelectedVariant_SynchronizesBidirectionallyWithReleases()
    {
        // Arrange
        var searchResult720 = new ContentSearchResult
        {
            Id = "outpost.cbp.720p",
            Name = "Control Bar Pro 720p",
            ProviderName = "CommunityOutpost",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            DownloadSize = 7549747,
        };
        var searchResult1080 = new ContentSearchResult
        {
            Id = "outpost.cbp.1080p",
            Name = "Control Bar Pro 1080p",
            ProviderName = "CommunityOutpost",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            DownloadSize = 10192158,
        };

        var variants = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["outpost.cbp.720p"] = searchResult720,
            ["outpost.cbp.1080p"] = searchResult1080,
        };

        var viewModel = CreateViewModel(
            searchResult1080,
            new Mock<IContentDownloadCoordinator>().Object,
            variantSearchResults: variants);

        var variant720 = new InstallableVariant { ManifestId = "outpost.cbp.720p", Name = "Control Bar Pro 720p" };
        var variant1080 = new InstallableVariant { ManifestId = "outpost.cbp.1080p", Name = "Control Bar Pro 1080p" };

        viewModel.Variants = [variant720, variant1080];
        viewModel.SelectedVariant = variant1080;
        viewModel.PopulateReleasesFromVariants();

        // Assert initial state
        Assert.Equal(2, viewModel.Releases.Count);
        Assert.True(viewModel.ShowSelectedTargetBanner);
        var release1080 = viewModel.Releases.First(r => r.DownloadedManifestId == "outpost.cbp.1080p");
        var release720 = viewModel.Releases.First(r => r.DownloadedManifestId == "outpost.cbp.720p");

        Assert.True(release1080.IsSelected);
        Assert.False(release720.IsSelected);
        Assert.Same(release1080, viewModel.SelectedDownloadableItem);

        // Act 1: Select 720p via row select command
        release720.SelectCommand?.Execute(null);

        // Assert 1: SelectedVariant updated to 720p
        Assert.Same(variant720, viewModel.SelectedVariant);
        Assert.True(release720.IsSelected);
        Assert.False(release1080.IsSelected);
        Assert.Same(release720, viewModel.SelectedDownloadableItem);

        // Act 2: Change SelectedVariant back to 1080p
        viewModel.SelectedVariant = variant1080;

        // Assert 2: Selected release row updated to 1080p
        Assert.True(release1080.IsSelected);
        Assert.False(release720.IsSelected);
        Assert.Same(release1080, viewModel.SelectedDownloadableItem);
    }

    /// <summary>
    /// Verifies that a GitHub card with attached release data hydrates the Releases tab from
    /// real release assets while the About tab keeps showing the card (repository) description.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Initialize_GitHubReleaseData_PopulatesReleasesFromAssetsAndKeepsAboutAsync()
    {
        // Arrange
        const string aboutText = "Short repo about text";
        const string releaseBody = "## Highlights\n- New units";
        var release = new GitHubRelease
        {
            TagName = "v2.0.0",
            Name = "Big Update",
            Body = releaseBody,
            Author = "modauthor",
            HtmlUrl = "https://github.com/modauthor/coolmod/releases/tag/v2.0.0",
            PublishedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero),
            Assets =
            [
                new GitHubReleaseAsset { Name = "coolmod.zip", Size = 1024, BrowserDownloadUrl = "https://github.com/modauthor/coolmod/releases/download/v2.0.0/coolmod.zip" },
                new GitHubReleaseAsset { Name = "coolmod-maps.zip", Size = 2048, BrowserDownloadUrl = "https://github.com/modauthor/coolmod/releases/download/v2.0.0/coolmod-maps.zip" },
            ],
        };

        var searchResult = new ContentSearchResult
        {
            Id = "github.modauthor.coolmod.v2.0.0",
            Name = "coolmod v2.0.0",
            Description = aboutText,
            Version = "2.0.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ProviderName = "github-topics",
            AuthorName = "modauthor",
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = release.HtmlUrl,
            LastUpdated = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        };
        searchResult.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "modauthor";
        searchResult.ResolverMetadata[GitHubConstants.RepoMetadataKey] = "coolmod";
        searchResult.ResolverMetadata[GitHubConstants.TagMetadataKey] = "v2.0.0";
        searchResult.SetData(release);

        var viewModel = CreateViewModel(searchResult, new Mock<IContentDownloadCoordinator>().Object);

        // Act
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert: Releases tab lists one row per asset with direct download URLs and raw changelogs.
        Assert.Equal(2, viewModel.Releases.Count);
        Assert.True(viewModel.HasReleases);
        Assert.Contains(viewModel.Releases, r =>
            r.DownloadUrl == "https://github.com/modauthor/coolmod/releases/download/v2.0.0/coolmod.zip" && r.FileSize == 1024);
        Assert.Contains(viewModel.Releases, r =>
            r.DownloadUrl == "https://github.com/modauthor/coolmod/releases/download/v2.0.0/coolmod-maps.zip" && r.FileSize == 2048);
        Assert.All(viewModel.Releases, r => Assert.Equal(releaseBody, r.FullDescription));
        Assert.All(viewModel.Releases, r => Assert.Equal(release.HtmlUrl, r.DetailsUrl));

        // Assert: About tab still shows the repository description, untouched by release notes.
        Assert.Equal(aboutText, searchResult.Description);
        Assert.Equal(aboutText, viewModel.Description);
    }

    /// <summary>
    /// Verifies that an asset-pinned card hydrates a single release row for that asset only.
    /// </summary>
    [Fact]
    public void PopulateGitHubReleases_WithAssetNameMetadata_SelectsPinnedAssetOnly()
    {
        // Arrange
        var release = new GitHubRelease
        {
            TagName = "weekly-2026-01-01",
            Body = "Weekly game code update",
            Author = "TheSuperHackers",
            HtmlUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/tag/weekly-2026-01-01",
            PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Assets =
            [
                new GitHubReleaseAsset { Name = "Generals-Weekly.zip", Size = 1000, BrowserDownloadUrl = "https://example.test/Generals-Weekly.zip" },
                new GitHubReleaseAsset { Name = "GeneralsZH-Weekly.zip", Size = 2000, BrowserDownloadUrl = "https://example.test/GeneralsZH-Weekly.zip" },
            ],
        };

        var searchResult = new ContentSearchResult
        {
            Id = "github.thesuperhackers.generalsgamecode.weekly-2026-01-01.zerohour",
            Name = "GeneralsGameCode weekly-2026-01-01 — Zero Hour",
            Description = "Weekly game code update",
            Version = "weekly-2026-01-01",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            ProviderName = "thesuperhackers",
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = release.HtmlUrl,
        };
        searchResult.ResolverMetadata[GitHubConstants.AssetNameMetadataKey] = "GeneralsZH-Weekly.zip";
        searchResult.SetData(release);

        var viewModel = CreateViewModel(searchResult, new Mock<IContentDownloadCoordinator>().Object);

        // Act
        var populated = viewModel.PopulateGitHubReleases(searchResult);

        // Assert
        Assert.True(populated);
        var row = Assert.Single(viewModel.Releases);
        Assert.Equal("https://example.test/GeneralsZH-Weekly.zip", row.DownloadUrl);
        Assert.Equal(2000, row.FileSize);
        Assert.Equal("Weekly game code update", row.FullDescription);
    }

    /// <summary>
    /// Verifies that a single-asset artifact payload hydrates one release row with the direct URL.
    /// </summary>
    [Fact]
    public void PopulateGitHubReleases_ArtifactData_PopulatesSingleRelease()
    {
        // Arrange
        var artifact = new GitHubArtifact
        {
            Name = "coolmod-1080p.zip",
            DownloadUrl = "https://github.com/modauthor/coolmod/releases/download/v1.0/coolmod-1080p.zip",
            SizeInBytes = 4096,
            IsRelease = true,
        };

        var searchResult = new ContentSearchResult
        {
            Id = "github.modauthor.coolmod.v1.0",
            Name = "coolmod (1080p)",
            Description = "Short repo about text",
            Version = "v1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ProviderName = "github-topics",
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = "https://github.com/modauthor/coolmod",
        };
        searchResult.SetData(artifact);

        var viewModel = CreateViewModel(searchResult, new Mock<IContentDownloadCoordinator>().Object);

        // Act
        var populated = viewModel.PopulateGitHubReleases(searchResult);

        // Assert
        Assert.True(populated);
        var row = Assert.Single(viewModel.Releases);
        Assert.Equal(artifact.DownloadUrl, row.DownloadUrl);
        Assert.Equal(4096, row.FileSize);
        Assert.Equal("Short repo about text", row.FullDescription);
    }

    /// <summary>
    /// Verifies that cards without GitHub payloads decline hydration so the legacy
    /// source-URL fallback still applies.
    /// </summary>
    [Fact]
    public void PopulateGitHubReleases_WithoutGitHubData_ReturnsFalse()
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = "plain-card",
            Name = "Plain Card",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var viewModel = CreateViewModel(searchResult, new Mock<IContentDownloadCoordinator>().Object);

        // Act
        var populated = viewModel.PopulateGitHubReleases(searchResult);

        // Assert
        Assert.False(populated);
        Assert.Empty(viewModel.Releases);
    }

    /// <summary>
    /// Verifies that a release without assets declines hydration so the legacy single-file
    /// fallback keeps covering repositories with no downloadable assets.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Initialize_GitHubReleaseWithoutAssets_FallsBackToSourceUrlAsync()
    {
        // Arrange
        var release = new GitHubRelease
        {
            TagName = "v1.0",
            Name = "Notes only",
            Body = "No assets attached",
            HtmlUrl = "https://github.com/modauthor/coolmod/releases/tag/v1.0",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Assets = [],
        };

        var searchResult = new ContentSearchResult
        {
            Id = "github.modauthor.coolmod.v1.0",
            Name = "coolmod v1.0",
            Description = "Short repo about text",
            Version = "v1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ProviderName = "github-topics",
            AuthorName = "modauthor",
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = release.HtmlUrl,
            LastUpdated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        searchResult.SetData(release);

        var viewModel = CreateViewModel(searchResult, new Mock<IContentDownloadCoordinator>().Object);

        // Act
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert
        var row = Assert.Single(viewModel.Releases);
        Assert.Equal(searchResult.SourceUrl, row.DownloadUrl);
    }

    /// <summary>
    /// Verifies that variant siblings sharing a release resolve their pinned direct asset URLs
    /// instead of the release page URL.
    /// </summary>
    [Fact]
    public void PopulateReleasesFromVariants_GitHubSiblings_ResolvesDirectAssetUrls()
    {
        // Arrange
        const string releaseBody = "# Weekly\n- fixes";
        const string releaseUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/tag/weekly-2026-01-01";
        var release = new GitHubRelease
        {
            TagName = "weekly-2026-01-01",
            Name = "weekly-2026-01-01",
            Body = releaseBody,
            Author = "TheSuperHackers",
            HtmlUrl = releaseUrl,
            PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Assets =
            [
                new GitHubReleaseAsset { Name = "Generals-Weekly.zip", Size = 1000, BrowserDownloadUrl = "https://example.test/Generals-Weekly.zip" },
                new GitHubReleaseAsset { Name = "GeneralsZH-Weekly.zip", Size = 2000, BrowserDownloadUrl = "https://example.test/GeneralsZH-Weekly.zip" },
            ],
        };

        ContentSearchResult CreateSibling(string suffix, string assetName, GameType gameType)
        {
            var sibling = new ContentSearchResult
            {
                Id = $"github.thesuperhackers.generalsgamecode.weekly-2026-01-01.{suffix}",
                Name = $"GeneralsGameCode weekly-2026-01-01 — {suffix}",
                Description = "Weekly game code update",
                Version = "weekly-2026-01-01",
                ContentType = ContentType.GameClient,
                TargetGame = gameType,
                ProviderName = "thesuperhackers",
                RequiresResolution = true,
                ResolverId = ContentSourceNames.GitHubResolverId,
                SourceUrl = releaseUrl,
            };
            sibling.ResolverMetadata[GitHubConstants.AssetNameMetadataKey] = assetName;
            sibling.SetData(release);
            return sibling;
        }

        var genSibling = CreateSibling("generals", "Generals-Weekly.zip", GameType.Generals);
        var zhSibling = CreateSibling("zerohour", "GeneralsZH-Weekly.zip", GameType.ZeroHour);

        var variants = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            [genSibling.Id] = genSibling,
            [zhSibling.Id] = zhSibling,
        };

        var viewModel = CreateViewModel(
            zhSibling,
            new Mock<IContentDownloadCoordinator>().Object,
            variantSearchResults: variants);

        viewModel.Variants =
        [
            new InstallableVariant { ManifestId = genSibling.Id, Name = genSibling.Name },
            new InstallableVariant { ManifestId = zhSibling.Id, Name = zhSibling.Name },
        ];

        // Act
        viewModel.PopulateReleasesFromVariants();

        // Assert
        Assert.Equal(2, viewModel.Releases.Count);
        var genRow = viewModel.Releases.First(r => r.DownloadedManifestId == genSibling.Id);
        var zhRow = viewModel.Releases.First(r => r.DownloadedManifestId == zhSibling.Id);
        Assert.Equal("https://example.test/Generals-Weekly.zip", genRow.DownloadUrl);
        Assert.Equal(1000, genRow.FileSize);
        Assert.Equal("https://example.test/GeneralsZH-Weekly.zip", zhRow.DownloadUrl);
        Assert.Equal(2000, zhRow.FileSize);
        Assert.Equal(releaseBody, genRow.FullDescription);
        Assert.Equal(releaseBody, zhRow.FullDescription);
    }

    /// <summary>
    /// Verifies that opening a GitHub card loads the repository README into the summary section.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Initialize_GitHubCard_LoadsReadmeIntoSummaryAsync()
    {
        // Arrange
        const string readme = "# coolmod\nA great mod.";
        var gitHubMock = new Mock<IGitHubApiClient>();
        gitHubMock
            .Setup(c => c.GetReadmeAsync("modauthor", "coolmod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(readme);

        var searchResult = new ContentSearchResult
        {
            Id = "github.modauthor.coolmod.v2.0.0",
            Name = "coolmod v2.0.0",
            Description = "Short repo about text",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ProviderName = "github-topics",
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = "https://github.com/modauthor/coolmod",
        };
        searchResult.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "modauthor";
        searchResult.ResolverMetadata[GitHubConstants.RepoMetadataKey] = "coolmod";

        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            gitHubApiClient: gitHubMock.Object);

        // Act
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert
        Assert.True(viewModel.HasReadme);
        Assert.Equal(readme, viewModel.ReadmeMarkdown);
        Assert.Contains("coolmod", viewModel.FormattedReadme);
    }

    /// <summary>
    /// Verifies that cards without GitHub repository metadata skip the README fetch.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Initialize_CardWithoutRepositoryMetadata_SkipsReadmeFetchAsync()
    {
        // Arrange
        var gitHubMock = new Mock<IGitHubApiClient>();
        var searchResult = new ContentSearchResult
        {
            Id = "plain-card",
            Name = "Plain Card",
            Description = "No repository metadata",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://example.test/plain",
        };

        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            gitHubApiClient: gitHubMock.Object);

        // Act
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert
        Assert.False(viewModel.HasReadme);
        Assert.Null(viewModel.ReadmeMarkdown);
        gitHubMock.Verify(
            c => c.GetReadmeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that release rows without changelogs hydrate their notes from the GitHub API
    /// with a single grouped request per release.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Initialize_GitHubReleaseWithoutBody_HydratesNotesFromApiAsync()
    {
        // Arrange
        const string releaseBody = "## Highlights\n- New units";
        var attachedRelease = new GitHubRelease
        {
            TagName = "v2.0.0",
            Name = "Big Update",
            Body = string.Empty,
            Author = "modauthor",
            HtmlUrl = "https://github.com/modauthor/coolmod/releases/tag/v2.0.0",
            PublishedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero),
            Assets =
            [
                new GitHubReleaseAsset { Name = "coolmod.zip", Size = 1024, BrowserDownloadUrl = "https://github.com/modauthor/coolmod/releases/download/v2.0.0/coolmod.zip" },
                new GitHubReleaseAsset { Name = "coolmod-maps.zip", Size = 2048, BrowserDownloadUrl = "https://github.com/modauthor/coolmod/releases/download/v2.0.0/coolmod-maps.zip" },
            ],
        };

        var searchResult = new ContentSearchResult
        {
            Id = "github.modauthor.coolmod.v2.0.0",
            Name = "coolmod v2.0.0",
            Description = "Short repo about text",
            Version = "2.0.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ProviderName = "github-topics",
            AuthorName = "modauthor",
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = attachedRelease.HtmlUrl,
            LastUpdated = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        };
        searchResult.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "modauthor";
        searchResult.ResolverMetadata[GitHubConstants.RepoMetadataKey] = "coolmod";
        searchResult.ResolverMetadata[GitHubConstants.TagMetadataKey] = "v2.0.0";
        searchResult.SetData(attachedRelease);

        var gitHubMock = new Mock<IGitHubApiClient>();
        gitHubMock
            .Setup(c => c.GetReleaseByTagAsync("modauthor", "coolmod", "v2.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubRelease { TagName = "v2.0.0", Body = releaseBody });

        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            gitHubApiClient: gitHubMock.Object);

        // Act
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert
        Assert.Equal(2, viewModel.Releases.Count);
        Assert.All(viewModel.Releases, r => Assert.Equal(releaseBody, r.FullDescription));
        Assert.All(viewModel.Releases, r => Assert.Equal(releaseBody, r.File!.Description));
        gitHubMock.Verify(
            c => c.GetReleaseByTagAsync("modauthor", "coolmod", "v2.0.0", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that variant rows seeded with the repository description hydrate the actual
    /// release notes from the GitHub API with a single grouped request per tag.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PopulateReleasesFromVariants_PlaceholderDescription_HydratesNotesFromApiAsync()
    {
        // Arrange
        const string repoDescription = "Short repo about text";
        const string releaseBody = "## Highlights\n- New units";
        var viewModel = CreateGitHubVariantViewModel(repoDescription, "v2.0.0", "v2.0.0", out var gitHubMock);
        var releaseRequested = new TaskCompletionSource<GitHubRelease>(TaskCreationOptions.RunContinuationsAsynchronously);
        gitHubMock
            .Setup(c => c.GetReleaseByTagAsync("modauthor", "coolmod", "v2.0.0", It.IsAny<CancellationToken>()))
            .Returns(releaseRequested.Task);

        // Act
        viewModel.PopulateReleasesFromVariants();
        Assert.All(viewModel.Releases, r => Assert.Equal(repoDescription, r.FullDescription));
        releaseRequested.SetResult(new GitHubRelease { TagName = "v2.0.0", Body = releaseBody });
        await viewModel.WaitForInitializationAsync();

        // Assert
        Assert.Equal(2, viewModel.Releases.Count);
        Assert.All(viewModel.Releases, r => Assert.Equal(releaseBody, r.FullDescription));
        Assert.All(viewModel.Releases, r => Assert.Equal(releaseBody, r.File!.Description));
        gitHubMock.Verify(
            c => c.GetReleaseByTagAsync("modauthor", "coolmod", "v2.0.0", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that variant rows already carrying release notes never trigger an API fetch.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PopulateReleasesFromVariants_AttachedReleaseBody_SkipsNotesFetchAsync()
    {
        // Arrange
        const string releaseBody = "# Weekly\n- fixes";
        var release = new GitHubRelease
        {
            TagName = "weekly-2026-01-01",
            Name = "weekly-2026-01-01",
            Body = releaseBody,
            Assets =
            [
                new GitHubReleaseAsset { Name = "Generals-Weekly.zip", Size = 1000, BrowserDownloadUrl = "https://example.test/Generals-Weekly.zip" },
            ],
        };

        var sibling = new ContentSearchResult
        {
            Id = "github.owner.repo.weekly.generals",
            Name = "repo (generals)",
            Description = "Short repo about text",
            Version = "weekly-2026-01-01",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            ProviderName = "github-topics",
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = "https://github.com/owner/repo",
        };
        sibling.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "owner";
        sibling.ResolverMetadata[GitHubConstants.RepoMetadataKey] = "repo";
        sibling.ResolverMetadata[GitHubConstants.TagMetadataKey] = "weekly-2026-01-01";
        sibling.SetData(release);

        var gitHubMock = new Mock<IGitHubApiClient>();
        var viewModel = CreateViewModel(
            sibling,
            new Mock<IContentDownloadCoordinator>().Object,
            variantSearchResults: new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
            {
                [sibling.Id] = sibling,
            },
            gitHubApiClient: gitHubMock.Object);
        viewModel.Variants = [new InstallableVariant { ManifestId = sibling.Id, Name = sibling.Name }];

        // Act
        viewModel.PopulateReleasesFromVariants();
        await viewModel.WaitForInitializationAsync();

        // Assert
        var row = Assert.Single(viewModel.Releases);
        Assert.Equal(releaseBody, row.FullDescription);
        gitHubMock.Verify(
            c => c.GetReleaseByTagAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that failed release-notes fetches are not cached, so a later pass retries them.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PopulateReleasesFromVariants_FailedFetch_RetriesOnNextPassAsync()
    {
        // Arrange
        const string repoDescription = "Short repo about text";
        var viewModel = CreateGitHubVariantViewModel(repoDescription, "v2.0.0", "v2.0.0", out var gitHubMock);
        var firstFetch = new TaskCompletionSource<GitHubRelease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFetch = new TaskCompletionSource<GitHubRelease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        gitHubMock
            .Setup(c => c.GetReleaseByTagAsync("modauthor", "coolmod", "v2.0.0", It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                return calls == 1 ? firstFetch.Task : secondFetch.Task;
            });

        // Act: a single variant per round keeps each drain to exactly one fetch.
        viewModel.Variants = [viewModel.Variants[0]];
        viewModel.PopulateReleasesFromVariants();
        firstFetch.SetResult(null!);
        await viewModel.WaitForInitializationAsync();
        viewModel.PopulateReleasesFromVariants();
        secondFetch.SetResult(null!);
        await viewModel.WaitForInitializationAsync();

        // Assert
        Assert.All(viewModel.Releases, r => Assert.Equal(repoDescription, r.FullDescription));
        gitHubMock.Verify(
            c => c.GetReleaseByTagAsync("modauthor", "coolmod", "v2.0.0", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Verifies that a failing notes group does not discard the remaining pending groups.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PopulateReleasesFromVariants_SingleGroupFailure_KeepsOtherGroupsAsync()
    {
        // Arrange
        const string repoDescription = "Short repo about text";
        const string releaseBody = "## Highlights\n- New units";
        var viewModel = CreateGitHubVariantViewModel(repoDescription, "v1.0.0", "v2.0.0", out var gitHubMock);
        gitHubMock
            .Setup(c => c.GetReleaseByTagAsync("modauthor", "coolmod", "v1.0.0", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Transient network failure"));
        gitHubMock
            .Setup(c => c.GetReleaseByTagAsync("modauthor", "coolmod", "v2.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubRelease { TagName = "v2.0.0", Body = releaseBody });

        // Act
        viewModel.PopulateReleasesFromVariants();
        await viewModel.WaitForInitializationAsync();

        // Assert
        Assert.Equal(2, viewModel.Releases.Count);
        var failedRow = viewModel.Releases.First(r => r.DownloadedManifestId!.EndsWith(".v1", StringComparison.Ordinal));
        var hydratedRow = viewModel.Releases.First(r => r.DownloadedManifestId!.EndsWith(".v2", StringComparison.Ordinal));
        Assert.Equal(repoDescription, failedRow.FullDescription);
        Assert.Equal(releaseBody, hydratedRow.FullDescription);
    }

    /// <summary>
    /// Verifies that downloading a row of an unpinned multi-asset GitHub card pins the
    /// clicked asset so acquisition downloads only that file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReleaseRowDownload_GitHubMultiAsset_StampsClickedAssetPinAsync()
    {
        // Arrange
        var release = new GitHubRelease
        {
            TagName = "v2.0.0",
            Name = "Big Update",
            Body = "## Highlights\n- New units",
            HtmlUrl = "https://github.com/modauthor/coolmod/releases/tag/v2.0.0",
            PublishedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero),
            Assets =
            [
                new GitHubReleaseAsset { Name = "coolmod.zip", Size = 1024, BrowserDownloadUrl = "https://github.com/modauthor/coolmod/releases/download/v2.0.0/coolmod.zip" },
                new GitHubReleaseAsset { Name = "coolmod-maps.zip", Size = 2048, BrowserDownloadUrl = "https://github.com/modauthor/coolmod/releases/download/v2.0.0/coolmod-maps.zip" },
            ],
        };

        var parent = new ContentSearchResult
        {
            Id = "github.modauthor.coolmod.v2.0.0",
            Name = "coolmod v2.0.0",
            Description = "Short repo about text",
            Version = "2.0.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ProviderName = "github-topics",
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = release.HtmlUrl,
        };
        parent.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "modauthor";
        parent.ResolverMetadata[GitHubConstants.RepoMetadataKey] = "coolmod";
        parent.ResolverMetadata[GitHubConstants.TagMetadataKey] = "v2.0.0";
        parent.SetData(release);

        ContentSearchResult? coordinatorInput = null;
        var coordinator = new Mock<IContentDownloadCoordinator>();
        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken, bool>(
                (content, _, _, _) => coordinatorInput = content)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.20260115.github.mod.coolmodmaps"),
                Name = "coolmod-maps",
                ContentType = ContentType.Mod,
            }));

        var viewModel = CreateViewModel(parent, coordinator.Object);
        Assert.True(viewModel.PopulateGitHubReleases(parent));
        var mapsRow = viewModel.Releases.First(r => r.DownloadUrl == "https://github.com/modauthor/coolmod/releases/download/v2.0.0/coolmod-maps.zip");

        // Act
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(mapsRow.DownloadCommand).ExecuteAsync(null);

        // Assert
        Assert.NotNull(coordinatorInput);
        Assert.True(coordinatorInput.ResolverMetadata.TryGetValue(GitHubConstants.AssetNameMetadataKey, out var pin));
        Assert.Equal("coolmod-maps.zip", pin);
    }

    /// <summary>
    /// Verifies that downloading the legacy fallback row of an asset-less GitHub release
    /// carries no asset pin, so resolution keeps using the source-URL fallback.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReleaseRowDownload_GitHubReleaseWithoutAssets_FallbackRowCarriesNoPinAsync()
    {
        // Arrange
        var release = new GitHubRelease
        {
            TagName = "v1.0",
            Name = "Notes only",
            Body = "No assets attached",
            HtmlUrl = "https://github.com/modauthor/coolmod/releases/tag/v1.0",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Assets = [],
        };

        var parent = new ContentSearchResult
        {
            Id = "github.modauthor.coolmod.v1.0",
            Name = "coolmod v1.0",
            Description = "Short repo about text",
            Version = "v1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ProviderName = "github-topics",
            AuthorName = "modauthor",
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = release.HtmlUrl,
            LastUpdated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        parent.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "modauthor";
        parent.ResolverMetadata[GitHubConstants.RepoMetadataKey] = "coolmod";
        parent.ResolverMetadata[GitHubConstants.TagMetadataKey] = "v1.0";
        parent.SetData(release);

        ContentSearchResult? coordinatorInput = null;
        var coordinator = new Mock<IContentDownloadCoordinator>();
        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken, bool>(
                (content, _, _, _) => coordinatorInput = content)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.20260101.github.mod.coolmod"),
                Name = "coolmod",
                ContentType = ContentType.Mod,
            }));

        var viewModel = CreateViewModel(parent, coordinator.Object);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        var row = Assert.Single(viewModel.Releases);

        // Act
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(row.DownloadCommand).ExecuteAsync(null);

        // Assert
        Assert.NotNull(coordinatorInput);
        Assert.False(coordinatorInput.ResolverMetadata.ContainsKey(GitHubConstants.AssetNameMetadataKey));
        Assert.Equal(parent.SourceUrl, coordinatorInput.SelectedDownloadUrl);
    }

    /// <summary>
    /// Verifies that downloading a row of an asset-pinned GitHub card keeps the existing pin.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReleaseRowDownload_GitHubPinnedCard_KeepsExistingPinAsync()
    {
        // Arrange
        const string pinnedAsset = "GeneralsZH-Weekly.zip";
        var release = new GitHubRelease
        {
            TagName = "weekly-2026-01-01",
            Body = "Weekly game code update",
            HtmlUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/tag/weekly-2026-01-01",
            PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Assets =
            [
                new GitHubReleaseAsset { Name = "Generals-Weekly.zip", Size = 1000, BrowserDownloadUrl = "https://example.test/Generals-Weekly.zip" },
                new GitHubReleaseAsset { Name = pinnedAsset, Size = 2000, BrowserDownloadUrl = "https://example.test/GeneralsZH-Weekly.zip" },
            ],
        };

        var parent = new ContentSearchResult
        {
            Id = "github.thesuperhackers.generalsgamecode.weekly-2026-01-01.zerohour",
            Name = "GeneralsGameCode weekly-2026-01-01 — Zero Hour",
            Description = "Weekly game code update",
            Version = "weekly-2026-01-01",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            ProviderName = "thesuperhackers",
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = release.HtmlUrl,
        };
        parent.ResolverMetadata[GitHubConstants.AssetNameMetadataKey] = pinnedAsset;
        parent.SetData(release);

        ContentSearchResult? coordinatorInput = null;
        var coordinator = new Mock<IContentDownloadCoordinator>();
        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken, bool>(
                (content, _, _, _) => coordinatorInput = content)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.20260101.thesuperhackers.gameclient.zerohour"),
                Name = "GeneralsGameCode Zero Hour",
                ContentType = ContentType.GameClient,
            }));

        var viewModel = CreateViewModel(parent, coordinator.Object);
        Assert.True(viewModel.PopulateGitHubReleases(parent));
        var row = Assert.Single(viewModel.Releases);

        // Act
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(row.DownloadCommand).ExecuteAsync(null);

        // Assert
        Assert.NotNull(coordinatorInput);
        Assert.True(coordinatorInput.ResolverMetadata.TryGetValue(GitHubConstants.AssetNameMetadataKey, out var pin));
        Assert.Equal(pinnedAsset, pin);
    }

    /// <summary>
    /// Verifies that a ContentBundle with catalog releases automatically selects the preferred release on load,
    /// routes row download commands to bundle component acquisition, and syncs download state.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ContentBundle_ReleasesTab_AutoSelectsRelease_AndExecutesBundleDownloadAsync()
    {
        // Arrange
        const string bundleId = "bundle-test";
        var bundleResult = new ContentSearchResult
        {
            Id = bundleId,
            Name = "Test Bundle",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            Version = "2026.07.31",
            ResolverId = "generic-catalog",
        };

        var componentTarget = new ContentSearchResult
        {
            Id = "child-component-id",
            Name = "Child Component",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        var component = new BundleComponentViewModel
        {
            CatalogContentId = "child-component-id",
            Name = "Child Component",
            ContentTypeDisplay = "Addon",
            IsOptional = false,
            IsBaseGame = false,
        };
        component.AddVariant(
            new InstallableVariant { ManifestId = "child-component-id", Name = "Default Variant" },
            componentTarget);
        component.SelectedVariant = component.Variants[0];

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.child.addon.test"),
            Name = "Child Component",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };
        var isDownloaded = false;
        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(() =>
            {
                isDownloaded = true;
                return OperationResult<ContentManifest>.CreateSuccess(manifest);
            });

        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchResult _, CancellationToken _) =>
                isDownloaded ? ContentState.Downloaded : ContentState.NotDownloaded);
        stateService
            .Setup(s => s.GetStateByManifestIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CancellationToken _) =>
                isDownloaded ? ContentState.Downloaded : ContentState.NotDownloaded);

        var viewModel = CreateViewModel(bundleResult, coordinator.Object, contentStateService: stateService.Object);
        viewModel.AttachBundleComponents([component]);

        var catalogItem = new CatalogContentItem
        {
            Id = bundleId,
            Name = "Test Bundle",
            ContentType = ContentType.ContentBundle,
            Releases =
            [
                new ContentRelease
                {
                    Version = "2026.07.31",
                    ReleaseDate = DateTime.UtcNow,
                    Changelog = "Initial release",
                    Artifacts = [],
                },
            ],
        };

        bundleResult.ResolverMetadata[CatalogConstants.CatalogItemJsonMetadataKey] =
            System.Text.Json.JsonSerializer.Serialize(catalogItem);

        // Act
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert: Release is populated, preferred release is selected, and download state matches bundle readiness
        var release = Assert.Single(viewModel.Releases);
        Assert.True(release.IsSelected);
        Assert.Same(release, viewModel.SelectedDownloadableItem);
        Assert.False(release.IsDownloaded);
        Assert.False(viewModel.AreBundleComponentsReadyForProfile);

        // Act: Execute the release download command
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(release.DownloadCommand).ExecuteAsync(null);

        // Assert: Bundle download coordinator was invoked, component and release row are marked downloaded
        coordinator.Verify(
            c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Once);

        Assert.True(viewModel.AreBundleComponentsReadyForProfile);
        Assert.True(release.IsDownloaded);
        Assert.True(viewModel.IsDownloaded);
    }

    /// <summary>
    /// Verifies opening full-screen media with an Image sets media URL, title, and opens modal.
    /// </summary>
    [Fact]
    public void OpenFullScreenMedia_WithImage_SetsFullScreenMediaPropertiesAndOpensModal()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-item", Name = "Test" };
        var viewModel = CreateViewModel(item, coordinator.Object);
        var image = new Image("Test Screenshot", "https://example.com/thumb.jpg", "https://example.com/full.jpg");

        // Act
        viewModel.OpenFullScreenMediaCommand.Execute(image);

        // Assert
        Assert.True(viewModel.IsFullScreenMediaOpen);
        Assert.Equal("https://example.com/full.jpg", viewModel.FullScreenMediaUrl);
        Assert.Equal("Test Screenshot", viewModel.FullScreenMediaTitle);
    }

    /// <summary>
    /// Verifies opening full-screen media with a Video without embed URL falls back to thumbnail.
    /// </summary>
    [Fact]
    public void OpenFullScreenMedia_WithVideoWithoutEmbedUrl_SetsThumbnailAndOpensModal()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-item", Name = "Test" };
        var viewModel = CreateViewModel(item, coordinator.Object);
        var video = new Video("Test Video", "https://example.com/vid-thumb.jpg");

        // Act
        viewModel.OpenFullScreenMediaCommand.Execute(video);

        // Assert
        Assert.True(viewModel.IsFullScreenMediaOpen);
        Assert.Equal("https://example.com/vid-thumb.jpg", viewModel.FullScreenMediaUrl);
        Assert.Equal("Test Video", viewModel.FullScreenMediaTitle);
    }

    /// <summary>
    /// Verifies opening full-screen media with a string URL sets URL and opens modal.
    /// </summary>
    [Fact]
    public void OpenFullScreenMedia_WithStringUrl_SetsUrlAndOpensModal()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-item", Name = "Test" };
        var viewModel = CreateViewModel(item, coordinator.Object);
        const string screenshotUrl = "https://example.com/screenshot.png";

        // Act
        viewModel.OpenFullScreenMediaCommand.Execute(screenshotUrl);

        // Assert
        Assert.True(viewModel.IsFullScreenMediaOpen);
        Assert.Equal(screenshotUrl, viewModel.FullScreenMediaUrl);
        Assert.Equal("Image Preview", viewModel.FullScreenMediaTitle);
    }

    /// <summary>
    /// Verifies closing full-screen media resets properties and closes modal.
    /// </summary>
    [Fact]
    public void CloseFullScreenMedia_ResetsFullScreenMediaPropertiesAndClosesModal()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-item", Name = "Test" };
        var viewModel = CreateViewModel(item, coordinator.Object);
        viewModel.OpenFullScreenMediaCommand.Execute("https://example.com/pic.jpg");
        Assert.True(viewModel.IsFullScreenMediaOpen);

        // Act
        viewModel.CloseFullScreenMediaCommand.Execute(null);

        // Assert
        Assert.False(viewModel.IsFullScreenMediaOpen);
        Assert.Null(viewModel.FullScreenMediaUrl);
        Assert.Null(viewModel.FullScreenMediaTitle);
    }

    /// <summary>
    /// Verifies that when a release item with an available update is selected, ShowUpdateButton is true,
    /// and ShowDownloadButton is false.
    /// </summary>
    [Fact]
    public void SelectedDownloadableItem_WithUpdateAvailable_ShowsUpdateButtonAndHidesDownloadButton()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-item", Name = "Test" };
        var viewModel = CreateViewModel(item, coordinator.Object);

        var release = new ReleaseItemViewModel
        {
            Id = "rel-1",
            Name = "Release 1.0",
            IsDownloaded = true,
            IsUpdateAvailable = true,
            DownloadedManifestId = "1.100.pub.mod.test",
        };
        viewModel.Releases.Add(release);

        // Act
        viewModel.SelectDownloadableItemCommand.Execute(release);

        // Assert
        Assert.Same(release, viewModel.SelectedDownloadableItem);
        Assert.True(viewModel.ShowUpdateButton);
        Assert.True(viewModel.ShowAddToProfileButton);
        Assert.False(viewModel.ShowDownloadButton);
    }

    /// <summary>
    /// Verifies that when a downloaded release item without an update is selected, ShowAddToProfileButton is true,
    /// and ShowDownloadButton and ShowUpdateButton are false.
    /// </summary>
    [Fact]
    public void SelectedDownloadableItem_Downloaded_ShowsAddToProfileButton()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-item", Name = "Test" };
        var viewModel = CreateViewModel(item, coordinator.Object);

        var release = new ReleaseItemViewModel
        {
            Id = "rel-1",
            Name = "Release 1.0",
            IsDownloaded = true,
            IsUpdateAvailable = false,
            DownloadedManifestId = "1.100.pub.mod.test",
        };
        viewModel.Releases.Add(release);

        // Act
        viewModel.SelectDownloadableItemCommand.Execute(release);

        // Assert
        Assert.Same(release, viewModel.SelectedDownloadableItem);
        Assert.False(viewModel.ShowUpdateButton);
        Assert.True(viewModel.ShowAddToProfileButton);
        Assert.False(viewModel.ShowDownloadButton);
    }

    /// <summary>
    /// Verifies that when an un-downloaded release item is selected, ShowDownloadButton is true,
    /// and ShowUpdateButton and ShowAddToProfileButton are false.
    /// </summary>
    [Fact]
    public void SelectedDownloadableItem_NotDownloaded_ShowsDownloadButton()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-item", Name = "Test" };
        var viewModel = CreateViewModel(item, coordinator.Object);

        var release = new ReleaseItemViewModel
        {
            Id = "rel-1",
            Name = "Release 1.0",
            IsDownloaded = false,
            IsUpdateAvailable = false,
        };
        viewModel.Releases.Add(release);

        // Act
        viewModel.SelectDownloadableItemCommand.Execute(release);

        // Assert
        Assert.Same(release, viewModel.SelectedDownloadableItem);
        Assert.False(viewModel.ShowUpdateButton);
        Assert.False(viewModel.ShowAddToProfileButton);
        Assert.True(viewModel.ShowDownloadButton);
    }

    /// <summary>
    /// Verifies that when ModDB returns multiple releases and only one is downloaded,
    /// each row gets its own distinct resolver metadata and only the downloaded row is marked downloaded.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task PopulateReleases_ModDbMultipleReleases_OnlyDownloadedRowIsMarkedDownloadedAsync()
    {
        // Arrange
        const string urlV92a = "https://www.moddb.com/downloads/start/307616";
        const string urlV091a = "https://www.moddb.com/downloads/start/297149";
        const string urlV09a = "https://www.moddb.com/downloads/start/297079";

        var searchResult = new ContentSearchResult
        {
            Id = "1.20260413.moddb.mod.admiralzv92a",
            Name = "Admiral Z v92a",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/janus-syndicate/downloads/admiral-z-v92a",
            LastUpdated = new DateTime(2026, 4, 13),
        };
        searchResult.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = "admiral-z-v92a";

        var file1 = new DownloadableFile(
            Name: "Admiral Z v92a",
            DownloadUrl: urlV92a,
            DetailsUrl: "https://www.moddb.com/mods/janus-syndicate/downloads/admiral-z-v92a",
            FileSectionType: FileSectionType.Downloads,
            ReleaseDate: new DateTime(2026, 4, 13));

        var file2 = new DownloadableFile(
            Name: "Admiral Z v0.91a",
            DownloadUrl: urlV091a,
            DetailsUrl: "https://www.moddb.com/mods/janus-syndicate/downloads/admiral-z-v091a",
            FileSectionType: FileSectionType.Downloads,
            ReleaseDate: new DateTime(2025, 9, 24));

        var file3 = new DownloadableFile(
            Name: "AdmiralZ v0.9a",
            DownloadUrl: urlV09a,
            DetailsUrl: "https://www.moddb.com/mods/janus-syndicate/downloads/admiral-z-v09a",
            FileSectionType: FileSectionType.Downloads,
            ReleaseDate: new DateTime(2025, 9, 21));

        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl == urlV92a), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);
        stateService
            .Setup(s => s.GetStateAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl != urlV92a), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);

        stateService
            .Setup(s => s.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl == urlV92a), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1.20260413.moddb.mod.admiralzv92a");
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl != urlV92a), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(searchResult, coordinator.Object, contentStateService: stateService.Object);

        // Act
        viewModel.PopulateReleases([file1, file2, file3]);
        await viewModel.WaitForRowStateResolutionsAsync();

        // Assert
        Assert.Equal(3, viewModel.Releases.Count);

        var rel0 = viewModel.Releases[0];
        Assert.Equal("Admiral Z v92a", rel0.Name);
        Assert.True(rel0.IsDownloaded);
        Assert.Equal("1.20260413.moddb.mod.admiralzv92a", rel0.DownloadedManifestId);

        var rel1 = viewModel.Releases[1];
        Assert.Equal("Admiral Z v0.91a", rel1.Name);
        Assert.False(rel1.IsDownloaded);
        Assert.Null(rel1.DownloadedManifestId);

        var rel2 = viewModel.Releases[2];
        Assert.Equal("AdmiralZ v0.9a", rel2.Name);
        Assert.False(rel2.IsDownloaded);
        Assert.Null(rel2.DownloadedManifestId);
    }

    /// <summary>
    /// Verifies that when multiple releases share the exact same display name (e.g. ModDB Patch vs Full Version),
    /// only the release matching the downloaded manifest is marked downloaded.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PopulateReleases_WhenReleasesShareName_OnlyExactDownloadedReleaseIsMarkedDownloadedAsync()
    {
        // Arrange
        const string fullModUrl = "https://www.moddb.com/downloads/start/115960";
        const string patchUrl = "https://www.moddb.com/downloads/start/170000";

        var searchResult = new ContentSearchResult
        {
            Id = "1.20200905.moddb.mod.shwchaos",
            Name = "C&C: Shockwave Chaos",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/cc-shockwave-chaos",
        };

        var patchFile = new DownloadableFile(
            Name: "SHW Chaos",
            Category: "Patch",
            DownloadUrl: patchUrl,
            DetailsUrl: "https://www.moddb.com/mods/cc-shockwave-chaos/downloads/shw-chaos-patch",
            ReleaseDate: new DateTime(2020, 9, 5),
            FileSectionType: FileSectionType.Downloads);

        var fullFile = new DownloadableFile(
            Name: "SHW Chaos",
            Category: "Full Version",
            DownloadUrl: fullModUrl,
            DetailsUrl: "https://www.moddb.com/mods/cc-shockwave-chaos/downloads/shw-chaos-mod",
            ReleaseDate: new DateTime(2016, 12, 19),
            FileSectionType: FileSectionType.Downloads);

        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl == fullModUrl), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);
        stateService
            .Setup(s => s.GetStateAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl == patchUrl), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);

        stateService
            .Setup(s => s.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl == fullModUrl), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1.20161219.moddb.mod.shwchaos");
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl == patchUrl), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(searchResult, coordinator.Object, contentStateService: stateService.Object);

        // Act
        viewModel.PopulateReleases([patchFile, fullFile]);
        await viewModel.WaitForRowStateResolutionsAsync();

        // Assert
        Assert.Equal(2, viewModel.Releases.Count);

        var patchRel = viewModel.Releases[0];
        Assert.Equal("SHW Chaos", patchRel.Name);
        Assert.False(patchRel.IsDownloaded);
        Assert.Null(patchRel.DownloadedManifestId);

        var fullRel = viewModel.Releases[1];
        Assert.Equal("SHW Chaos", fullRel.Name);
        Assert.True(fullRel.IsDownloaded);
        Assert.Equal("1.20161219.moddb.mod.shwchaos", fullRel.DownloadedManifestId);
    }

    /// <summary>
    /// Verifies that OpenUrl rejects null, whitespace, or non-http/https URIs without throwing exceptions.
    /// </summary>
    /// <param name="url">The URL string to evaluate.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.com/file.zip")]
    [InlineData("file:///C:/malicious.exe")]
    public void OpenUrlCommand_WithInvalidOrNonHttpScheme_DoesNotThrow(string? url)
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-item", Name = "Test" };
        var viewModel = CreateViewModel(item, coordinator.Object);

        // Act & Assert
        var ex = Record.Exception(() => viewModel.OpenUrlCommand.Execute(url));
        Assert.Null(ex);
    }

    /// <summary>
    /// Verifies that ShowDownloadButton returns false when IsDownloading is true, even when not downloaded.
    /// </summary>
    [Fact]
    public void ShowDownloadButton_WhenIsDownloading_ReturnsFalse()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-item", Name = "Test Item" };
        var viewModel = CreateViewModel(item, coordinator.Object);

        viewModel.IsDownloaded = false;
        viewModel.IsDownloading = false;
        Assert.True(viewModel.ShowDownloadButton);

        // Act
        viewModel.IsDownloading = true;

        // Assert
        Assert.False(viewModel.ShowDownloadButton);
    }

    /// <summary>
    /// Verifies that Initialize detects in-flight coordinator downloads and sets IsDownloading and progress.
    /// </summary>
    [Fact]
    public void Initialize_WhenCoordinatorHasInFlightDownload_SetsIsDownloadingAndProgress()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-inflight-item", Name = "Inflight Item", ProviderName = "ModDB" };

        double progressPct = 60;
        string statusMsg = "Downloading 60%...";
        coordinator.Setup(c => c.IsDownloading(item)).Returns(true);
        coordinator.Setup(c => c.TryGetDownloadProgress(item, out progressPct, out statusMsg)).Returns(true);

        var viewModel = CreateViewModel(item, coordinator.Object);

        // Act
        viewModel.Initialize();

        // Assert
        Assert.True(viewModel.IsDownloading);
        Assert.False(viewModel.ShowDownloadButton);
        Assert.Equal(60, viewModel.DownloadProgress);
        Assert.Equal("Downloading 60%...", viewModel.DownloadStatusMessage);
    }

    /// <summary>
    /// Verifies that download messages broadcast via WeakReferenceMessenger update ContentDetailViewModel state.
    /// </summary>
    [Fact]
    public void DownloadMessages_Broadcast_UpdatesDetailViewModelDownloadState()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-msg-item", Name = "Message Item", ProviderName = "ModDB" };
        var viewModel = CreateViewModel(item, coordinator.Object);
        viewModel.Initialize();

        Assert.False(viewModel.IsDownloading);
        Assert.True(viewModel.ShowDownloadButton);

        // Act 1: Send Started message
        WeakReferenceMessenger.Default.Send(new ContentDownloadStartedMessage(
            "ModDB::test-msg-item",
            item.Id,
            item.ProviderName,
            item.Name));

        Assert.True(viewModel.IsDownloading);
        Assert.False(viewModel.ShowDownloadButton);

        // Act 2: Send Progress message
        WeakReferenceMessenger.Default.Send(new ContentDownloadProgressMessage(
            "ModDB::test-msg-item",
            item.Id,
            item.ProviderName,
            item.Name,
            75,
            "75% downloaded"));

        Assert.True(viewModel.IsDownloading);
        Assert.Equal(75, viewModel.DownloadProgress);
        Assert.Equal("75% downloaded", viewModel.DownloadStatusMessage);

        // Act 3: Send Completed message
        WeakReferenceMessenger.Default.Send(new ContentDownloadCompletedMessage(
            "ModDB::test-msg-item",
            item.Id,
            item.ProviderName,
            item.Name,
            true));

        Assert.False(viewModel.IsDownloading);
    }

    /// <summary>
    /// Verifies that UpdateCommand executes the update action, clears update availability, and updates ShowUpdateButton.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task UpdateCommand_WhenUpdateActionProvided_ExecutesUpdateActionAndClearsUpdateAvailableAsync()
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = "weekly-2026-08-21",
            Name = "GeneralsGameCode weekly-2026-08-21",
            ProviderName = PublisherTypeConstants.TheSuperHackers,
        };
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var stateService = new Mock<IContentStateService>();
        stateService.Setup(s => s.GetStateAsync(searchResult, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);

        var updateExecuted = false;
        var viewModel = CreateViewModel(
            searchResult,
            coordinator.Object,
            contentStateService: stateService.Object,
            updateAction: ct =>
            {
                updateExecuted = true;
                return Task.CompletedTask;
            },
            isUpdateAvailable: true);

        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert initial update state
        Assert.True(viewModel.IsUpdateAvailable);
        Assert.True(viewModel.ShowUpdateButton);

        // Act
        await viewModel.UpdateCommand.ExecuteAsync(null);

        // Assert
        Assert.True(updateExecuted);
        Assert.False(viewModel.IsUpdateAvailable);
        Assert.False(viewModel.ShowUpdateButton);
    }

    /// <summary>
    /// Verifies that ReconcileReleases marks older downloaded releases with IsUpdateAvailable when a newer release is not downloaded.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReconcileReleases_WhenOlderReleaseDownloadedAndNewerNotDownloaded_MarksUpdateAvailableAsync()
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = "mod-parent",
            Name = "Test Mod",
            ProviderName = "ModDB",
        };
        var fileNewer = new DownloadableFile(
            Name: "Test Mod v2.0",
            DownloadUrl: "https://example.com/v2.zip",
            ReleaseDate: new DateTime(2026, 9, 1),
            FileSectionType: FileSectionType.Downloads);
        var fileOlder = new DownloadableFile(
            Name: "Test Mod v1.0",
            DownloadUrl: "https://example.com/v1.zip",
            ReleaseDate: new DateTime(2026, 8, 1),
            FileSectionType: FileSectionType.Downloads);

        var stateService = new Mock<IContentStateService>();
        stateService.Setup(s => s.GetStateAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl == "https://example.com/v2.zip"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);
        stateService.Setup(s => s.GetStateAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl == "https://example.com/v1.zip"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);
        stateService.Setup(s => s.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(r => r.SelectedDownloadUrl == "https://example.com/v1.zip"), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1.20260801.test.mod.test");

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(searchResult, coordinator.Object, contentStateService: stateService.Object);

        // Act
        viewModel.PopulateReleases([fileNewer, fileOlder]);
        await viewModel.WaitForRowStateResolutionsAsync();

        // Assert
        Assert.Equal(2, viewModel.Releases.Count);
        var newerRel = viewModel.Releases[0];
        var olderRel = viewModel.Releases[1];

        Assert.False(newerRel.IsDownloaded);
        Assert.False(newerRel.IsUpdateAvailable);

        Assert.True(olderRel.IsDownloaded);
        Assert.True(olderRel.IsUpdateAvailable);

        viewModel.SelectDownloadableItemCommand.Execute(olderRel);
        Assert.True(viewModel.ShowUpdateButton);
        Assert.True(viewModel.ShowAddToProfileButton);
        Assert.False(viewModel.ShowDownloadButton);
    }

    /// <summary>
    /// Verifies the delete button is visible only for downloaded content.
    /// </summary>
    [Fact]
    public void ShowDeleteButton_ReflectsDownloadedState()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var item = new ContentSearchResult { Id = "test-item", Name = "Test Item" };
        var viewModel = CreateViewModel(item, coordinator.Object);

        viewModel.IsDownloaded = false;
        Assert.False(viewModel.ShowDeleteButton);

        // Act
        viewModel.IsDownloaded = true;

        // Assert
        Assert.True(viewModel.ShowDeleteButton);
    }

    /// <summary>
    /// Verifies that confirming the delete dialog removes the manifest, notifies success,
    /// and invokes the deleted callback with the removed manifest ID.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenConfirmed_RemovesManifestAndNotifiesAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.custom.mod.test";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Custom Mod",
            ProviderName = "custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var manifestPool = CreateManifestPoolMock(CreateDownloadedManifest(manifestId, "Custom Mod", ContentType.Mod));
        ManifestId? removedId = null;
        manifestPool
            .Setup(pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ManifestId, bool, CancellationToken>((id, _, _) => removedId = id)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(manager => manager.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));
        profileManager
            .Setup(manager => manager.ScrubDeletedManifestReferencesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ProfileScrubResult>.CreateSuccess(new ProfileScrubResult(0, 0, [])));

        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(dialog => dialog.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true);

        var notifications = new Mock<INotificationService>();
        var artworkService = new Mock<IContentArtworkService>();
        artworkService
            .Setup(service => service.PurgeArtworkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        string? deletedId = null;
        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            notificationService: notifications.Object,
            profileManager: profileManager.Object,
            dialogService: dialogService.Object,
            deletedAction: id =>
            {
                deletedId = id;
                return Task.CompletedTask;
            },
            artworkService: artworkService.Object);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(removedId);
        Assert.Equal(manifestId, removedId.Value.Value);
        Assert.Equal(manifestId, deletedId);
        profileManager.Verify(
            manager => manager.ScrubDeletedManifestReferencesAsync(
                It.Is<IEnumerable<string>>(ids => ids.Contains(manifestId)),
                CancellationToken.None),
            Times.Once);
        artworkService.Verify(
            service => service.PurgeArtworkAsync(manifestId, It.IsAny<CancellationToken>()),
            Times.Once);
        notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when scrubbing profile references fails after deleting a download, a warning notification is displayed.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenScrubFails_ShowsWarningNotificationAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.custom.mod.test";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Custom Mod",
            ProviderName = "custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var manifestPool = CreateManifestPoolMock(CreateDownloadedManifest(manifestId, "Custom Mod", ContentType.Mod));
        manifestPool
            .Setup(pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(manager => manager.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));
        profileManager
            .Setup(manager => manager.ScrubDeletedManifestReferencesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ProfileScrubResult>.CreateFailure("Storage locked"));

        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(dialog => dialog.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true);

        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            notificationService: notifications.Object,
            profileManager: profileManager.Object,
            dialogService: dialogService.Object);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when scrubbing profile references reports failed profiles, a warning notification is displayed.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenScrubHasFailedProfiles_ShowsWarningNotificationAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.custom.mod.test";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Custom Mod",
            ProviderName = "custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var manifestPool = CreateManifestPoolMock(CreateDownloadedManifest(manifestId, "Custom Mod", ContentType.Mod));
        manifestPool
            .Setup(pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(manager => manager.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));
        profileManager
            .Setup(manager => manager.ScrubDeletedManifestReferencesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ProfileScrubResult>.CreateSuccess(new ProfileScrubResult(1, 0, ["Locked Profile"])));

        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(dialog => dialog.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true);

        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            notificationService: notifications.Object,
            profileManager: profileManager.Object,
            dialogService: dialogService.Object);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that dismissing the delete dialog leaves the stored manifest untouched.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenCancelled_DoesNotRemoveManifestAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.custom.mod.test";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Custom Mod",
            ProviderName = "custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var manifestPool = CreateManifestPoolMock(CreateDownloadedManifest(manifestId, "Custom Mod", ContentType.Mod));
        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(manager => manager.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(dialog => dialog.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(false);

        var deleted = false;
        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            profileManager: profileManager.Object,
            dialogService: dialogService.Object,
            deletedAction: _ =>
            {
                deleted = true;
                return Task.CompletedTask;
            });
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        manifestPool.Verify(
            pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.False(deleted);
    }

    /// <summary>
    /// Verifies the delete confirmation names the profiles currently using the content.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenUsedByProfiles_NamesProfilesInConfirmationAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.custom.mod.test";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Custom Mod",
            ProviderName = "custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(manager => manager.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(
            [
                new GameProfile { Id = "main", Name = "Main Profile", EnabledContentIds = [manifestId] },
                new GameProfile { Id = "other", Name = "Other Profile", EnabledContentIds = ["1.00000000.other.mod.else"] },
            ]));

        string? confirmationMessage = null;
        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(dialog => dialog.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .Callback<string, string, string, string, string?>((_, message, _, _, _) => confirmationMessage = message)
            .ReturnsAsync(false);

        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: CreateManifestPoolMock(CreateDownloadedManifest(manifestId, "Custom Mod", ContentType.Mod)).Object,
            profileManager: profileManager.Object,
            dialogService: dialogService.Object);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(confirmationMessage);
        Assert.Contains("Main Profile", confirmationMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Profile", confirmationMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a missing confirmation service fails closed instead of deleting silently.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenDialogServiceMissing_DoesNotRemoveManifestAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.custom.mod.test";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Custom Mod",
            ProviderName = "custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var manifestPool = CreateManifestPoolMock(CreateDownloadedManifest(manifestId, "Custom Mod", ContentType.Mod));
        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(manager => manager.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            profileManager: profileManager.Object,
            dialogService: null);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        manifestPool.Verify(
            pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a profile-lookup failure aborts the delete with an error instead of
    /// confirming without the in-use warning.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenProfileLookupFails_AbortsDeleteAndReportsErrorAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.custom.mod.test";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Custom Mod",
            ProviderName = "custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var manifestPool = CreateManifestPoolMock(CreateDownloadedManifest(manifestId, "Custom Mod", ContentType.Mod));
        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(manager => manager.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateFailure("Profiles unavailable"));

        var dialogService = new Mock<IDialogService>();
        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            notificationService: notifications.Object,
            profileManager: profileManager.Object,
            dialogService: dialogService.Object);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        manifestPool.Verify(
            pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        dialogService.Verify(
            dialog => dialog.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a manifest lookup failure aborts the delete with an error instead of
    /// proceeding or throwing an unhandled exception.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenManifestLookupFails_AbortsDeleteAndReportsErrorAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.custom.mod.test";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Custom Mod",
            ProviderName = "custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Manifest pool read error"));

        var dialogService = new Mock<IDialogService>();
        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            notificationService: notifications.Object,
            dialogService: dialogService.Object);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        manifestPool.Verify(
            pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        dialogService.Verify(
            dialog => dialog.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a type dependents enumeration failure aborts the delete with an error instead of
    /// proceeding or throwing an unhandled exception.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenTypeDependentsLookupFails_AbortsDeleteAndReportsErrorAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.custom.mod.test";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Custom Mod",
            ProviderName = "custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var doomedManifest = CreateDownloadedManifest(manifestId, "Custom Mod", ContentType.Mod);
        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(doomedManifest));
        manifestPool
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateFailure("Stored content enumeration failed"));

        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(manager => manager.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        var dialogService = new Mock<IDialogService>();
        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            profileManager: profileManager.Object,
            notificationService: notifications.Object,
            dialogService: dialogService.Object);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        manifestPool.Verify(
            pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        dialogService.Verify(
            dialog => dialog.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that launcher-managed installation manifests cannot be deleted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenManifestIsGameInstallation_BlocksDeleteAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.steam.gameinstallation.zerohour";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Steam Zero Hour",
            ProviderName = "steam",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };

        var manifestPool = CreateManifestPoolMock(CreateDownloadedManifest(manifestId, "Steam Zero Hour", ContentType.GameInstallation));
        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            notificationService: notifications.Object,
            dialogService: new Mock<IDialogService>().Object);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert: the button stays visible (search-result types are unreliable) while the
        // command blocks launcher-managed manifests authoritatively.
        Assert.True(viewModel.ShowDeleteButton);
        manifestPool.Verify(
            pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies the delete command blocks locally detected game clients, which the
    /// launcher regenerates automatically, instead of removing them.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenManifestIsLocalGameClient_BlocksDeleteAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.steam.gameclient.zerohour";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Steam Zero Hour",
            ProviderName = "steam",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        var manifestPool = CreateManifestPoolMock(CreateDownloadedManifest(manifestId, "Steam Zero Hour", ContentType.GameClient));
        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            notificationService: notifications.Object,
            dialogService: new Mock<IDialogService>().Object);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        manifestPool.Verify(
            pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies the delete confirmation names other stored content with type-based
    /// dependencies the doomed manifest may satisfy.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteDownloadCommand_WhenTypeDependentsExist_NamesThemInConfirmationAsync()
    {
        // Arrange
        const string manifestId = "1.20260901.custom.patch.test";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Custom Patch",
            ProviderName = "custom",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
        };

        var doomedManifest = CreateDownloadedManifest(manifestId, "Custom Patch", ContentType.Patch);
        var dependentManifest = CreateDownloadedManifest("1.20260902.custom.mod.needy", "Needy Mod", ContentType.Mod);
        dependentManifest.Dependencies.Add(new ContentDependency
        {
            Id = ManifestId.Create(ManifestConstants.DefaultContentDependencyId),
            Name = "Base patch",
            DependencyType = ContentType.Patch,
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            CompatibleGameTypes = [GameType.ZeroHour],
            IsOptional = false,
        });
        var unrelatedManifest = CreateDownloadedManifest("1.20260903.custom.mod.loner", "Loner Mod", ContentType.Mod);

        var manifestPool = CreateManifestPoolMock(doomedManifest, [doomedManifest, dependentManifest, unrelatedManifest]);
        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(manager => manager.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        string? confirmationMessage = null;
        var dialogService = new Mock<IDialogService>();
        dialogService
            .Setup(dialog => dialog.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .Callback<string, string, string, string, string?>((_, message, _, _, _) => confirmationMessage = message)
            .ReturnsAsync(false);

        var viewModel = CreateViewModel(
            searchResult,
            new Mock<IContentDownloadCoordinator>().Object,
            manifestPool: manifestPool.Object,
            profileManager: profileManager.Object,
            dialogService: dialogService.Object);
        viewModel.IsDownloaded = true;

        // Act
        await viewModel.DeleteDownloadCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(confirmationMessage);
        Assert.Contains("Needy Mod", confirmationMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Loner Mod", confirmationMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that items without a description show key content facts instead of a blank About section.
    /// </summary>
    [Fact]
    public void FormattedDescription_WhenEmpty_ShowsDetailsFallback()
    {
        // Arrange
        var item = new ContentSearchResult
        {
            Id = "test-item",
            Name = "Test Item",
            Description = string.Empty,
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            Version = "1.04",
            AuthorName = "Test Author",
        };
        var viewModel = CreateViewModel(item, new Mock<IContentDownloadCoordinator>().Object);

        // Act
        var formatted = viewModel.FormattedDescription;

        // Assert
        Assert.Contains("No description available.", formatted, StringComparison.Ordinal);
        Assert.Contains("1.04", formatted, StringComparison.Ordinal);
        Assert.Contains("Test Author", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that items with a description render it instead of the facts fallback.
    /// </summary>
    [Fact]
    public void FormattedDescription_WhenPresent_RendersDescription()
    {
        // Arrange
        var item = new ContentSearchResult
        {
            Id = "test-item",
            Name = "Test Item",
            Description = "A great patch with many fixes.",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
        };
        var viewModel = CreateViewModel(item, new Mock<IContentDownloadCoordinator>().Object);

        // Act
        var formatted = viewModel.FormattedDescription;

        // Assert
        Assert.Contains("A great patch with many fixes.", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("No description available.", formatted, StringComparison.Ordinal);
    }

    private static Mock<IContentManifestPool> CreateManifestPoolMock(ContentManifest doomedManifest, IReadOnlyList<ContentManifest>? allManifests = null)
    {
        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(doomedManifest));
        manifestPool
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(allManifests ?? [doomedManifest]));
        return manifestPool;
    }

    private static ContentManifest CreateDownloadedManifest(string manifestId, string name, ContentType contentType)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(manifestId),
            Name = name,
            ContentType = contentType,
            TargetGame = GameType.ZeroHour,
        };
    }

    private static CapturingContentDetailViewModel CreateGitHubVariantViewModel(
        string repoDescription,
        string firstTag,
        string secondTag,
        out Mock<IGitHubApiClient> gitHubMock)
    {
        ContentSearchResult CreateSibling(string suffix, string tag, string assetName)
        {
            var sibling = new ContentSearchResult
            {
                Id = $"github.modauthor.coolmod.{suffix}",
                Name = $"coolmod ({suffix})",
                Description = repoDescription,
                Version = tag,
                ContentType = ContentType.Mod,
                TargetGame = GameType.ZeroHour,
                ProviderName = "github-topics",
                AuthorName = "modauthor",
                RequiresResolution = true,
                ResolverId = ContentSourceNames.GitHubResolverId,
                SourceUrl = "https://github.com/modauthor/coolmod",
                LastUpdated = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                DownloadSize = 1024,
            };
            sibling.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "modauthor";
            sibling.ResolverMetadata[GitHubConstants.RepoMetadataKey] = "coolmod";
            sibling.ResolverMetadata[GitHubConstants.TagMetadataKey] = tag;
            sibling.ResolverMetadata[GitHubConstants.AssetNameMetadataKey] = assetName;
            sibling.SetData(new GitHubArtifact
            {
                Name = assetName,
                DownloadUrl = $"https://github.com/modauthor/coolmod/releases/download/{tag}/{assetName}",
                SizeInBytes = 1024,
                IsRelease = true,
            });
            return sibling;
        }

        var first = CreateSibling("v1", firstTag, "coolmod-v1.zip");
        var second = CreateSibling("v2", secondTag, "coolmod-v2.zip");
        gitHubMock = new Mock<IGitHubApiClient>();
        var viewModel = CreateViewModel(
            second,
            new Mock<IContentDownloadCoordinator>().Object,
            variantSearchResults: new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
            {
                [first.Id] = first,
                [second.Id] = second,
            },
            gitHubApiClient: gitHubMock.Object);
        viewModel.Variants =
        [
            new InstallableVariant { ManifestId = first.Id, Name = first.Name },
            new InstallableVariant { ManifestId = second.Id, Name = second.Name },
        ];
        return viewModel;
    }

    private static CapturingContentDetailViewModel CreateViewModel(
        ContentSearchResult searchResult,
        IContentDownloadCoordinator downloadCoordinator,
        IContentManifestPool? manifestPool = null,
        INotificationService? notificationService = null,
        IReadOnlyDictionary<string, ContentSearchResult>? variantSearchResults = null,
        IReadOnlyList<IWebPageParser>? parsers = null,
        IContentStateService? contentStateService = null,
        ContentSearchResult? updateTargetSearchResult = null,
        Func<CancellationToken, Task>? updateAction = null,
        bool? isUpdateAvailable = null,
        string? initialVariantManifestId = null,
        IGameProfileManager? profileManager = null,
        IDialogService? dialogService = null,
        Func<string, Task>? deletedAction = null,
        IContentArtworkService? artworkService = null,
        IGitHubApiClient? gitHubApiClient = null)
    {
        if (contentStateService == null)
        {
            var defaultStateService = new Mock<IContentStateService>();
            defaultStateService
                .Setup(s => s.GetStateByManifestIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ContentState.Downloaded);
            defaultStateService
                .Setup(s => s.GetLocalManifestIdAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ContentSearchResult sr, CancellationToken _) => sr.Id);
            contentStateService = defaultStateService.Object;
        }

        return new CapturingContentDetailViewModel(
            searchResult,
            parsers ?? [],
            new Mock<IProfileContentService>().Object,
            profileManager ?? new Mock<IGameProfileManager>().Object,
            notificationService ?? new Mock<INotificationService>().Object,
            new Mock<ITabProviderRegistry>().Object,
            contentStateService,
            downloadCoordinator,
            manifestPool ?? new Mock<IContentManifestPool>().Object,
            new Mock<ILoggerFactory>().Object,
            new Mock<ILogger<ContentDetailViewModel>>().Object,
            variantSearchResults: variantSearchResults,
            updateTargetSearchResult: updateTargetSearchResult,
            updateAction: updateAction,
            isUpdateAvailable: isUpdateAvailable,
            initialVariantManifestId: initialVariantManifestId,
            dialogService: dialogService,
            deletedAction: deletedAction,
            artworkService: artworkService,
            gitHubApiClient: gitHubApiClient);
    }

    private sealed class CapturingContentDetailViewModel(
        ContentSearchResult searchResult,
        IReadOnlyList<IWebPageParser> parsers,
        IProfileContentService profileContentService,
        IGameProfileManager profileManager,
        INotificationService notificationService,
        ITabProviderRegistry tabProviderRegistry,
        IContentStateService contentStateService,
        IContentDownloadCoordinator downloadCoordinator,
        IContentManifestPool manifestPool,
        ILoggerFactory loggerFactory,
        ILogger<ContentDetailViewModel> logger,
        IReadOnlyDictionary<string, ContentSearchResult>? variantSearchResults = null,
        ContentSearchResult? updateTargetSearchResult = null,
        Func<CancellationToken, Task>? updateAction = null,
        bool? isUpdateAvailable = null,
        string? initialVariantManifestId = null,
        IDialogService? dialogService = null,
        Func<string, Task>? deletedAction = null,
        IContentArtworkService? artworkService = null,
        IGitHubApiClient? gitHubApiClient = null)
        : ContentDetailViewModel(
            searchResult,
            parsers,
            profileContentService,
            profileManager,
            notificationService,
            tabProviderRegistry,
            contentStateService,
            downloadCoordinator,
            manifestPool,
            loggerFactory,
            logger,
            variantSearchResults: variantSearchResults,
            updateTargetSearchResult: updateTargetSearchResult,
            updateAction: updateAction,
            isUpdateAvailable: isUpdateAvailable,
            initialVariantManifestId: initialVariantManifestId,
            dialogService: dialogService,
            deletedAction: deletedAction,
            artworkService: artworkService,
            gitHubApiClient: gitHubApiClient)
    {
        /// <summary>
        /// Gets the manifest ID sent to the profile selection flow.
        /// </summary>
        public string? ProfileManifestId { get; private set; }

        /// <summary>
        /// Gets the content name sent to the profile selection flow.
        /// </summary>
        public string? ProfileContentName { get; private set; }

        /// <summary>
        /// Gets the target game sent to the profile selection flow.
        /// </summary>
        public GameType? ProfileTargetGame { get; private set; }

        /// <summary>
        /// Awaits the content-type persist task started by the Type dropdown.
        /// </summary>
        /// <returns>A task that completes when persistence finishes.</returns>
        public Task AwaitContentTypePersistAsync() => WaitForContentTypePersistAsync();

        /// <inheritdoc />
        protected override Task ShowProfileSelectionDialogAsync(
            string? manifestId = null,
            string? contentName = null,
            GameType? targetGame = null)
        {
            ProfileManifestId = manifestId;
            ProfileContentName = contentName;
            ProfileTargetGame = targetGame;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Verifies that AddToProfile does not trust a valid-format catalog ID if it is not actually
    /// downloaded in the pool, and falls back to GetLocalManifestIdAsync (Finding 1).
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AddToProfile_WhenSearchResultIdNotInPool_FallsBackToLocalManifestIdAsync()
    {
        // Arrange
        const string catalogId = "1.20260901.custom.mod.test";
        const string localManifestId = "1.20260801.custom.mod.test";

        var searchResult = new ContentSearchResult
        {
            Id = catalogId,
            Name = "Custom Mod",
            ProviderName = "custom",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateByManifestIdAsync(catalogId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(searchResult, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localManifestId);
        stateService
            .Setup(s => s.GetStateByManifestIdAsync(localManifestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(searchResult, coordinator.Object, contentStateService: stateService.Object);

        // Act
        await viewModel.AddToProfileCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(localManifestId, viewModel.ProfileManifestId);
    }

    /// <summary>
    /// Verifies that disposing ContentDetailViewModel while a download is in-flight
    /// does not cancel the download operation in ContentDownloadCoordinator.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Dispose_WhileDownloadInFlight_DoesNotCancelCoordinatorDownloadAsync()
    {
        // Arrange
        var item = new ContentSearchResult
        {
            Id = "test-download-item",
            Name = "Test Item",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
        };

        var downloadStartedTcs = new TaskCompletionSource<bool>();
        var tcsCompleteDownload = new TaskCompletionSource<OperationResult<ContentManifest>>();
        var coordinator = new Mock<IContentDownloadCoordinator>();

        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.Is<ContentSearchResult>(sr => sr.Id == item.Id),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Returns<ContentSearchResult, IProgress<ContentAcquisitionProgress>, CancellationToken, bool>((_, _, ct, _) =>
            {
                downloadStartedTcs.SetResult(true);
                ct.Register(() => tcsCompleteDownload.TrySetCanceled(ct));
                return tcsCompleteDownload.Task;
            });

        var viewModel = CreateViewModel(item, coordinator.Object);
        viewModel.Initialize();

        // Act
        var downloadTask = viewModel.DownloadCommand.ExecuteAsync(null);
        await downloadStartedTcs.Task;

        // Dispose the viewmodel (simulating user navigating away or closing detail tab)
        viewModel.Dispose();

        // Assert: Verify cancellation was NOT requested on the coordinator token
        Assert.False(tcsCompleteDownload.Task.IsCanceled);

        // Complete the download successfully
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.moddb.mod.testitem"),
            Name = "Test Item",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        tcsCompleteDownload.SetResult(OperationResult<ContentManifest>.CreateSuccess(manifest));

        // Wait for downloadTask to finish without throwing
        var ex = await Record.ExceptionAsync(() => downloadTask);
        Assert.Null(ex);
    }

    /// <summary>
    /// Verifies that Generals Online preserves its authoritative GameClient content type across release population,
    /// rather than falling back to Addon, and that CanChangeContentType is false.
    /// </summary>
    [Fact]
    public void GeneralsOnline_RetainsGameClientContentType_AndCanChangeContentTypeIsFalse()
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = "generalsonline.client",
            Name = "Generals Online",
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://playgenerals.online/download",
            RequiresResolution = true,
        };

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(searchResult, coordinator.Object);

        // Act
        viewModel.Initialize();
        viewModel.PopulateReleases([new DownloadableFile("Generals Online Client") { FileSectionType = FileSectionType.Downloads }]);

        // Assert
        Assert.Equal(ContentType.GameClient, viewModel.ContentType);
        Assert.Equal(ContentType.GameClient, viewModel.SelectedContentType);
        Assert.False(viewModel.CanChangeContentType);
        Assert.NotEmpty(viewModel.Releases);
        Assert.Equal(ContentType.GameClient, viewModel.Releases[0].ContentType);
    }

    /// <summary>
    /// Verifies that official providers (Generals Online, Community Outpost, The Super Hackers)
    /// lock their content type and reject changes.
    /// </summary>
    /// <param name="provider">The official provider identifier.</param>
    [Theory]
    [InlineData(PublisherTypeConstants.GeneralsOnline)]
    [InlineData(PublisherTypeConstants.CommunityOutpost)]
    [InlineData(PublisherTypeConstants.TheSuperHackers)]
    public void OfficialProviders_CanChangeContentTypeIsFalse_AndContentTypeChangeIsBlocked(string provider)
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = $"{provider}.test",
            Name = $"{provider} Content",
            ProviderName = provider,
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(searchResult, coordinator.Object);

        // Assert initial
        Assert.False(viewModel.CanChangeContentType);

        // Act: try to change SelectedContentType
        viewModel.SelectedContentType = ContentType.Addon;

        // Assert: searchResult.ContentType and VM properties remain unchanged
        Assert.Equal(ContentType.Mod, searchResult.ContentType);
        Assert.Equal(ContentType.Mod, viewModel.ContentType);
        Assert.Equal(ContentType.Mod, viewModel.SelectedContentType);
    }

    /// <summary>
    /// Verifies that generic GitHub community content allows changing content type both before and after download,
    /// but locks it while a download is actively in progress.
    /// </summary>
    [Fact]
    public void GenericGitHub_CanChangeContentTypeIsTrue_BeforeAndAfterDownload_AndFalseWhileDownloading()
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = "github.someone.generals-tool",
            Name = "Generals Community Tool",
            ProviderName = "GitHub",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(searchResult, coordinator.Object);

        // Assert: prior to download, user can change content type
        Assert.True(viewModel.CanChangeContentType);

        // Act: change content type prior to download
        viewModel.SelectedContentType = ContentType.ModdingTool;
        Assert.Equal(ContentType.ModdingTool, searchResult.ContentType);
        Assert.Equal(ContentType.ModdingTool, viewModel.ContentType);

        // Download in progress
        viewModel.IsDownloading = true;
        Assert.False(viewModel.CanChangeContentType);

        // Download complete - content type is editable post-download
        viewModel.IsDownloading = false;
        viewModel.IsDownloaded = true;
        Assert.True(viewModel.CanChangeContentType);
    }

    /// <summary>
    /// Verifies that ModDB content allows changing content type both before and after download,
    /// but locks it while a download is actively in progress.
    /// </summary>
    [Fact]
    public void ModDB_CanChangeContentTypeIsTrue_BeforeAndAfterDownload_AndFalseWhileDownloading()
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = "1.20240101.moddb.mod.supermod",
            Name = "Super Mod",
            ProviderName = ModDBConstants.PublisherDisplayName,
            SourceUrl = "https://www.moddb.com/mods/supermod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(searchResult, coordinator.Object);

        // Assert: prior to download, user can change content type
        Assert.True(viewModel.CanChangeContentType);

        // Act: change content type prior to download
        viewModel.SelectedContentType = ContentType.ModdingTool;
        Assert.Equal(ContentType.ModdingTool, searchResult.ContentType);
        Assert.Equal(ContentType.ModdingTool, viewModel.ContentType);
        Assert.True(searchResult.ResolverMetadata.TryGetValue(ContentConstants.ExplicitContentTypeMetadataKey, out var explicitFlag));
        Assert.Equal(ContentConstants.ExplicitContentTypeEnabledValue, explicitFlag);

        // Download in progress
        viewModel.IsDownloading = true;
        Assert.False(viewModel.CanChangeContentType);

        // Download complete - content type is editable post-download
        viewModel.IsDownloading = false;
        viewModel.IsDownloaded = true;
        Assert.True(viewModel.CanChangeContentType);
    }

    /// <summary>
    /// Verifies that changing content type on a downloaded ModDB item persists the new type
    /// to the manifest pool without re-downloading, and drops game-installation dependencies
    /// when changing to a standalone type (ModdingTool / Executable).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ModDB_SelectedContentType_WhenDownloaded_PersistsTypeAndClearsGameInstallDepsAsync()
    {
        // Arrange
        const string manifestId = "1.20240101.moddb.mod.supermod";
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Super Mod",
            ProviderName = ModDBConstants.PublisherDisplayName,
            SourceUrl = "https://www.moddb.com/mods/supermod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create(manifestId),
            Name = "Super Mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "moddb-superauthor" },
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(ManifestConstants.ZeroHourGameInstallationManifestId),
                    Name = ManifestConstants.ZeroHourInstallationName,
                    DependencyType = ContentType.GameInstallation,
                },
            ],
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "tool.exe",
                    Hash = "fakehash123",
                    Size = 1024,
                },
            ],
        };

        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(p => p.GetManifestAsync(It.Is<ManifestId>(id => id.Value == manifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(originalManifest));

        ContentManifest? persistedManifest = null;
        manifestPoolMock
            .Setup(p => p.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .Callback<ContentManifest, CancellationToken>((m, _) => persistedManifest = m)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(searchResult, coordinator.Object, manifestPool: manifestPoolMock.Object);

        viewModel.IsDownloaded = true;

        // Act: change to standalone ModdingTool
        viewModel.SelectedContentType = ContentType.ModdingTool;

        // Wait deterministically for the queued background persist to finish
        await viewModel.AwaitContentTypePersistAsync();

        // Assert
        Assert.NotNull(persistedManifest);
        Assert.Equal(ContentType.ModdingTool, persistedManifest.ContentType);
        Assert.Empty(persistedManifest.Dependencies);
        manifestPoolMock.Verify(p => p.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that initializing ContentDetailViewModel with an initial variant selection
    /// retains that variant rather than resetting to the default variant.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Initialize_WithInitialVariantManifestId_RetainsSelectedVariantAsync()
    {
        // Arrange: Item with multiple variants (720p, 1080p, Russian)
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.cbpx",
            Name = "Control Bar Pro",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Variants =
            [
                new ContentVariantInfo { Id = "720p", Name = "720p Resolution", ManifestId = "1.0.communityoutpost.addon.cbpx-720p" },
                new ContentVariantInfo { Id = "1080p", Name = "1080p Resolution", ManifestId = "1.0.communityoutpost.addon.cbpx-1080p", IsDefault = false },
                new ContentVariantInfo { Id = "ru", Name = "Russian Language", ManifestId = "1.0.communityoutpost.addon.cbpx-ru", IsDefault = false },
            ],
        };

        var variantsMap = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["1.0.communityoutpost.addon.cbpx-720p"] = new() { Id = "1.0.communityoutpost.addon.cbpx-720p", Name = "Control Bar Pro - 720p", TargetGame = GameType.ZeroHour },
            ["1.0.communityoutpost.addon.cbpx-1080p"] = new() { Id = "1.0.communityoutpost.addon.cbpx-1080p", Name = "Control Bar Pro - 1080p", TargetGame = GameType.ZeroHour },
            ["1.0.communityoutpost.addon.cbpx-ru"] = new() { Id = "1.0.communityoutpost.addon.cbpx-ru", Name = "Control Bar Pro - Russian", TargetGame = GameType.ZeroHour },
        };

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(
            searchResult,
            coordinator.Object,
            variantSearchResults: variantsMap,
            initialVariantManifestId: "1.0.communityoutpost.addon.cbpx-1080p");

        // Act
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert: 1080p variant remains selected, not 720p
        Assert.NotNull(viewModel.SelectedVariant);
        Assert.Equal("1.0.communityoutpost.addon.cbpx-1080p", viewModel.SelectedVariant.ManifestId);
    }

    /// <summary>
    /// Verifies that calling SelectVariantByManifestId before initialization finishes
    /// buffers the selection and applies it once variants load.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectVariantByManifestId_CalledBeforeInitialization_AppliesVariantWhenLoadedAsync()
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.cbpx",
            Name = "Control Bar Pro",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Variants =
            [
                new ContentVariantInfo { Id = "en", Name = "English", ManifestId = "1.0.communityoutpost.addon.cbpx-en", IsDefault = true },
                new ContentVariantInfo { Id = "ru", Name = "Russian", ManifestId = "1.0.communityoutpost.addon.cbpx-ru", IsDefault = false },
            ],
        };

        var variantsMap = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["1.0.communityoutpost.addon.cbpx-en"] = new() { Id = "1.0.communityoutpost.addon.cbpx-en", Name = "English", TargetGame = GameType.ZeroHour },
            ["1.0.communityoutpost.addon.cbpx-ru"] = new() { Id = "1.0.communityoutpost.addon.cbpx-ru", Name = "Russian", TargetGame = GameType.ZeroHour },
        };

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(
            searchResult,
            coordinator.Object,
            variantSearchResults: variantsMap);

        // Act: select variant before initializing
        viewModel.SelectVariantByManifestId("1.0.communityoutpost.addon.cbpx-ru");
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert: Russian was buffered and selected
        Assert.NotNull(viewModel.SelectedVariant);
        Assert.Equal("1.0.communityoutpost.addon.cbpx-ru", viewModel.SelectedVariant.ManifestId);
    }

    /// <summary>
    /// Verifies that synthesized variants carry their variant-specific target game, so selecting
    /// a Generals variant on a Zero Hour parent applies Generals to the detail search result.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectedVariant_SynthesizedVariant_AppliesVariantTargetGameAsync()
    {
        // Arrange: parent associated with Zero Hour, variants split across both games.
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.test.mod.weekly",
            Name = "Weekly Build",
            ProviderName = "superhackers",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Variants =
            [
                new ContentVariantInfo { Id = "zerohour", Name = "Zero Hour", ManifestId = "1.0.test.mod.weekly-zh", TargetGame = GameType.ZeroHour },
                new ContentVariantInfo { Id = "generals", Name = "Generals", ManifestId = "1.0.test.mod.weekly-gen", TargetGame = GameType.Generals },
            ],
        };

        var viewModel = CreateViewModel(searchResult, new Mock<IContentDownloadCoordinator>().Object);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        var generalsVariant = viewModel.Variants.First(v =>
            string.Equals(v.ManifestId, "1.0.test.mod.weekly-gen", StringComparison.OrdinalIgnoreCase));

        // Act
        viewModel.SelectedVariant = generalsVariant;

        // Assert
        Assert.Equal(GameType.Generals, searchResult.TargetGame);
        Assert.Equal("generals", searchResult.ResolverMetadata[CatalogConstants.SelectedVariantMetadataKey]);
    }

    /// <summary>
    /// Verifies that when SearchResult has ResolverMetadata selectedVariant,
    /// ContentDetailViewModel initializes with that variant selected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Initialize_WithSelectedVariantInResolverMetadata_RetainsSelectedVariantAsync()
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.cbpx",
            Name = "Control Bar Pro",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Variants =
            [
                new ContentVariantInfo { Id = "720p", Name = "720p", ManifestId = "1.0.communityoutpost.addon.cbpx-720p" },
                new ContentVariantInfo { Id = "1080p", Name = "1080p", ManifestId = "1.0.communityoutpost.addon.cbpx-1080p" },
            ],
        };
        searchResult.ResolverMetadata["selectedVariant"] = "1080p";

        var variantsMap = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["1.0.communityoutpost.addon.cbpx-720p"] = new() { Id = "1.0.communityoutpost.addon.cbpx-720p", Name = "720p", TargetGame = GameType.ZeroHour },
            ["1.0.communityoutpost.addon.cbpx-1080p"] = new() { Id = "1.0.communityoutpost.addon.cbpx-1080p", Name = "1080p", TargetGame = GameType.ZeroHour },
        };

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(
            searchResult,
            coordinator.Object,
            variantSearchResults: variantsMap);

        // Act
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert: 1080p selected from metadata
        Assert.NotNull(viewModel.SelectedVariant);
        Assert.Equal("1.0.communityoutpost.addon.cbpx-1080p", viewModel.SelectedVariant.ManifestId);
    }

    /// <summary>
    /// Verifies that when a downloadable item has a SHA-256 hash,
    /// the checksum metadata and title are accurately configured for SHA-256 rather than MD5.
    /// </summary>
    [Fact]
    public void DownloadableItem_WithSha256_ConfiguresSha256ChecksumProperties()
    {
        // Arrange
        var item = new ReleaseItemViewModel
        {
            Sha256Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        };

        // Assert
        Assert.True(item.HasSha256Hash);
        Assert.False(item.HasMd5Hash);
        Assert.True(item.HasChecksum);
        Assert.Equal(ContentConstants.Sha256ChecksumTitle, item.ChecksumTitle);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", item.ChecksumDisplay);
    }

    /// <summary>
    /// Verifies that when a downloadable item has an MD5 hash,
    /// the checksum metadata and title are accurately configured for MD5.
    /// </summary>
    [Fact]
    public void DownloadableItem_WithMd5_ConfiguresMd5ChecksumProperties()
    {
        // Arrange
        var item = new ReleaseItemViewModel
        {
            Md5Hash = "098f6bcd4621d373cade4e832627b4f6",
        };

        // Assert
        Assert.False(item.HasSha256Hash);
        Assert.True(item.HasMd5Hash);
        Assert.True(item.HasChecksum);
        Assert.Equal(ContentConstants.Md5ChecksumTitle, item.ChecksumTitle);
        Assert.Equal("098f6bcd4621d373cade4e832627b4f6", item.ChecksumDisplay);
    }

    /// <summary>
    /// Verifies that when a downloadable item has an empty SHA-256 string,
    /// it correctly falls back to displaying the valid MD5 checksum.
    /// </summary>
    [Fact]
    public void DownloadableItem_WithEmptySha256AndValidMd5_DisplaysMd5Checksum()
    {
        // Arrange
        var item = new ReleaseItemViewModel
        {
            Sha256Hash = string.Empty,
            Md5Hash = "098f6bcd4621d373cade4e832627b4f6",
        };

        // Assert
        Assert.False(item.HasSha256Hash);
        Assert.True(item.HasMd5Hash);
        Assert.True(item.HasChecksum);
        Assert.Equal(ContentConstants.Md5ChecksumTitle, item.ChecksumTitle);
        Assert.Equal("098f6bcd4621d373cade4e832627b4f6", item.ChecksumDisplay);
    }

    /// <summary>
    /// Verifies that when content has an update available and its search result ID is not a valid manifest ID
    /// (e.g. Generals Online uninstalled prospective update "GeneralsOnline_082826_QFE1"),
    /// LoadInitialStateAsync does not rewrite SearchResult.Id to the older local manifest ID,
    /// preserving the prospective release's identity and update state.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Initialize_WhenUpdateAvailableAndSearchResultIdInvalidManifest_DoesNotRewriteSearchResultIdAsync()
    {
        // Arrange
        const string prospectiveCatalogId = "GeneralsOnline_082826_QFE1";
        const string oldLocalManifestId = "1.329260.generalsonline.gameclient.60hz";

        var searchResult = new ContentSearchResult
        {
            Id = prospectiveCatalogId,
            Name = "Generals Online",
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateAsync(searchResult, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.UpdateAvailable);
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(searchResult, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldLocalManifestId);

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var viewModel = CreateViewModel(
            searchResult,
            coordinator.Object,
            contentStateService: stateService.Object,
            isUpdateAvailable: true);

        // Act
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert
        Assert.Equal(prospectiveCatalogId, searchResult.Id);
        Assert.True(viewModel.IsUpdateAvailable);
        Assert.True(viewModel.IsDownloaded);
        Assert.True(viewModel.ShowUpdateButton);
        Assert.True(viewModel.ShowAddToProfileButton);
    }

    /// <summary>
    /// Verifies that when a content has multiple releases and a downloaded variant is selected
    /// (e.g. on application restart), the matching release row is marked as downloaded with its manifest ID,
    /// the main download button is hidden, and the Add to Profile button is displayed.
    /// </summary>
    [Fact]
    public void SelectedVariant_WhenDownloadedOnRestart_MarksMatchingReleaseRowAsDownloadedAndShowsAddToProfile()
    {
        // Arrange
        const string manifestId = "1.0.genlauncherzerohour.mod.riseofthereds187publicbuild20";
        var parent = new ContentSearchResult
        {
            Id = "genlauncher-zerohour-riseofthereds",
            Name = "Rise of the Reds",
            ProviderName = "genlauncher",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateAsync(parent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(parent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manifestId);
        stateService
            .Setup(s => s.GetStateAsync(It.Is<ContentSearchResult>(sr => sr.SelectedDownloadUrl == "http://example.com/build20.zip"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(sr => sr.SelectedDownloadUrl == "http://example.com/build20.zip"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(manifestId);
        stateService
            .Setup(s => s.GetStateAsync(It.Is<ContentSearchResult>(sr => sr.SelectedDownloadUrl == "http://example.com/patch28.zip"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);

        var viewModel = CreateViewModel(
            parent,
            new Mock<IContentDownloadCoordinator>().Object,
            contentStateService: stateService.Object);

        var release1 = new DownloadableFile(
            Name: "Rise Of The Reds 1.87 Patch 2.8",
            DownloadUrl: "http://example.com/patch28.zip",
            FileSectionType: FileSectionType.Downloads);

        var release2 = new DownloadableFile(
            Name: "Rise Of The Reds 1.87 Public Build 2.0",
            DownloadUrl: "http://example.com/build20.zip",
            FileSectionType: FileSectionType.Downloads);

        viewModel.PopulateReleases([release1, release2]);

        var variant = new InstallableVariant
        {
            Name = "Rise Of The Reds 1.87 Public Build 2.0",
            ManifestId = manifestId,
            CurrentState = ContentState.Downloaded,
        };

        viewModel.Variants.Add(variant);

        // Act
        viewModel.SelectedVariant = variant;

        // Assert
        Assert.NotNull(viewModel.SelectedDownloadableItem);
        Assert.Equal("Rise Of The Reds 1.87 Public Build 2.0", viewModel.SelectedDownloadableItem.Name);
        Assert.True(viewModel.SelectedDownloadableItem.IsDownloaded, "Selected release row must be marked IsDownloaded=true");
        Assert.Equal(manifestId, viewModel.SelectedDownloadableItem.DownloadedManifestId);
        Assert.False(viewModel.ShowDownloadButton, "ShowDownloadButton must be false when downloaded variant is selected");
        Assert.True(viewModel.ShowAddToProfileButton, "ShowAddToProfileButton must be true when downloaded variant is selected");
    }

    /// <summary>
    /// Verifies that selecting a downloaded variant does not flip row state or bind the
    /// variant's manifest when the variant name matches several same-named release rows:
    /// the variant carries no version, so ambiguous siblings are indistinguishable and the
    /// rows' own probes must resolve them. Selection still moves to the first match.
    /// </summary>
    [Fact]
    public void SelectedVariant_WhenReleaseNameIsAmbiguous_DoesNotBindManifestToSiblingRow()
    {
        // Arrange
        const string manifestId = "1.0.genlauncherzerohour.mod.riseofthereds187publicbuild20";
        var parent = new ContentSearchResult
        {
            Id = "genlauncher-zerohour-riseofthereds",
            Name = "Rise of the Reds",
            ProviderName = "genlauncher",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);

        var viewModel = CreateViewModel(
            parent,
            new Mock<IContentDownloadCoordinator>().Object,
            contentStateService: stateService.Object);

        viewModel.PopulateReleases(
        [
            new DownloadableFile(
                Name: "Shared Title",
                Version: "1.0",
                DownloadUrl: "http://example.com/shared10.zip",
                FileSectionType: FileSectionType.Downloads),
            new DownloadableFile(
                Name: "Shared Title",
                Version: "2.0",
                DownloadUrl: "http://example.com/shared20.zip",
                FileSectionType: FileSectionType.Downloads),
        ]);

        var variant = new InstallableVariant
        {
            Name = "Shared Title",
            ManifestId = manifestId,
            CurrentState = ContentState.Downloaded,
        };

        viewModel.Variants.Add(variant);

        // Act
        viewModel.SelectedVariant = variant;

        // Assert
        Assert.NotNull(viewModel.SelectedDownloadableItem);
        Assert.All(viewModel.Releases, row =>
        {
            Assert.False(row.IsDownloaded);
            Assert.Null(row.DownloadedManifestId);
        });
    }

    /// <summary>
    /// Verifies that LoadInitialStateAsync reconciles release rows when there are multiple releases.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadInitialStateAsync_WithMultipleReleases_ReconcilesMatchingReleaseRowAsync()
    {
        // Arrange
        const string manifestId = "1.0.genlauncherzerohour.mod.riseofthereds187publicbuild20";
        var parent = new ContentSearchResult
        {
            Id = "genlauncher-zerohour-riseofthereds",
            Name = "Rise of the Reds",
            Version = "1.87 Public Build 2.0",
            ProviderName = "genlauncher",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateAsync(parent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(parent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manifestId);

        var viewModel = CreateViewModel(
            parent,
            new Mock<IContentDownloadCoordinator>().Object,
            contentStateService: stateService.Object);

        var release1 = new DownloadableFile(
            Name: "Rise Of The Reds 1.87 Patch 2.8",
            Version: "1.87 Patch 2.8",
            DownloadUrl: "http://example.com/patch28.zip",
            FileSectionType: FileSectionType.Downloads);

        var release2 = new DownloadableFile(
            Name: "Rise Of The Reds 1.87 Public Build 2.0",
            Version: "1.87 Public Build 2.0",
            DownloadUrl: "http://example.com/build20.zip",
            FileSectionType: FileSectionType.Downloads);

        viewModel.PopulateReleases([release1, release2]);

        // Act
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        // Assert
        Assert.True(viewModel.IsDownloaded);
        var matching = viewModel.Releases.FirstOrDefault(r => r.Name == "Rise Of The Reds 1.87 Public Build 2.0");
        Assert.NotNull(matching);
        Assert.True(matching.IsDownloaded);
        Assert.Equal(manifestId, matching.DownloadedManifestId);
    }

    /// <summary>
    /// Verifies that receiving ContentLibraryClearedMessage resets all download, release, and variant states
    /// in the detail view back to NotDownloaded.
    /// </summary>
    [Fact]
    public void ContentLibraryClearedMessage_WhenReceived_ResetsAllDownloadAndVariantStates()
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.test.mod.detail",
            Name = "Test Mod Detail",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        var coordinator = new Mock<IContentDownloadCoordinator>();
        var stateServiceMock = new Mock<IContentStateService>();
        stateServiceMock
            .Setup(s => s.GetStateAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);
        stateServiceMock
            .Setup(s => s.GetStateByManifestIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);

        var viewModel = CreateViewModel(searchResult, coordinator.Object, contentStateService: stateServiceMock.Object);
        viewModel.Initialize();

        var variant = new InstallableVariant
        {
            Name = "Default",
            ManifestId = "1.0.test.mod.detail",
            CurrentState = ContentState.Downloaded,
        };
        viewModel.Variants.Add(variant);
        viewModel.SelectedVariant = variant;

        var release = new ReleaseItemViewModel
        {
            Id = "rel-1",
            Name = "Release 1",
            IsDownloaded = true,
            IsUpdateAvailable = true,
            DownloadedManifestId = "1.0.test.mod.detail",
        };
        viewModel.Releases.Add(release);
        viewModel.SelectedDownloadableItem = release;

        viewModel.IsDownloaded = true;
        viewModel.IsUpdateAvailable = true;

        Assert.True(viewModel.IsDownloaded);
        Assert.True(viewModel.ShowAddToProfileButton);
        Assert.True(viewModel.ShowDeleteButton);
        Assert.False(viewModel.ShowDownloadButton);

        // Act
        WeakReferenceMessenger.Default.Send(new ContentLibraryClearedMessage());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.False(viewModel.IsDownloaded);
        Assert.False(viewModel.IsUpdateAvailable);
        Assert.False(release.IsDownloaded);
        Assert.False(release.IsUpdateAvailable);
        Assert.Null(release.DownloadedManifestId);
        Assert.Equal(ContentState.NotDownloaded, variant.CurrentState);
        Assert.Equal(ContentState.NotDownloaded, viewModel.SelectedVariant.CurrentState);
        Assert.True(viewModel.ShowDownloadButton);
        Assert.False(viewModel.ShowUpdateButton);
        Assert.False(viewModel.ShowAddToProfileButton);
        Assert.False(viewModel.ShowDeleteButton);
    }
}
