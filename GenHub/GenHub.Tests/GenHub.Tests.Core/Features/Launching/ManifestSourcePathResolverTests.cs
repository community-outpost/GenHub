using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for <see cref="ManifestSourcePathResolver"/>.
/// </summary>
public sealed class ManifestSourcePathResolverTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"GenHub-ManifestSourcePathTests-{Guid.NewGuid():N}");

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestSourcePathResolverTests"/> class.
    /// </summary>
    public ManifestSourcePathResolverTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// A lone Flatpak bundle staged outside the game directory resolves its source from
    /// the manifest pool instead of the profile working directory, so the workspace can
    /// materialize the bundle the provisioner installs at launch.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task FlatpakBundleOutsideWorkingDirectory_ResolvesFromPoolAsync()
    {
        // Arrange
        var workingDirectory = CreateDirectory("game");
        var stagingDirectory = CreateDirectory("staging");
        File.WriteAllText(Path.Combine(stagingDirectory, "Linux-GeneralsXZH.flatpak"), "bundle");
        var manifest = new ContentManifest
        {
            Id = "1.100.fbraz3.gameclient.generalsxlinuxgeneralsxzh",
            ContentType = ContentType.GameClient,
            Files = [new ManifestFile { RelativePath = "Linux-GeneralsXZH.flatpak" }],
        };
        var profile = new GameProfile
        {
            GameClient = new GameClient { WorkingDirectory = workingDirectory },
        };
        var poolMock = new Mock<IContentManifestPool>();
        poolMock
            .Setup(x => x.GetContentDirectoryAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string?>.CreateSuccess(stagingDirectory));

        // Act
        var paths = await ManifestSourcePathResolver.ResolveManifestSourcePathsAsync(
            [manifest],
            profile,
            poolMock.Object,
            NullLogger.Instance,
            CancellationToken.None);

        // Assert
        Assert.True(paths.TryGetValue(manifest.Id.Value, out var sourcePath));
        Assert.Equal(stagingDirectory, sourcePath);
    }

    /// <summary>
    /// Ordinary game clients keep resolving from the profile working directory, so the
    /// Flatpak diversion cannot reroute a retail install that was already working.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task WindowsClientInWorkingDirectory_ResolvesFromWorkingDirectoryAsync()
    {
        // Arrange
        var workingDirectory = CreateDirectory("game");
        File.WriteAllText(Path.Combine(workingDirectory, "generals.exe"), "MZ");
        var manifest = new ContentManifest
        {
            Id = "1.105.retail.gameclient.generals",
            ContentType = ContentType.GameClient,
            Files = [new ManifestFile { RelativePath = "generals.exe", IsExecutable = true }],
        };
        var profile = new GameProfile
        {
            GameClient = new GameClient { WorkingDirectory = workingDirectory },
        };
        var poolMock = new Mock<IContentManifestPool>(MockBehavior.Strict);

        // Act
        var paths = await ManifestSourcePathResolver.ResolveManifestSourcePathsAsync(
            [manifest],
            profile,
            poolMock.Object,
            NullLogger.Instance,
            CancellationToken.None);

        // Assert
        Assert.True(paths.TryGetValue(manifest.Id.Value, out var sourcePath));
        Assert.Equal(workingDirectory, sourcePath);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_tempDirectory, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
