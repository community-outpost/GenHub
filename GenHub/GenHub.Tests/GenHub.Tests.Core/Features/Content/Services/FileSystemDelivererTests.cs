using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.ContentDeliverers;
using GenHub.Tests.Core.Models.Manifest;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Unit tests for <see cref="FileSystemDeliverer"/>.
/// </summary>
public sealed class FileSystemDelivererTests
{
    private readonly Mock<IConfigurationProviderService> _configProviderMock = new();
    private readonly List<string> _builtFilePaths = [];
    private string? _builtEntryPoint;

    /// <summary>
    /// Verifies that CanDeliver returns false when the manifest has no files.
    /// </summary>
    [Fact]
    public void CanDeliver_EmptyFiles_ReturnsFalse()
    {
        var deliverer = CreateDeliverer();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameclient.zerohour"),
            Name = "Test",
            ContentType = ContentType.GameClient,
            Files = [],
        };

        var canDeliver = deliverer.CanDeliver(manifest);

        Assert.False(canDeliver);
    }

    /// <summary>
    /// Verifies that CanDeliver returns true when all files are ContentAddressable.
    /// </summary>
    [Fact]
    public void CanDeliver_ContentAddressableFiles_ReturnsTrue()
    {
        var deliverer = CreateDeliverer();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameclient.zerohour"),
            Name = "Test",
            ContentType = ContentType.GameClient,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "test.dll",
                    SourceType = ContentSourceType.ContentAddressable,
                },
            ],
        };

        var canDeliver = deliverer.CanDeliver(manifest);

        Assert.True(canDeliver);
    }

    /// <summary>
    /// Verifies that DeliverContentAsync supports string versions without throwing format errors.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_StringVersion_SucceedsAsync()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "sample content");
            _configProviderMock.Setup(c => c.GetWorkspacePath()).Returns(Path.GetDirectoryName(tempFile)!);

            var deliverer = CreateDeliverer();
            var manifest = new ContentManifest
            {
                Id = ManifestId.Create("1.0.thesuperhackers.gameclient.zerohour"),
                Name = "TheSuperHackers Zero Hour Game Code",
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                Version = "2026.07.31",
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = Path.GetFileName(tempFile),
                        SourceType = ContentSourceType.ContentAddressable,
                        SourcePath = tempFile,
                    },
                ],
            };

            var result = await deliverer.DeliverContentAsync(manifest, Path.GetDirectoryName(tempFile)!);

            Assert.True(result.Success, result.FirstError);
            Assert.NotNull(result.Data);
            Assert.Equal("2026.07.31", result.Data.Version);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// A variant manifest carries no root files, so the deliverer must judge the host
    /// variant's files rather than reject the manifest as empty.
    /// </summary>
    [Fact]
    public void CanDeliver_VariantManifest_UsesHostVariantFiles()
    {
        var deliverer = CreateDeliverer();
        var manifest = VariantManifestFixture.Create(
            [new ManifestFile { RelativePath = "host.bin", SourceType = ContentSourceType.ContentAddressable }],
            [new ManifestFile { RelativePath = "foreign.bin", SourceType = ContentSourceType.RemoteDownload }]);

        Assert.True(deliverer.CanDeliver(manifest));
    }

    /// <summary>
    /// Delivering a variant manifest delivers exactly the host variant's files. Only the
    /// host files exist on disk, so touching the foreign variant would fail delivery.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_VariantManifest_DeliversHostVariantFilesAsync()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var hostFiles = new List<ManifestFile>
            {
                CreateLocalFile(directory.FullName, "generalszh"),
                CreateLocalFile(directory.FullName, "libSDL3.dylib"),
            };
            var foreignFiles = new List<ManifestFile>
            {
                new() { RelativePath = "generalszh.exe", SourceType = ContentSourceType.ContentAddressable, SourcePath = Path.Combine(directory.FullName, "generalszh.exe") },
            };
            _configProviderMock.Setup(c => c.GetWorkspacePath()).Returns(directory.FullName);

            var manifest = VariantManifestFixture.Create(hostFiles, foreignFiles);
            manifest.Variants[0].EntryPoint = "generalszh.exe";
            manifest.Variants[1].EntryPoint = "generalszh";

            var result = await CreateDeliverer().DeliverContentAsync(manifest, directory.FullName);

            Assert.True(result.Success, result.FirstError);
            Assert.Equal(["generalszh", "libSDL3.dylib"], _builtFilePaths);
            Assert.Equal("generalszh", _builtEntryPoint);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A flat manifest still delivers its root files unchanged.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_FlatManifest_DeliversRootFilesAsync()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            _configProviderMock.Setup(c => c.GetWorkspacePath()).Returns(directory.FullName);
            var manifest = new ContentManifest
            {
                Id = ManifestId.Create("1.0.test.gameclient.zerohour"),
                Name = "Test",
                ContentType = ContentType.GameClient,
                Files = [CreateLocalFile(directory.FullName, "a.big"), CreateLocalFile(directory.FullName, "b.big")],
            };

            var result = await CreateDeliverer().DeliverContentAsync(manifest, directory.FullName);

            Assert.True(result.Success, result.FirstError);
            Assert.Equal(["a.big", "b.big"], _builtFilePaths);
            Assert.Null(_builtEntryPoint);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Validation of a variant manifest checks the host variant's required files, so a
    /// missing host file is reported instead of passing over an empty root list.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateContentAsync_VariantManifest_ChecksHostVariantFilesAsync()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            _configProviderMock.Setup(c => c.GetWorkspacePath()).Returns(directory.FullName);
            var manifest = VariantManifestFixture.Create(
                [new ManifestFile { RelativePath = "missing-host.bin", IsRequired = true, SourceType = ContentSourceType.ContentAddressable }],
                [CreateLocalFile(directory.FullName, "present-foreign.bin")]);

            var result = await CreateDeliverer().ValidateContentAsync(manifest);

            Assert.True(result.Success, result.FirstError);
            Assert.False(result.Data);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Files of a variant for another platform are not required on this host.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateContentAsync_VariantManifest_IgnoresForeignVariantFilesAsync()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            _configProviderMock.Setup(c => c.GetWorkspacePath()).Returns(directory.FullName);
            var manifest = VariantManifestFixture.Create(
                [CreateLocalFile(directory.FullName, "present-host.bin")],
                [new ManifestFile { RelativePath = "missing-foreign.bin", IsRequired = true, SourceType = ContentSourceType.ContentAddressable }]);

            var result = await CreateDeliverer().ValidateContentAsync(manifest);

            Assert.True(result.Success, result.FirstError);
            Assert.True(result.Data);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A variant manifest with no variant for this host fails delivery and validation
    /// instead of succeeding with nothing delivered.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task VariantManifest_WithoutHostVariant_FailsDeliveryAndValidationAsync()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            _configProviderMock.Setup(c => c.GetWorkspacePath()).Returns(directory.FullName);
            var manifest = VariantManifestFixture.Create([], [CreateLocalFile(directory.FullName, "foreign.bin")]);
            manifest.Variants.RemoveAt(1);
            var deliverer = CreateDeliverer();

            var result = await deliverer.DeliverContentAsync(manifest, directory.FullName);
            var validation = await deliverer.ValidateContentAsync(manifest);

            Assert.False(result.Success);
            Assert.Empty(_builtFilePaths);
            Assert.False(validation.Data);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static ManifestFile CreateLocalFile(string directory, string relativePath)
    {
        var path = Path.Combine(directory, relativePath);
        File.WriteAllText(path, relativePath);
        return new ManifestFile
        {
            RelativePath = relativePath,
            SourceType = ContentSourceType.ContentAddressable,
            SourcePath = path,
            IsRequired = true,
        };
    }

    private FileSystemDeliverer CreateDeliverer(IContentManifestBuilder? manifestBuilder = null)
    {
        var builderMock = new Mock<IContentManifestBuilder>();
        string? capturedVersion = null;
        builderMock
            .Setup(b => b.WithBasicInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Callback<string, string, string?>((_, _, version) => capturedVersion = version)
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithContentType(It.IsAny<ContentType>(), It.IsAny<GameType>())).Returns(builderMock.Object);
        builderMock.Setup(b => b.WithPublisher(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(builderMock.Object);
        builderMock.Setup(b => b.WithMetadata(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>())).Returns(builderMock.Object);
        builderMock
            .Setup(b => b.AddContentAddressableFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<FilePermissions?>()))
            .Callback<string, string, long, bool, FilePermissions?>((relativePath, _, _, _, _) => _builtFilePaths.Add(relativePath))
            .ReturnsAsync(builderMock.Object);
        builderMock
            .Setup(b => b.WithEntryPoint(It.IsAny<string?>()))
            .Callback<string?>(entryPoint => _builtEntryPoint = entryPoint)
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.AddRequiredDirectories(It.IsAny<string[]>())).Returns(builderMock.Object);
        builderMock.Setup(b => b.Build()).Returns(() => new ContentManifest
        {
            Id = ManifestId.Create("1.0.thesuperhackers.gameclient.zerohour"),
            Name = "TheSuperHackers Zero Hour Game Code",
            Version = capturedVersion ?? "1.0",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        });

        return new FileSystemDeliverer(
            NullLogger<FileSystemDeliverer>.Instance,
            _configProviderMock.Object,
            () => manifestBuilder ?? builderMock.Object);
    }
}
