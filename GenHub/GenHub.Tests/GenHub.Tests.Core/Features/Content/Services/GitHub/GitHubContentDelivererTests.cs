using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.GitHub;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Tests.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Features.Content.Services.GitHub;

/// <summary>
/// Unit tests for <see cref="GitHubContentDeliverer"/>.
/// </summary>
public class GitHubContentDelivererTests
{
    private readonly Mock<IDownloadService> _downloadService = new();
    private readonly Mock<IContentManifestPool> _manifestPool = new();
    private readonly Mock<ILogger<GitHubContentDeliverer>> _logger = new();
    private readonly Mock<IFileHashProvider> _fileHashProvider = new();
    private readonly PublisherManifestFactoryResolver _factoryResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubContentDelivererTests"/> class.
    /// </summary>
    public GitHubContentDelivererTests()
    {
        _factoryResolver = new PublisherManifestFactoryResolver(
            [],
            new Mock<ILogger<PublisherManifestFactoryResolver>>().Object);
    }

    /// <summary>
    /// Tests that CanDeliver returns true for github.com URLs.
    /// </summary>
    [Fact]
    public void CanDeliver_ShouldReturnTrue_ForGitHubUrls()
    {
        var deliverer = CreateSut();
        var manifest = new ContentManifest
        {
            Files =
            [
                new ManifestFile { DownloadUrl = "https://github.com/user/repo/release.zip" },
            ],
        };

        deliverer.CanDeliver(manifest).Should().BeTrue();
    }

    /// <summary>
    /// Tests that CanDeliver returns false for githubusercontent.com URLs, since the deliverer
    /// only accepts the github.com domain family.
    /// </summary>
    [Fact]
    public void CanDeliver_ShouldReturnFalse_ForGitHubAssetsUrls()
    {
        // Note: objects.githubusercontent.com is NOT a *.github.com subdomain,
        // so the deliverer does not accept it. GitHub release assets use github.com URLs.
        var deliverer = CreateSut();
        var manifest = new ContentManifest
        {
            Files =
            [
                new ManifestFile { DownloadUrl = "https://objects.githubusercontent.com/some-asset.zip" },
            ],
        };

        deliverer.CanDeliver(manifest).Should().BeFalse();
    }

    /// <summary>
    /// Tests that CanDeliver returns false for non-GitHub URLs.
    /// </summary>
    [Fact]
    public void CanDeliver_ShouldReturnFalse_ForNonGitHubUrls()
    {
        var deliverer = CreateSut();
        var manifest = new ContentManifest
        {
            Files =
            [
                new ManifestFile { DownloadUrl = "https://example.com/release.zip" },
            ],
        };

        deliverer.CanDeliver(manifest).Should().BeFalse();
    }

    /// <summary>
    /// Tests that CanDeliver returns false when there are no files.
    /// </summary>
    [Fact]
    public void CanDeliver_ShouldReturnFalse_ForEmptyFileList()
    {
        var deliverer = CreateSut();
        var manifest = new ContentManifest { Files = [] };

        deliverer.CanDeliver(manifest).Should().BeFalse();
    }

    /// <summary>
    /// Tests that CanDeliver returns false when file has no download URL.
    /// </summary>
    [Fact]
    public void CanDeliver_ShouldReturnFalse_ForFileWithNullDownloadUrl()
    {
        var deliverer = CreateSut();
        var manifest = new ContentManifest
        {
            Files = [new ManifestFile { DownloadUrl = null }],
        };

        deliverer.CanDeliver(manifest).Should().BeFalse();
    }

    /// <summary>
    /// Tests that CanDeliver returns true if at least one file has a GitHub URL.
    /// </summary>
    [Fact]
    public void CanDeliver_ShouldReturnTrue_IfAtLeastOneFileIsGitHub()
    {
        var deliverer = CreateSut();
        var manifest = new ContentManifest
        {
            Files =
            [
                new ManifestFile { DownloadUrl = "https://example.com/file1.zip" },
                new ManifestFile { DownloadUrl = "https://github.com/user/repo/file2.zip" },
            ],
        };

        deliverer.CanDeliver(manifest).Should().BeTrue();
    }

    /// <summary>
    /// Tests that ValidateContentAsync returns success with true for valid GitHub files.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateContentAsync_ShouldReturnTrue_ForValidGitHubContentAsync()
    {
        var deliverer = CreateSut();
        var manifest = new ContentManifest
        {
            Files =
            [
                new ManifestFile { DownloadUrl = "https://github.com/user/repo/file.zip", IsRequired = true },
            ],
        };

        var result = await deliverer.ValidateContentAsync(manifest);

        result.Success.Should().BeTrue();
        result.Data.Should().BeTrue();
    }

    /// <summary>
    /// Tests that ValidateContentAsync returns false when required files have non-GitHub URLs.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateContentAsync_ShouldReturnFalse_WhenRequiredFilesHaveNonGitHubUrlsAsync()
    {
        var deliverer = CreateSut();
        var manifest = new ContentManifest
        {
            Files =
            [
                new ManifestFile { DownloadUrl = "https://other.com/file.zip", IsRequired = true },
            ],
        };

        var result = await deliverer.ValidateContentAsync(manifest);

        result.Success.Should().BeTrue();
        result.Data.Should().BeFalse();
    }

    /// <summary>
    /// Tests that SourceName returns the GitHub deliverer identifier.
    /// </summary>
    [Fact]
    public void SourceName_ShouldReturnGitHubDelivererName()
    {
        var deliverer = CreateSut();
        deliverer.SourceName.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Tests that IsEnabled returns true.
    /// </summary>
    [Fact]
    public void IsEnabled_ShouldReturnTrue()
    {
        var deliverer = CreateSut();
        deliverer.IsEnabled.Should().BeTrue();
    }

    /// <summary>
    /// Creates the system under test with the mocked dependencies.
    /// </summary>
    /// <returns>A configured <see cref="GitHubContentDeliverer"/> instance.</returns>
    private GitHubContentDeliverer CreateSut() =>
        new(_downloadService.Object, _factoryResolver, _fileHashProvider.Object, _logger.Object);

    /// <summary>
    /// Surfaces a cancellation that lands part-way through extraction as a cancellation. The
    /// downloaded archive is the only complete copy of the content, so it must survive, and the
    /// truncated file set must never reach the manifest pool.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeliverContentAsync_CancelledDuringExtraction_KeepsArchiveAndRegistersNothingAsync()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "GenHubGitHubDeliverer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDirectory);

        try
        {
            const int entryCount = 6;
            _downloadService
                .Setup(d => d.DownloadFileAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<DownloadProgress>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Uri _, string destination, string? _, IProgress<DownloadProgress>? _, CancellationToken _) =>
                {
                    CreateArchive(destination, entryCount);
                    return Task.FromResult(DownloadResult.CreateSuccess(destination, 1, TimeSpan.FromSeconds(1)));
                });

            var deliverer = CreateSut();
            var manifest = new ContentManifest
            {
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = "release.zip",
                        DownloadUrl = "https://github.com/user/repo/release.zip",
                    },
                ],
            };

            using var cancellation = new CancellationTokenSource();
            var progress = new CancelOnFirstReport(cancellation);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                deliverer.DeliverContentAsync(manifest, targetDirectory, progress, cancellation.Token));

            var archivePath = Path.Combine(targetDirectory, "release.zip");
            File.Exists(archivePath).Should().BeTrue("the archive is the only recoverable copy of the content");

            var extracted = Directory.GetFiles(targetDirectory, "entry*.dat", SearchOption.AllDirectories);
            extracted.Length.Should().BeLessThan(entryCount);
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Fails delivery when an archive understates the size it decompresses to. The lie is only
    /// visible while inflating, so the copy has to abort mid-stream, drop the partial file, and
    /// leave the truncated file set out of the manifest pool. The failure is a result, not a
    /// cancellation, so callers can tell a hostile archive from a user who changed their mind.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeliverContentAsync_ArchiveUnderstatingItsDeclaredSize_FailsWithoutRegisteringAManifestAsync()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "GenHubGitHubDeliverer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDirectory);

        try
        {
            _downloadService
                .Setup(d => d.DownloadFileAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<DownloadProgress>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Uri _, string destination, string? _, IProgress<DownloadProgress>? _, CancellationToken _) =>
                {
                    ArchiveFixtures.CreateWithSpoofedEntrySize(destination, "payload.dat", 12 * 1024 * 1024, 4096);
                    return Task.FromResult(DownloadResult.CreateSuccess(destination, 1, TimeSpan.FromSeconds(1)));
                });

            var deliverer = CreateSut();
            var manifest = new ContentManifest
            {
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = "release.zip",
                        DownloadUrl = "https://github.com/user/repo/release.zip",
                    },
                ],
            };

            var result = await deliverer.DeliverContentAsync(manifest, targetDirectory, cancellationToken: CancellationToken.None);

            result.Success.Should().BeFalse();
            result.FirstError.Should().Contain("potential zip bomb");

            File.Exists(Path.Combine(targetDirectory, "payload.dat")).Should().BeFalse("the partial output is removed");
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Refuses an entry whose key climbs out of the target directory rather than trusting the
    /// archive library to block it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchiveAsync_RejectsEntryEscapingTheTargetDirectoryAsync()
    {
        var root = CreateWorkingDirectory();

        try
        {
            var targetDirectory = Path.Combine(root, "target");
            Directory.CreateDirectory(targetDirectory);
            var archivePath = Path.Combine(root, "traversal.zip");
            CreateArchive(archivePath, "../escaped.dat");

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InvokeExtractArchiveAsync(CreateDeliverer(), archivePath, targetDirectory));

            failure.Message.Should().Contain("outside target directory");
            File.Exists(Path.Combine(root, "escaped.dat")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Refuses an entry whose name cannot name a file before that name is turned into a path,
    /// rather than letting the write fail several layers deeper with an unrelated error.
    /// </summary>
    /// <param name="entryName">The entry name the archive declares.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(".")]
    [InlineData("assets/..")]
    [InlineData(" ")]
    [InlineData("payload.dat:stream")]
    public async Task ExtractArchiveAsync_RejectsEntryWithAnUnusableNameAsync(string entryName)
    {
        var root = CreateWorkingDirectory();

        try
        {
            var targetDirectory = Path.Combine(root, "target");
            Directory.CreateDirectory(targetDirectory);
            var archivePath = Path.Combine(root, "unusable.zip");
            CreateArchive(archivePath, entryName);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InvokeExtractArchiveAsync(CreateDeliverer(), archivePath, targetDirectory));

            failure.Message.Should().Contain("cannot be extracted to a file");
            Directory.GetFileSystemEntries(root, "*.genhub-staging*").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Refuses an archive that declares more entries than the extraction budget allows, before any
    /// of them is written.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchiveAsync_RejectsArchiveOverTheEntryBudgetAsync()
    {
        var root = CreateWorkingDirectory();

        try
        {
            var targetDirectory = Path.Combine(root, "target");
            Directory.CreateDirectory(targetDirectory);
            var archivePath = Path.Combine(root, "swarm.zip");
            CreateArchive(archivePath, GitHubConstants.MaxArchiveEntries + 1);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InvokeExtractArchiveAsync(CreateDeliverer(), archivePath, targetDirectory));

            failure.Message.Should().Contain("too many entries");
            Directory.GetFileSystemEntries(targetDirectory).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateWorkingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "GenHubGitHubDeliverer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        return root;
    }

    private static async Task InvokeExtractArchiveAsync(
        GitHubContentDeliverer deliverer,
        string archivePath,
        string targetDirectory)
    {
        var extract = typeof(GitHubContentDeliverer).GetMethod(
            "ExtractArchiveAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GitHubContentDeliverer.ExtractArchiveAsync was not found.");

        await (Task)extract.Invoke(deliverer, [archivePath, targetDirectory, null, CancellationToken.None])!;
    }

    private static void CreateArchive(string archivePath, params string[] entryNames)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var entryName in entryNames)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes("payload"));
        }
    }

    private static void CreateArchive(string archivePath, int entryCount)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        for (var index = 0; index < entryCount; index++)
        {
            var entry = archive.CreateEntry($"entry{index}.dat", CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes($"payload {index}"));
        }
    }

    private GitHubContentDeliverer CreateDeliverer() =>
        new(_downloadService.Object, _manifestPool.Object, _factoryResolver.Object, _logger.Object);

    private sealed class CancelOnFirstReport(CancellationTokenSource cancellation) : IProgress<ContentAcquisitionProgress>
    {
        public void Report(ContentAcquisitionProgress value)
        {
            if (value.Phase == ContentAcquisitionPhase.Extracting)
            {
                cancellation.Cancel();
            }
        }
    }
>>>>>>> upstream/development
}
