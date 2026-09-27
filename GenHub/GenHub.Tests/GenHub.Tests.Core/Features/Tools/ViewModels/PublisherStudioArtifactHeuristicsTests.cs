using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
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
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests verifying Publisher Studio artifact heuristics: game client auto-detection,
/// local file preservation, drag-and-drop population, per-row hosting uploads, and library hints.
/// </summary>
public class PublisherStudioArtifactHeuristicsTests
{
    /// <summary>
    /// Verifies that dropping an executable auto-detects the game client content type.
    /// </summary>
    [Fact]
    public void PopulateFromPath_ExecutableFile_DetectsGameClient()
    {
        var path = CreateTempFile(".exe", "client-bytes");
        try
        {
            var vm = new AddContentDialogViewModel(_ => { });

            vm.PopulateFromPath(path);

            Assert.Equal(GenHub.Core.Models.Enums.ContentType.GameClient, vm.SelectedContentType);
            Assert.False(vm.UseDirectUrl);
            Assert.Equal(path, vm.LocalFilePath);
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    /// <summary>
    /// Verifies that dropping a regular mod archive keeps the default mod content type.
    /// </summary>
    [Fact]
    public void PopulateFromPath_ModArchive_KeepsModType()
    {
        var path = CreateTempFile(".zip", "mod-bytes");
        try
        {
            var vm = new AddContentDialogViewModel(_ => { });

            vm.PopulateFromPath(path);

            Assert.Equal(GenHub.Core.Models.Enums.ContentType.Mod, vm.SelectedContentType);
            Assert.False(vm.UseDirectUrl);
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    /// <summary>
    /// Verifies that creating content from a local game client preserves the file path, size, and MIME type.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateContent_LocalGameClient_PreservesFileAndAssignsMimeTypeAsync()
    {
        var path = CreateTempFile(".exe", "game-client-bytes");
        CatalogContentItem? created = null;
        try
        {
            var vm = new AddContentDialogViewModel(item => created = item);
            vm.PopulateFromPath(path);
            await WaitForHashAsync(vm);

            vm.CreateContentCommand.Execute(null);

            Assert.NotNull(created);
            Assert.Equal(GenHub.Core.Models.Enums.ContentType.GameClient, created.ContentType);
            var artifact = Assert.Single(created.Releases.Single().Artifacts);
            Assert.Equal(path, artifact.LocalFilePath);
            Assert.True(artifact.Size > 0);
            Assert.Equal(HostingConstants.ExecutableContentType, artifact.ContentType);
            Assert.False(string.IsNullOrWhiteSpace(artifact.Sha256));
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    /// <summary>
    /// Verifies that dropping a folder into the artifact dialog names it as a ZIP archive with folder size.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PopulateFromDroppedPath_Folder_SetsZipNameAndSizeAsync()
    {
        var folder = CreateTempFolder("artifact-folder", "inner.txt", "folder-bytes");
        try
        {
            var vm = new AddArtifactDialogViewModel(_ => { });

            await vm.PopulateFromDroppedPathAsync(folder);

            Assert.Equal(folder, vm.LocalFilePath);
            Assert.EndsWith(".zip", vm.Filename, StringComparison.OrdinalIgnoreCase);
            Assert.True(vm.FileSize > 0);
        }
        finally
        {
            DeleteTempFolder(folder);
        }
    }

    /// <summary>
    /// Verifies that dropping a file into the artifact dialog fills filename, size, and hash.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PopulateFromDroppedPath_File_FillsMetadataAsync()
    {
        var path = CreateTempFile(".zip", "artifact-bytes");
        try
        {
            var vm = new AddArtifactDialogViewModel(_ => { });

            await vm.PopulateFromDroppedPathAsync(path);

            Assert.Equal(Path.GetFileName(path), vm.Filename);
            Assert.True(vm.FileSize > 0);
            Assert.False(string.IsNullOrWhiteSpace(vm.Sha256Hash));
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    /// <summary>
    /// Verifies that dropping files into the release dialog creates heuristically filled artifacts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task AddArtifactsFromPaths_Files_CreatesPrimaryAndSecondaryArtifactsAsync()
    {
        var first = CreateTempFile(".zip", "first-bytes");
        var second = CreateTempFile(".exe", "second-bytes");
        try
        {
            var dialogService = new Mock<IPublisherStudioDialogService>();
            var vm = new AddReleaseDialogViewModel(
                new CatalogContentItem(),
                new PublisherCatalog(),
                _ => { },
                dialogService.Object);

            await vm.AddArtifactsFromPathsAsync([first, second]);

            Assert.Equal(2, vm.Artifacts.Count);
            Assert.True(vm.Artifacts[0].IsPrimary);
            Assert.False(vm.Artifacts[1].IsPrimary);
            Assert.Equal(HostingConstants.ZipContentType, vm.Artifacts[0].ContentType);
            Assert.Equal(HostingConstants.ExecutableContentType, vm.Artifacts[1].ContentType);
            Assert.All(vm.Artifacts, a => Assert.False(string.IsNullOrWhiteSpace(a.Sha256)));
        }
        finally
        {
            DeleteTempFile(first);
            DeleteTempFile(second);
        }
    }

    /// <summary>
    /// Verifies that the per-row hosting upload uploads a pending artifact and persists the project.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadHostedAsset_PendingArtifact_UploadsAndPersistsProjectAsync()
    {
        var path = CreateTempFile(".exe", "upload-bytes");
        try
        {
            var artifact = new ReleaseArtifact
            {
                Filename = Path.GetFileName(path),
                LocalFilePath = path,
                Size = new FileInfo(path).Length,
            };
            var project = CreateProjectWithArtifact("content-1", "1.0.0", artifact);
            var provider = new Mock<IHostingProvider>();
            provider.SetupGet(p => p.ProviderId).Returns("test-cloud");
            provider.SetupGet(p => p.DisplayName).Returns("Test Cloud");
            provider.SetupGet(p => p.RequiresAuthentication).Returns(false);
            provider.SetupGet(p => p.IsAuthenticated).Returns(true);
            provider.SetupGet(p => p.SupportsArtifactHosting).Returns(true);
            provider.SetupGet(p => p.SupportsCatalogHosting).Returns(true);
            provider.Setup(p => p.UploadFileAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                {
                    DirectDownloadUrl = "https://cloud.example.com/file.exe",
                    FileId = "file-1",
                    FileSize = artifact.Size,
                }));

            var notifications = new Mock<INotificationService>();
            var vm = new PublishShareViewModel(
                project,
                new Mock<IPublisherStudioService>().Object,
                new Mock<ILogger<PublishShareViewModel>>().Object,
                null,
                new Mock<IHostingStateManager>().Object,
                notifications.Object);
            vm.SelectedHostingProvider = provider.Object;
            var persistCount = 0;
            vm.SaveProjectCallback = () =>
            {
                persistCount++;
                return Task.CompletedTask;
            };

            var asset = new HostedAssetItemViewModel
            {
                AssetKind = HostedAssetKind.Artifact,
                CanUpload = true,
                Name = artifact.Filename,
                ContentId = "content-1",
                ReleaseVersion = "1.0.0",
            };
            await vm.UploadHostedAssetCommand.ExecuteAsync(asset);

            Assert.Equal("https://cloud.example.com/file.exe", artifact.DownloadUrl);
            Assert.Equal(1, persistCount);
            notifications.Verify(
                n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    /// <summary>
    /// Verifies that the per-row hosting upload warns instead of uploading when no provider is connected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadHostedAsset_NotAuthenticated_WarnsWithoutUploadingAsync()
    {
        var artifact = new ReleaseArtifact { Filename = "mod.zip", LocalFilePath = "/tmp/mod.zip" };
        var project = CreateProjectWithArtifact("content-1", "1.0.0", artifact);
        var provider = new Mock<IHostingProvider>();
        provider.SetupGet(p => p.ProviderId).Returns("test-cloud");
        provider.SetupGet(p => p.DisplayName).Returns("Test Cloud");
        provider.SetupGet(p => p.RequiresAuthentication).Returns(true);
        provider.SetupGet(p => p.IsAuthenticated).Returns(false);
        provider.SetupGet(p => p.SupportsArtifactHosting).Returns(true);

        var notifications = new Mock<INotificationService>();
        var vm = new PublishShareViewModel(
            project,
            new Mock<IPublisherStudioService>().Object,
            new Mock<ILogger<PublishShareViewModel>>().Object,
            null,
            new Mock<IHostingStateManager>().Object,
            notifications.Object);
        vm.SelectedHostingProvider = provider.Object;

        var asset = new HostedAssetItemViewModel
        {
            AssetKind = HostedAssetKind.Artifact,
            CanUpload = true,
            Name = artifact.Filename,
        };
        await vm.UploadHostedAssetCommand.ExecuteAsync(asset);

        Assert.Empty(artifact.DownloadUrl);
        notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        provider.Verify(
            p => p.UploadFileAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that the content library shows the hosting hint when files are pending without a provider.
    /// </summary>
    [Fact]
    public void ContentLibrary_PendingUploadWithoutProvider_ShowsHostingHint()
    {
        var artifact = new ReleaseArtifact { Filename = "mod.zip", LocalFilePath = "/tmp/mod.zip" };
        var project = CreateProjectWithArtifact("content-1", "1.0.0", artifact);
        var parent = new PublisherStudioViewModel(
            new Mock<ILogger<PublisherStudioViewModel>>().Object,
            new Mock<IPublisherStudioService>().Object,
            new Mock<IPublisherStudioDialogService>().Object);
        var vm = new ContentLibraryViewModel(
            project,
            project.Catalogs[0],
            parent,
            new Mock<ILogger<ContentLibraryViewModel>>().Object,
            new Mock<IPublisherStudioDialogService>().Object);

        vm.SelectedContent = project.Catalogs[0].Catalog.Content[0];

        Assert.Equal(1, vm.PendingUploadCount);
        Assert.False(vm.IsHostingConnected);
        Assert.True(vm.ShowHostingHint);

        vm.GoToHostingCommand.Execute(null);

        Assert.Equal(PublisherStudioViewModel.TabHostingStorage, parent.SelectedTabIndex);
    }

    /// <summary>
    /// Verifies that deleting a release asks for confirmation, removes it, and reports deletion.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteRelease_Confirmed_RemovesReleaseAndReportsDeletionAsync()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "GenHubPublisherDeleteTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDirectory);
        try
        {
            var release = new ContentRelease
            {
                Version = "1.0.0",
                Artifacts = [new ReleaseArtifact { Filename = "mod.zip" }],
            };
            var project = CreateProjectWithArtifact("content-1", "9.9.9", new ReleaseArtifact { Filename = "other.zip" });
            project.Catalogs[0].Catalog.Content[0].Releases.Add(release);
            project.ProjectPath = Path.Combine(testDirectory, "project.json");

            var configProvider = new Mock<IConfigurationProviderService>();
            configProvider.Setup(x => x.GetApplicationDataPath()).Returns(testDirectory);
            var studioService = new Mock<IPublisherStudioService>();
            studioService
                .Setup(x => x.SaveProjectAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
            var dialogService = new Mock<IPublisherStudioDialogService>();
            dialogService
                .Setup(x => x.ShowConfirmationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(true);
            var notifications = new Mock<INotificationService>();

            var parent = new PublisherStudioViewModel(
                new Mock<ILogger<PublisherStudioViewModel>>().Object,
                studioService.Object,
                dialogService.Object,
                configurationProvider: configProvider.Object);
            parent.CurrentProject = project;
            var vm = new ContentLibraryViewModel(
                project,
                project.Catalogs[0],
                parent,
                new Mock<ILogger<ContentLibraryViewModel>>().Object,
                dialogService.Object,
                notifications.Object);
            vm.SelectedContent = project.Catalogs[0].Catalog.Content[0];

            await vm.DeleteReleaseCommand.ExecuteAsync(release);

            Assert.DoesNotContain(release, project.Catalogs[0].Catalog.Content[0].Releases);
            notifications.Verify(
                n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
            parent.Dispose();
        }
        finally
        {
            DeleteTempFolder(testDirectory);
        }
    }

    /// <summary>
    /// Verifies that canceling the delete confirmation keeps the release untouched.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteRelease_Cancelled_KeepsReleaseAsync()
    {
        var release = new ContentRelease
        {
            Version = "1.0.0",
            Artifacts = [new ReleaseArtifact { Filename = "mod.zip" }],
        };
        var project = CreateProjectWithArtifact("content-1", "9.9.9", new ReleaseArtifact { Filename = "other.zip" });
        project.Catalogs[0].Catalog.Content[0].Releases.Add(release);

        var studioService = new Mock<IPublisherStudioService>();
        var dialogService = new Mock<IPublisherStudioDialogService>();
        dialogService
            .Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(false);

        var parent = new PublisherStudioViewModel(
            new Mock<ILogger<PublisherStudioViewModel>>().Object,
            studioService.Object,
            dialogService.Object);
        parent.CurrentProject = project;
        var vm = new ContentLibraryViewModel(
            project,
            project.Catalogs[0],
            parent,
            new Mock<ILogger<ContentLibraryViewModel>>().Object,
            dialogService.Object);
        vm.SelectedContent = project.Catalogs[0].Catalog.Content[0];

        await vm.DeleteReleaseCommand.ExecuteAsync(release);

        Assert.Contains(release, project.Catalogs[0].Catalog.Content[0].Releases);
        studioService.Verify(
            x => x.SaveProjectAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<CancellationToken>()),
            Times.Never);
        parent.Dispose();
    }

    private static PublisherStudioProject CreateProjectWithArtifact(string contentId, string version, ReleaseArtifact artifact)
    {
        var project = new PublisherStudioProject();
        project.Catalogs.Add(new NamedCatalog
        {
            Id = "default",
            Name = "Content",
            FileName = "catalog.json",
            Catalog = new PublisherCatalog
            {
                Content =
                [
                    new CatalogContentItem
                    {
                        Id = contentId,
                        Name = "Test Content",
                        Releases =
                        [
                            new ContentRelease
                            {
                                Version = version,
                                Artifacts = [artifact],
                            },
                        ],
                    },
                ],
            },
        });

        return project;
    }

    private static string CreateTempFile(string extension, string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"genhub-test-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, contents);
        return path;
    }

    private static void DeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup
        }
    }

    private static string CreateTempFolder(string name, string fileName, string contents)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"genhub-test-{Guid.NewGuid():N}-{name}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, fileName), contents);
        return folder;
    }

    private static void DeleteTempFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup
        }
    }

    private static async Task WaitForHashAsync(AddContentDialogViewModel vm)
    {
        for (var i = 0; i < 200 && (vm.IsComputingHash || string.IsNullOrEmpty(vm.Sha256Hash)); i++)
        {
            await Task.Delay(25);
        }
    }
}
