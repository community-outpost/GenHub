using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests that cancellation during the hard-link fallback propagates as cancellation instead of
/// falling through to a copy or being reported as a file failure.
/// </summary>
public sealed class WorkspaceFallbackCancellationTests : IDisposable
{
    // Exceeds the small-file threshold so hybrid exercises its link fallback.
    private const long NonEssentialFileSize = 5 * 1024 * 1024;

    private const string RelativePath = "Movies/intro.bik";

    private readonly Mock<IFileOperationsService> _fileOperations = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// When a symlink is denied and the hard-link fallback is cancelled, preparation throws
    /// <see cref="OperationCanceledException"/> and never attempts the copy fallback.
    /// </summary>
    /// <param name="strategyType">The strategy under test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Theory]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.HardLink)]
    public async Task PrepareAsync_WhenHardLinkFallbackIsCancelled_PropagatesCancellationAsync(WorkspaceStrategy strategyType)
    {
        var installDir = Path.Combine(_root, "Install");
        Directory.CreateDirectory(Path.Combine(installDir, "Movies"));
        var sourcePath = Path.Combine(installDir, "Movies", "intro.bik");
        File.WriteAllText(sourcePath, "video");
        _fileOperations
            .Setup(f => f.CreateSymlinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("symlink denied"));
        _fileOperations
            .Setup(f => f.CreateHardLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var configuration = new WorkspaceConfiguration
        {
            Id = Guid.NewGuid().ToString("N"),
            Strategy = strategyType,
            WorkspaceRootPath = Path.Combine(_root, "Workspaces"),
            BaseInstallationPath = installDir,
            GameClient = new GameClient { Id = "test" },
            Manifests =
            [
                new ContentManifest
                {
                    Files = [new() { RelativePath = RelativePath, SourcePath = sourcePath, Size = NonEssentialFileSize, SourceType = ContentSourceType.GameInstallation }],
                },
            ],
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateStrategy(strategyType).PrepareAsync(configuration, null, CancellationToken.None));
        _fileOperations.Verify(
            f => f.CopyFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Hybrid's local-file path has its own hard-link fallback. When that fallback is cancelled,
    /// cancellation propagates and the copy fallback is not attempted.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HybridProcessLocalFileAsync_WhenHardLinkFallbackIsCancelled_PropagatesCancellationAsync()
    {
        var installDir = Path.Combine(_root, "Install");
        Directory.CreateDirectory(Path.Combine(installDir, "Movies"));
        var sourcePath = Path.Combine(installDir, "Movies", "intro.bik");
        File.WriteAllText(sourcePath, "video");
        _fileOperations
            .Setup(f => f.CreateSymlinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("symlink denied"));
        _fileOperations
            .Setup(f => f.CreateHardLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var file = new ManifestFile { RelativePath = RelativePath, SourcePath = sourcePath, Size = NonEssentialFileSize, SourceType = ContentSourceType.LocalFile };
        var configuration = new WorkspaceConfiguration { BaseInstallationPath = installDir };
        var strategy = new HybridCopySymlinkStrategy(_fileOperations.Object, new Mock<ILogger<HybridCopySymlinkStrategy>>().Object);
        var method = typeof(HybridCopySymlinkStrategy).GetMethod("ProcessLocalFileAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => (Task)method.Invoke(
            strategy,
            [file, new ContentManifest { Files = [file] }, Path.Combine(_root, "Workspace", RelativePath), configuration, CancellationToken.None])!);
        _fileOperations.Verify(
            f => f.CopyFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Cancellation from the CAS service must bypass every strategy's retry and failure wrappers.
    /// </summary>
    /// <param name="strategyType">The strategy under test.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.HardLink)]
    [InlineData(WorkspaceStrategy.FullCopy)]
    public async Task PrepareAsync_WhenCasOperationIsCancelled_DoesNotRetryAsync(WorkspaceStrategy strategyType)
    {
        Directory.CreateDirectory(_root);
        _fileOperations.Setup(f => f.LinkFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        _fileOperations.Setup(f => f.CopyFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var configuration = new WorkspaceConfiguration
        {
            Id = Guid.NewGuid().ToString("N"),
            Strategy = strategyType,
            WorkspaceRootPath = Path.Combine(_root, "Workspaces"),
            BaseInstallationPath = _root,
            GameClient = new GameClient { Id = "test" },
            Manifests =
            [
                new ContentManifest
                {
                    Files = [new() { RelativePath = RelativePath, Hash = "hash", Size = NonEssentialFileSize, SourceType = ContentSourceType.ContentAddressable }],
                },
            ],
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateStrategy(strategyType).PrepareAsync(configuration, null, CancellationToken.None));
        Assert.Single(_fileOperations.Invocations, i => i.Method.Name is "LinkFromCasAsync" or "CopyFromCasAsync");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // Ignore cleanup errors
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore cleanup errors
        }
    }

    private IWorkspaceStrategy CreateStrategy(WorkspaceStrategy strategyType) => strategyType switch
    {
        WorkspaceStrategy.HybridCopySymlink => new HybridCopySymlinkStrategy(_fileOperations.Object, new Mock<ILogger<HybridCopySymlinkStrategy>>().Object),
        WorkspaceStrategy.HardLink => new HardLinkStrategy(_fileOperations.Object, new Mock<ILogger<HardLinkStrategy>>().Object),
        WorkspaceStrategy.FullCopy => new FullCopyStrategy(_fileOperations.Object, new Mock<ILogger<FullCopyStrategy>>().Object),
        WorkspaceStrategy.SymlinkOnly => new SymlinkOnlyStrategy(_fileOperations.Object, new Mock<ILogger<SymlinkOnlyStrategy>>().Object),
        _ => throw new ArgumentException($"Unknown strategy type: {strategyType}"),
    };
}
