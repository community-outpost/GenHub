using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Results;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests for <see cref="UnixFileOperationsService"/>.
/// <para>
/// These assert that a hard link is a hard link. The previous test asserted only
/// <c>File.Exists</c> and matching content, which a plain copy satisfies — which is
/// exactly why nobody noticed that Unix had been copying instead of linking.
/// </para>
/// </summary>
public class UnixFileOperationsServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"genhub-unixfileops-{Guid.NewGuid():N}");

    private readonly Mock<ICasService> _casServiceMock = new();
    private readonly UnixFileOperationsService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnixFileOperationsServiceTests"/> class.
    /// </summary>
    public UnixFileOperationsServiceTests()
    {
        Directory.CreateDirectory(_tempDir);

        var baseService = new FileOperationsService(
            NullLogger<FileOperationsService>.Instance,
            new Mock<IDownloadService>().Object,
            new Mock<ICasService>().Object);

        _service = new UnixFileOperationsService(
            baseService,
            _casServiceMock.Object,
            NullLogger<UnixFileOperationsService>.Instance);
    }

    private static bool OnUnix => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// The link and its target must be the same inode with a link count of two. Content
    /// equality is not sufficient evidence: a copy has identical content and a distinct
    /// inode, and that is the failure mode this test exists to detect.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateHardLinkAsync_ProducesRealLinkNotCopy()
    {
        if (!OnUnix)
        {
            return;
        }

        var source = Path.Combine(_tempDir, "source.dat");
        var link = Path.Combine(_tempDir, "link.dat");
        await File.WriteAllTextAsync(source, "payload");

        await _service.CreateHardLinkAsync(link, source);

        Assert.True(File.Exists(link));

        var sourceInfo = new FileInfo(source);
        var linkInfo = new FileInfo(link);

        // UnixFileMode alone would not distinguish a copy; the identity check is the point.
        Assert.Equal(sourceInfo.Length, linkInfo.Length);

        // Mutating through one path must be visible through the other. That is only true
        // for a shared inode, so it distinguishes a link from a copy without needing stat.
        await File.WriteAllTextAsync(source, "mutated through the source path");
        Assert.Equal("mutated through the source path", await File.ReadAllTextAsync(link));

        // And the reverse direction, to rule out a coincidence of ordering.
        await File.WriteAllTextAsync(link, "mutated through the link path");
        Assert.Equal("mutated through the link path", await File.ReadAllTextAsync(source));
    }

    /// <summary>
    /// A missing target must raise a diagnosable error naming the file, rather than the
    /// bare "errno 2" that an uninterpreted P/Invoke failure would produce.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateHardLinkAsync_MissingTarget_ThrowsFileNotFound()
    {
        if (!OnUnix)
        {
            return;
        }

        var missing = Path.Combine(_tempDir, "does-not-exist.dat");
        var link = Path.Combine(_tempDir, "link.dat");

        var thrown = await Record.ExceptionAsync(() => _service.CreateHardLinkAsync(link, missing));

        Assert.IsType<FileNotFoundException>(thrown);
        Assert.Contains("does-not-exist.dat", thrown.Message);
    }

    /// <summary>
    /// Linking over an existing file replaces it, matching the Windows implementation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateHardLinkAsync_ExistingDestination_IsReplaced()
    {
        if (!OnUnix)
        {
            return;
        }

        var source = Path.Combine(_tempDir, "source.dat");
        var link = Path.Combine(_tempDir, "link.dat");
        await File.WriteAllTextAsync(source, "new content");
        await File.WriteAllTextAsync(link, "stale content");

        await _service.CreateHardLinkAsync(link, source);

        Assert.Equal("new content", await File.ReadAllTextAsync(link));
    }

    /// <summary>
    /// Copying from CAS must create an independent inode. Full-copy and hybrid callers
    /// are allowed to modify their destination without changing the shared CAS blob.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyFromCasAsync_ProducesIndependentCopy()
    {
        var casBlob = Path.Combine(_tempDir, "cas-copy-source.dat");
        var copy = Path.Combine(_tempDir, "cas-copy-destination.dat");
        await File.WriteAllTextAsync(casBlob, "shared content");

        _casServiceMock
            .Setup(service => service.GetContentPathAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(casBlob));

        Assert.True(await _service.CopyFromCasAsync("hash", copy));

        await File.WriteAllTextAsync(copy, "workspace content");

        Assert.Equal("shared content", await File.ReadAllTextAsync(casBlob));
        Assert.Equal("workspace content", await File.ReadAllTextAsync(copy));
    }

    /// <summary>
    /// The base implementation must refuse rather than quietly copy. Silently copying is
    /// what hid the missing Unix registration for as long as it existed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BaseService_CreateHardLinkAsync_ThrowsRatherThanCopying()
    {
        var baseService = new FileOperationsService(
            NullLogger<FileOperationsService>.Instance,
            new Mock<IDownloadService>().Object,
            new Mock<ICasService>().Object);

        var source = Path.Combine(_tempDir, "base-source.dat");
        var link = Path.Combine(_tempDir, "base-link.dat");
        await File.WriteAllTextAsync(source, "payload");

        var thrown = await Record.ExceptionAsync(() => baseService.CreateHardLinkAsync(link, source));

        Assert.IsType<NotSupportedException>(thrown);
        Assert.False(File.Exists(link), "The base service copied the file instead of refusing.");
    }

    /// <summary>
    /// Releases the temporary directory.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
