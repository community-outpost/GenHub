using System;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services.Hosting;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

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
        Assert.True(cdnNode.IsExternalCdn);
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
        Assert.False(cloudNode.IsExternalCdn);
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
        Assert.False(pendingNode.IsExternalCdn);
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

        cdnStatus.Validate();
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
            var pendingStatus = new ArtifactUrlStatus(pendingArtifact, "Test Content", "1.0.0")
            {
                HasLocalFile = true,
                LocalFilePath = tempFile,
            };
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
    /// Tests that setting ContentType to GameClient automatically selects Direct URL distribution mode.
    /// </summary>
    [Fact]
    public void AddContentDialogViewModel_WhenContentTypeIsGameClient_SetsIsGameClientTypeAndDirectUrl()
    {
        var vm = new AddContentDialogViewModel(_ => { })
        {
            UseDirectUrl = false,
        };

        // Act
        vm.SelectedContentType = ContentType.GameClient;

        // Assert
        Assert.True(vm.IsGameClientType);
        Assert.True(vm.UseDirectUrl);
    }

    /// <summary>
    /// Tests that credential navigation commands execute gracefully without unhandled exceptions.
    /// </summary>
    [Fact]
    public void PublishShareViewModel_CredentialConsoleCommands_CanBeInvoked()
    {
        var project = new PublisherStudioProject();
        var vm = new PublishShareViewModel(
            project,
            _mockStudioService.Object,
            _mockPublishLogger.Object,
            null,
            _mockHostingStateManager.Object,
            _mockNotificationService.Object);

        // Execute commands; on headless linux without a default browser launcher configured,
        // they should catch and log warning without crashing.
        var googleEx = Record.Exception(() => vm.OpenGoogleCredentialsConsoleCommand.Execute(null));
        Assert.Null(googleEx);

        var githubEx = Record.Exception(() => vm.OpenGitHubTokenConsoleCommand.Execute(null));
        Assert.Null(githubEx);

        var dropboxEx = Record.Exception(() => vm.OpenDropboxAppConsoleCommand.Execute(null));
        Assert.Null(dropboxEx);
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
        Assert.Contains("GenHub-Publishing", vm.TargetDestinationDescription);

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
        Assert.Contains("GitHub Gists", vm.TargetDestinationDescription);
    }

    /// <summary>
    /// Tests that HasIncompatibleArtifactsForProvider flags incompatible metadata-only providers
    /// when pending local files require artifact hosting.
    /// </summary>
    [Fact]
    public void IncompatibleArtifacts_FlaggedWhenBinaryHostingNotSupported()
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
            _mockNotificationService.Object);

        vm.ActiveCatalog = namedCatalog;

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
        vm.PublishAllCatalogsCommand.Execute(null);
        _mockNotificationService.Verify(n => n.ShowError("Incompatible Provider", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }
}
