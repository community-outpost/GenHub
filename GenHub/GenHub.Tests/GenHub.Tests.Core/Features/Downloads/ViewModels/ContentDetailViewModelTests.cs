using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads.ViewModels;

/// <summary>
/// Regression tests for independent release/addon row downloads in the content detail view.
/// </summary>
public sealed class ContentDetailViewModelTests
{
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
                It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>(
                (content, _, _) => coordinatorInput = content)
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
        Assert.StartsWith("file:", coordinatorInput.Id, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(coordinatorInput.Version));
        Assert.True(release.IsDownloaded);
        Assert.Equal(childManifestId, release.DownloadedManifestId);
        Assert.False(viewModel.IsDownloaded);
        Assert.Equal(childManifestId, viewModel.ProfileManifestId);
        Assert.Equal(releaseFile.Name, viewModel.ProfileContentName);
        Assert.Equal(GameType.ZeroHour, viewModel.ProfileTargetGame);
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
                It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>(
                (content, _, _) => downloadedContent = content)
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
                It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>(
                (content, _, _) => downloadedResult = content)
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
                It.IsAny<CancellationToken>()))
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
        await Task.Delay(100);

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
                It.IsAny<CancellationToken>()),
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
        await Task.Delay(150);

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
        await Task.Delay(150);

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

    private static CapturingContentDetailViewModel CreateViewModel(
        ContentSearchResult searchResult,
        IContentDownloadCoordinator downloadCoordinator,
        IContentManifestPool? manifestPool = null,
        INotificationService? notificationService = null,
        IReadOnlyDictionary<string, ContentSearchResult>? variantSearchResults = null,
        IReadOnlyList<IWebPageParser>? parsers = null,
        IContentStateService? contentStateService = null)
    {
        return new CapturingContentDetailViewModel(
            searchResult,
            parsers ?? [],
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            notificationService ?? new Mock<INotificationService>().Object,
            new Mock<ITabProviderRegistry>().Object,
            contentStateService ?? new Mock<IContentStateService>().Object,
            downloadCoordinator,
            manifestPool ?? new Mock<IContentManifestPool>().Object,
            new Mock<ILoggerFactory>().Object,
            new Mock<ILogger<ContentDetailViewModel>>().Object,
            variantSearchResults: variantSearchResults);
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
        IReadOnlyDictionary<string, ContentSearchResult>? variantSearchResults = null)
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
            variantSearchResults: variantSearchResults)
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
}
