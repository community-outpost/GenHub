using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Content;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Contains tests for <see cref="LocalContentService"/>.
/// </summary>
public class LocalContentServiceTests : IDisposable
{
    private readonly Mock<IManifestGenerationService> _manifestGenServiceMock;
    private readonly Mock<IContentStorageService> _contentStorageServiceMock;
    private readonly Mock<IContentReconciliationService> _reconciliationServiceMock;
    private readonly LocalContentService _service;
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalContentServiceTests"/> class.
    /// </summary>
    public LocalContentServiceTests()
    {
        _manifestGenServiceMock = new Mock<IManifestGenerationService>();
        _contentStorageServiceMock = new Mock<IContentStorageService>();
        _reconciliationServiceMock = new Mock<IContentReconciliationService>();

        _service = new LocalContentService(
            _manifestGenServiceMock.Object,
            _contentStorageServiceMock.Object,
            _reconciliationServiceMock.Object,
            NullLogger<LocalContentService>.Instance);

        _tempDir = Path.Combine(Path.GetTempPath(), "LocalContentServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Cleans up temporary resources.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that CreateLocalContentManifestAsync sets EntryPoint when provided.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateLocalContentManifestAsync_WithEntryPoint_SetsManifestEntryPoint()
    {
        SetupManifestBuilder(ContentType.ModdingTool, GameType.ZeroHour, "FinalBIG", "FinalBIG.exe");

        var result = await _service.CreateLocalContentManifestAsync(
            directoryPath: _tempDir,
            name: "FinalBIG",
            contentType: ContentType.ModdingTool,
            targetGame: GameType.ZeroHour,
            entryPoint: "FinalBIG.exe");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("FinalBIG.exe", result.Data!.EntryPoint);
    }

    /// <summary>
    /// Verifies that CreateLocalContentManifestAsync normalizes backslashes to forward slashes in EntryPoint.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateLocalContentManifestAsync_NormalizesBackslashesInEntryPoint()
    {
        SetupManifestBuilder(ContentType.Executable, GameType.ZeroHour, "Tool", "bin/sub/tool.exe");

        var result = await _service.CreateLocalContentManifestAsync(
            directoryPath: _tempDir,
            name: "Tool",
            contentType: ContentType.Executable,
            targetGame: GameType.ZeroHour,
            entryPoint: "bin\\sub\\tool.exe");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("bin/sub/tool.exe", result.Data!.EntryPoint);
    }

    /// <summary>
    /// Verifies that CreateLocalContentManifestAsync leaves EntryPoint null when passed a whitespace-only value.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateLocalContentManifestAsync_WithWhitespaceOnlyEntryPoint_LeavesEntryPointNull()
    {
        SetupManifestBuilder(ContentType.ModdingTool, GameType.ZeroHour, "FinalBIG", "FinalBIG.exe");

        var result = await _service.CreateLocalContentManifestAsync(
            directoryPath: _tempDir,
            name: "FinalBIG",
            contentType: ContentType.ModdingTool,
            targetGame: GameType.ZeroHour,
            entryPoint: "   ");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Null(result.Data!.EntryPoint);
    }

    /// <summary>
    /// Verifies that CreateLocalContentManifestAsync rejects rooted or parent-traversal entry points.
    /// </summary>
    /// <param name="invalidEntryPoint">The invalid entry point path to test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("/usr/bin/tool.exe")]
    [InlineData("../tool.exe")]
    [InlineData("bin/../../tool.exe")]
    public async Task CreateLocalContentManifestAsync_WithInvalidEntryPointPath_ReturnsFailure(string invalidEntryPoint)
    {
        SetupManifestBuilder(ContentType.Executable, GameType.ZeroHour, "Tool", "tool.exe");

        var result = await _service.CreateLocalContentManifestAsync(
            directoryPath: _tempDir,
            name: "Tool",
            contentType: ContentType.Executable,
            targetGame: GameType.ZeroHour,
            entryPoint: invalidEntryPoint);

        Assert.False(result.Success);
        Assert.Contains("invalid", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that CreateLocalContentManifestAsync rejects an entry point that does not exist in manifest files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateLocalContentManifestAsync_WithNonExistentEntryPoint_ReturnsFailure()
    {
        SetupManifestBuilder(ContentType.Executable, GameType.ZeroHour, "Tool", "tool.exe");

        var result = await _service.CreateLocalContentManifestAsync(
            directoryPath: _tempDir,
            name: "Tool",
            contentType: ContentType.Executable,
            targetGame: GameType.ZeroHour,
            entryPoint: "missing.exe");

        Assert.False(result.Success);
        Assert.Contains("not found", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that CreateLocalContentManifestAsync leaves EntryPoint null when not provided.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateLocalContentManifestAsync_WithoutEntryPoint_LeavesEntryPointNull()
    {
        SetupManifestBuilder(ContentType.Mod, GameType.ZeroHour, "MyMod");

        var result = await _service.CreateLocalContentManifestAsync(
            directoryPath: _tempDir,
            name: "MyMod",
            contentType: ContentType.Mod,
            targetGame: GameType.ZeroHour);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Null(result.Data!.EntryPoint);
    }

    /// <summary>
    /// Verifies that UpdateLocalContentManifestAsync passes entryPoint through to the created manifest.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UpdateLocalContentManifestAsync_WithEntryPoint_SetsEntryPointOnUpdatedManifest()
    {
        SetupManifestBuilder(ContentType.GameClient, GameType.ZeroHour, "GeneralsClient", "generals.exe");

        _reconciliationServiceMock
            .Setup(x => x.OrchestrateLocalUpdateAsync(
                It.IsAny<string>(),
                It.IsAny<ContentManifest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentUpdateResult>.CreateSuccess(new ContentUpdateResult()));

        var result = await _service.UpdateLocalContentManifestAsync(
            existingManifestId: "1.0.local.gameclient.old",
            name: "GeneralsClient",
            directoryPath: _tempDir,
            contentType: ContentType.GameClient,
            targetGame: GameType.ZeroHour,
            entryPoint: "generals.exe");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("generals.exe", result.Data!.EntryPoint);
    }

    private void SetupManifestBuilder(ContentType contentType, GameType targetGame, string contentName, params string[] filePaths)
    {
        var files = filePaths.Length > 0
            ? filePaths.Select(f => new ManifestFile { RelativePath = f, IsExecutable = f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) }).ToList()
            : new List<ManifestFile>();

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create($"1.0.local.{contentType.ToString().ToLowerInvariant()}.{contentName.ToLowerInvariant()}"),
            Name = contentName,
            ContentType = contentType,
            TargetGame = targetGame,
            Files = files,
        };

        var builderMock = new Mock<IContentManifestBuilder>();
        builderMock.Setup(b => b.Build()).Returns(manifest);

        _manifestGenServiceMock
            .Setup(x => x.CreateContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<ContentType>(),
                It.IsAny<GameType>(),
                It.IsAny<ContentDependency[]>()))
            .ReturnsAsync(builderMock.Object);

        _contentStorageServiceMock
            .Setup(x => x.StoreContentAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));
    }
}
