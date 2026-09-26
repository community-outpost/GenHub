using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services.Hosting;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests verifying credential console navigation commands, external CDN badge classification,
/// and mixed CDN / cloud storage handling in Publisher Studio.
/// </summary>
public class PublisherStudioMixedCdnAndCredentialTests
{
    private readonly Mock<IPublisherStudioService> _mockStudioService = new();
    private readonly Mock<ILogger<PublishShareViewModel>> _mockPublishLogger = new();
    private readonly Mock<IHostingStateManager> _mockHostingStateManager = new();
    private readonly Mock<INotificationService> _mockNotificationService = new();

    /// <summary>
    /// Tests that UploadArtifactNodeViewModel correctly calculates external CDN vs cloud hosted badges.
    /// </summary>
    [Fact]
    public void UploadArtifactNodeViewModel_ClassifiesCdnCloudAndPending()
    {
        // Case 1: External CDN
        var cdnNode = new UploadArtifactNodeViewModel
        {
            FileName = "gameclient.zip",
            DownloadUrl = "https://cdn.example.com/gameclient.zip",
            IsHosted = true,
            HasLocalFile = false,
            IsExternalCdn = true,
        };
        Assert.False(cdnNode.IsCloudHosted);
        Assert.False(cdnNode.IsPendingUpload);
        Assert.Equal("External CDN", cdnNode.StorageBadgeText);

        // Case 2: Cloud Hosted
        var cloudNode = new UploadArtifactNodeViewModel
        {
            FileName = "mod-patch.zip",
            DownloadUrl = "https://drive.google.com/uc?id=123",
            IsHosted = true,
            HasLocalFile = false,
            IsExternalCdn = false,
        };
        Assert.True(cloudNode.IsCloudHosted);
        Assert.False(cloudNode.IsPendingUpload);
        Assert.Equal("Cloud Hosted", cloudNode.StorageBadgeText);

        // Case 3: Local Pending Upload
        var pendingNode = new UploadArtifactNodeViewModel
        {
            FileName = "large-archive.zip",
            DownloadUrl = string.Empty,
            IsHosted = false,
            HasLocalFile = true,
            LocalFilePath = "/path/to/large-archive.zip",
            IsExternalCdn = false,
        };
        Assert.False(pendingNode.IsCloudHosted);
        Assert.True(pendingNode.IsPendingUpload);
        Assert.Equal("Pending Upload", pendingNode.StorageBadgeText);
    }

    /// <summary>
    /// Tests that ArtifactUrlStatus correctly identifies external CDN URLs versus pending upload.
    /// </summary>
    [Fact]
    public void ArtifactUrlStatus_IdentifiesExternalCdnAndPending()
    {
        var cdnArtifact = new ReleaseArtifact
        {
            Filename = "client.zip",
            DownloadUrl = "https://cdn.fastmirror.org/client.zip",
        };
        var cdnStatus = new ArtifactUrlStatus(cdnArtifact, "Test Content", "1.0.0");
        Assert.True(cdnStatus.IsExternalCdn);
        Assert.False(cdnStatus.IsPendingUpload);
        Assert.Contains("External CDN", cdnStatus.StatusMessage);

        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            var pendingArtifact = new ReleaseArtifact
            {
                Filename = "patch.zip",
                DownloadUrl = string.Empty,
                LocalFilePath = tempFile,
            };
            var pendingStatus = new ArtifactUrlStatus(pendingArtifact, "Test Content", "1.0.0");
            Assert.False(pendingStatus.IsExternalCdn);
            Assert.True(pendingStatus.IsPendingUpload);

            pendingStatus.Validate();
            Assert.Contains("Pending cloud upload", pendingStatus.StatusMessage);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Tests that AddArtifactDialogViewModel.TryParseFileSize parses human-readable units and byte strings correctly.
    /// </summary>
    /// <param name="input">The size string.</param>
    /// <param name="expectedBytes">The expected bytes.</param>
    [Theory]
    [InlineData("500 MB", 500L * 1024 * 1024)]
    [InlineData("1.5 GB", (long)(1.5 * 1024 * 1024 * 1024))]
    [InlineData("250 KB", 250L * 1024)]
    [InlineData("1048576", 1048576L)]
    public void TryParseFileSize_ValidInputs_ParsesExpectedBytes(string input, long expectedBytes)
    {
        var success = AddArtifactDialogViewModel.TryParseFileSize(input, out var bytes);
        Assert.True(success);
        Assert.Equal(expectedBytes, bytes);
    }

    /// <summary>
    /// Tests that AddArtifactDialogViewModel.TryParseFileSize returns false for invalid inputs.
    /// </summary>
    /// <param name="input">The size string.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("-50 MB")]
    public void TryParseFileSize_InvalidInputs_ReturnsFalse(string input)
    {
        var success = AddArtifactDialogViewModel.TryParseFileSize(input, out var bytes);
        Assert.False(success);
        Assert.Equal(0, bytes);
    }

    /// <summary>
    /// Tests that setting ContentType to GameClient preserves the local file selection instead of forcing Direct URL mode.
    /// </summary>
    [Fact]
    public void AddContentDialogViewModel_WhenContentTypeIsGameClient_PreservesLocalFileSelection()
    {
        // Act
        var vm = new AddContentDialogViewModel(_ => { })
        {
            UseDirectUrl = false,
            LocalFilePath = "/path/to/gameclient.exe",
            SelectedContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
        };

        // Assert
        Assert.False(vm.UseDirectUrl);
        Assert.Equal("/path/to/gameclient.exe", vm.LocalFilePath);
    }

    /// <summary>
    /// Tests that credential navigation commands execute gracefully without unhandled exceptions.
    /// </summary>
    [Fact]
    public void PublishShareViewModel_CredentialConsoleCommands_CanBeInvoked()
    {
        var project = new PublisherStudioProject();
        var openedUrls = new List<string>();
        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object,
            browserLauncher: openedUrls.Add);

        // Execute commands and verify they invoke the injected browser launcher with expected URIs
        var googleAuthEx = Record.Exception(() => vm.OpenGoogleAuthPlatformConsoleCommand.Execute(null));
        Assert.Null(googleAuthEx);

        var googleEx = Record.Exception(() => vm.OpenGoogleCredentialsConsoleCommand.Execute(null));
        Assert.Null(googleEx);

        var driveApiEx = Record.Exception(() => vm.OpenGoogleDriveApiConsoleCommand.Execute(null));
        Assert.Null(driveApiEx);

        var githubEx = Record.Exception(() => vm.OpenGitHubTokenConsoleCommand.Execute(null));
        Assert.Null(githubEx);

        var dropboxEx = Record.Exception(() => vm.OpenDropboxAppConsoleCommand.Execute(null));
        Assert.Null(dropboxEx);

        Assert.Contains(HostingConstants.GoogleAuthPlatformUrl, openedUrls);
        Assert.Contains(HostingConstants.GoogleCloudConsoleCredentialsUrl, openedUrls);
        Assert.Contains(HostingConstants.GoogleDriveApiEnablementUrl, openedUrls);
        Assert.Contains(HostingConstants.GitHubPersonalAccessTokensUrl, openedUrls);
        Assert.Contains(HostingConstants.DropboxAppConsoleUrl, openedUrls);
    }

    /// <summary>
    /// Tests that ConnectButtonText and PublishButtonText dynamically adapt to the selected hosting provider.
    /// </summary>
    [Fact]
    public void DynamicButtonTexts_AdaptToSelectedProvider()
    {
        var project = new PublisherStudioProject();
        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);

        // Google Drive
        var mockGoogle = new Mock<IHostingProvider>();
        mockGoogle.Setup(p => p.DisplayName).Returns("Google Drive");
        mockGoogle.Setup(p => p.SupportsArtifactHosting).Returns(true);
        vm.SelectedHostingProvider = mockGoogle.Object;
        Assert.Equal("Connect to Google Drive", vm.ConnectButtonText);
        Assert.Equal("Publish to Google Drive", vm.PublishButtonText);

        // Dropbox
        var mockDropbox = new Mock<IHostingProvider>();
        mockDropbox.Setup(p => p.DisplayName).Returns("Dropbox");
        mockDropbox.Setup(p => p.SupportsArtifactHosting).Returns(true);
        vm.SelectedHostingProvider = mockDropbox.Object;
        Assert.Equal("Connect to Dropbox", vm.ConnectButtonText);
        Assert.Equal("Publish to Dropbox", vm.PublishButtonText);

        // GitHub Gists
        var mockGithub = new Mock<IHostingProvider>();
        mockGithub.Setup(p => p.DisplayName).Returns("GitHub Gists");
        mockGithub.Setup(p => p.SupportsArtifactHosting).Returns(false);
        vm.SelectedHostingProvider = mockGithub.Object;
        Assert.Equal("Connect to GitHub Gists", vm.ConnectButtonText);
        Assert.Equal("Publish to GitHub Gists", vm.PublishButtonText);
    }

    /// <summary>
    /// Tests that TargetDestinationDescription provides clear human-readable destination information.
    /// </summary>
    [Fact]
    public void TargetDestinationDescription_ProvidesClearProviderDestination()
    {
        var project = new PublisherStudioProject();
        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);

        var mockGoogle = new Mock<IHostingProvider>();
        mockGoogle.Setup(p => p.DisplayName).Returns("Google Drive");
        mockGoogle.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        vm.SelectedHostingProvider = mockGoogle.Object;
        Assert.Contains("Google Drive", vm.TargetDestinationDescription);
        Assert.Contains("GenHub_Publisher", vm.TargetDestinationDescription);

        var mockDropbox = new Mock<IHostingProvider>();
        mockDropbox.Setup(p => p.DisplayName).Returns("Dropbox");
        mockDropbox.Setup(p => p.ProviderId).Returns(HostingConstants.Dropbox);
        vm.SelectedHostingProvider = mockDropbox.Object;
        Assert.Contains("Dropbox", vm.TargetDestinationDescription);
        Assert.Contains("/Apps/", vm.TargetDestinationDescription);

        var mockGithub = new Mock<IHostingProvider>();
        mockGithub.Setup(p => p.DisplayName).Returns("GitHub Gists");
        mockGithub.Setup(p => p.ProviderId).Returns(HostingConstants.GitHub);
        vm.SelectedHostingProvider = mockGithub.Object;
        Assert.Contains("GitHub Gists", vm.TargetDestinationDescription);
        Assert.Contains("manifests", vm.TargetDestinationDescription);
    }

    /// <summary>
    /// Tests that HasIncompatibleArtifactsForProvider flags incompatible metadata-only providers
    /// when pending local files require artifact hosting.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IncompatibleArtifacts_FlaggedWhenBinaryHostingNotSupportedAsync()
    {
        var project = new PublisherStudioProject();
        var namedCatalog = new NamedCatalog { Name = "Main Catalog" };
        var item = new CatalogContentItem { Id = "item-1", Name = "Mod Item" };
        var release = new ContentRelease { Version = "1.0.0" };
        var artifact = new ReleaseArtifact
        {
            Filename = "mod.zip",
            LocalFilePath = "/tmp/mod.zip",
            DownloadUrl = string.Empty,
        };
        release.Artifacts.Add(artifact);
        item.Releases.Add(release);
        namedCatalog.Catalog.Content.Add(item);
        project.Catalogs.Add(namedCatalog);

        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object)
        {
            ActiveCatalog = namedCatalog,
        };

        // 1 pending artifact
        Assert.Equal(1, vm.PendingArtifactsCount);
        Assert.Equal(0, vm.ExternalCdnArtifactsCount);

        // Google Drive supports artifact hosting: compatible
        var mockGoogle = new Mock<IHostingProvider>();
        mockGoogle.Setup(p => p.DisplayName).Returns("Google Drive");
        mockGoogle.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        mockGoogle.Setup(p => p.SupportsArtifactHosting).Returns(true);
        vm.SelectedHostingProvider = mockGoogle.Object;
        Assert.False(vm.HasIncompatibleArtifactsForProvider);

        // GitHub Gists does NOT support artifact hosting: INCOMPATIBLE
        var mockGithub = new Mock<IHostingProvider>();
        mockGithub.Setup(p => p.DisplayName).Returns("GitHub Gists");
        mockGithub.Setup(p => p.ProviderId).Returns(HostingConstants.GitHub);
        mockGithub.Setup(p => p.SupportsArtifactHosting).Returns(false);
        vm.SelectedHostingProvider = mockGithub.Object;
        Assert.True(vm.HasIncompatibleArtifactsForProvider);
        Assert.Contains("GitHub Gists only hosts catalog metadata", vm.IncompatibleArtifactsWarningMessage);

        // PublishAllCatalogsCommand should block publish and show notification
        await vm.PublishAllCatalogsCommand.ExecuteAsync(null);
        _mockNotificationService.Verify(n => n.ShowError("Incompatible Provider", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Tests that RenameCatalogInHostingStateAsync updates catalog ID, catalog name, and file name
    /// in the hosting state and persists the changes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PublishShareViewModel_RenameCatalogInHostingStateAsync_UpdatesCatalogEntriesAndSavesAsync()
    {
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var hostingState = new HostingState
        {
            Catalogs =
            [
                new()
                {
                    CatalogId = "old-cat-id",
                    CatalogName = "Old Name",
                    FileName = "catalog-old.json",
                    Url = "https://example.com/catalog-old.json",
                },
                new()
                {
                    CatalogId = "other-cat-id",
                    CatalogName = "Other Name",
                    FileName = "catalog-other.json",
                },
            ],
        };

        var container = new PublisherHostingStates
        {
            States = { [HostingConstants.GoogleDrive] = hostingState },
        };

        _mockHostingStateManager.Setup(m => m.LoadStatesAsync("/test/path/project.json", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherHostingStates>.CreateSuccess(container));
        _mockHostingStateManager.Setup(m => m.SaveStatesAsync("/test/path/project.json", It.IsAny<PublisherHostingStates>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);

        await vm.RenameCatalogInHostingStateAsync("old-cat-id", "new-cat-id", "New Name", "catalog-new.json");

        var renamed = hostingState.Catalogs.Find(c => c.CatalogId == "new-cat-id");
        Assert.NotNull(renamed);
        Assert.Equal("New Name", renamed.CatalogName);
        Assert.Equal("catalog-new.json", renamed.FileName);

        var unaffected = hostingState.Catalogs.Find(c => c.CatalogId == "other-cat-id");
        Assert.NotNull(unaffected);
        Assert.Equal("Other Name", unaffected.CatalogName);

        _mockHostingStateManager.Verify(m => m.SaveStatesAsync("/test/path/project.json", It.IsAny<PublisherHostingStates>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that CancelUploadCommand executes safely without throwing when no upload is currently active.
    /// </summary>
    [Fact]
    public void PublishShareViewModel_CancelUploadCommand_WhenNotUploading_ExecutesSafely()
    {
        var project = new PublisherStudioProject();
        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);

        Assert.False(vm.IsUploading);

        var ex = Record.Exception(() => vm.CancelUploadCommand.Execute(null));
        Assert.Null(ex);
        Assert.False(vm.IsUploading);
    }

    /// <summary>
    /// Tests that reopening a project backed by a non-default provider (e.g., Dropbox) restores
    /// the persisted provider selection and its stored credentials instead of defaulting to Google Drive.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PublishShareViewModel_ReopenNonDefaultProviderProject_RestoresPersistedProviderAndCredentialAsync()
    {
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var hostingState = new HostingState
        {
            ProviderId = HostingConstants.Dropbox,
        };

        var dropboxContainer = new PublisherHostingStates
        {
            States = { [HostingConstants.Dropbox] = hostingState },
        };

        _mockHostingStateManager.Setup(m => m.LoadStatesAsync("/test/path/project.json", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherHostingStates>.CreateSuccess(dropboxContainer));

        var googleMock = new Mock<IHostingProvider>();
        googleMock.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        googleMock.Setup(p => p.DisplayName).Returns("Google Drive");

        var dropboxMock = new Mock<IHostingProvider>();
        dropboxMock.Setup(p => p.ProviderId).Returns(HostingConstants.Dropbox);
        dropboxMock.Setup(p => p.DisplayName).Returns("Dropbox");

        var mockFactory = new Mock<IHostingProviderFactory>();

        // Google Drive is first (default), Dropbox is second
        mockFactory.Setup(f => f.GetCatalogHostingProviders())
            .Returns([googleMock.Object, dropboxMock.Object]);

        var mockCredentialStore = new Mock<IHostingCredentialStore>();
        mockCredentialStore.Setup(s => s.GetCredentialAsync(HostingConstants.Dropbox, It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("dropbox-stored-token");

        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            mockFactory.Object,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object,
            credentialStore: mockCredentialStore.Object);

        await vm.InitializeAsync();

        // Must restore Dropbox as the selected provider, not Google Drive
        Assert.NotNull(vm.SelectedHostingProvider);
        Assert.Equal(HostingConstants.Dropbox, vm.SelectedHostingProvider.ProviderId);
        Assert.Equal("dropbox-stored-token", vm.DropboxAccessToken);
        mockCredentialStore.Verify(s => s.GetCredentialAsync(HostingConstants.Dropbox, It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        mockCredentialStore.Verify(s => s.GetCredentialAsync(HostingConstants.GoogleDrive, It.IsAny<System.Threading.CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that switching hosting providers clears provider-scoped remote file IDs and does not reuse or delete old provider file IDs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PublishShareViewModel_SwitchProvider_ResetsProviderScopedRemoteFileIdsAsync()
    {
        var project = new PublisherStudioProject { ProjectPath = "/test/path/project.json" };
        var hostingState = new HostingState
        {
            ProviderId = HostingConstants.Dropbox,
            Definition = new HostedFileInfo { FileId = "dropbox-def-123" },
            Catalogs =
            [
                new CatalogHostingInfo
                {
                    CatalogId = "cat-1",
                    FileId = "dropbox-cat-123",
                    Url = "https://dropbox.com/cat-123",
                },
            ],
        };

        var switchContainer = new PublisherHostingStates
        {
            States = { [HostingConstants.Dropbox] = hostingState },
        };

        _mockHostingStateManager.Setup(m => m.LoadStatesAsync("/test/path/project.json", It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherHostingStates>.CreateSuccess(switchContainer));

        var googleMock = new Mock<IHostingProvider>();
        googleMock.Setup(p => p.ProviderId).Returns(HostingConstants.GoogleDrive);
        googleMock.Setup(p => p.DisplayName).Returns("Google Drive");
        googleMock.Setup(p => p.SupportsCatalogHosting).Returns(true);
        googleMock.Setup(p => p.SupportsUpdate).Returns(true);

        var dropboxMock = new Mock<IHostingProvider>();
        dropboxMock.Setup(p => p.ProviderId).Returns(HostingConstants.Dropbox);
        dropboxMock.Setup(p => p.DisplayName).Returns("Dropbox");
        dropboxMock.Setup(p => p.SupportsCatalogHosting).Returns(true);

        var mockFactory = new Mock<IHostingProviderFactory>();
        mockFactory.Setup(f => f.GetCatalogHostingProviders())
            .Returns([dropboxMock.Object, googleMock.Object]);

        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            mockFactory.Object,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);

        await vm.InitializeAsync();
        Assert.Equal(HostingConstants.Dropbox, vm.SelectedHostingProvider?.ProviderId);

        // Switch provider to Google Drive
        vm.SelectedHostingProvider = googleMock.Object;

        // ProviderId in state must be migrated and old remote file IDs cleared
        Assert.Equal(HostingConstants.GoogleDrive, vm.CurrentHostingState?.ProviderId);
        Assert.Null(vm.CurrentHostingState?.Definition);
        Assert.NotNull(vm.CurrentHostingState?.Catalogs);
        Assert.Empty(vm.CurrentHostingState!.Catalogs);

        // Verify Google Drive DeleteFileAsync is never called with the old Dropbox file IDs
        googleMock.Verify(p => p.DeleteFileAsync(It.Is<string>(id => id.Contains("dropbox")), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
    }
}
