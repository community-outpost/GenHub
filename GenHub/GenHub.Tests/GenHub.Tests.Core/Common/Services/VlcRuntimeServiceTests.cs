using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Unit tests for <see cref="VlcRuntimeService"/>.
/// </summary>
public sealed class VlcRuntimeServiceTests : IDisposable
{
    private readonly string testTargetDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="VlcRuntimeServiceTests"/> class.
    /// </summary>
    public VlcRuntimeServiceTests()
    {
        testTargetDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", "VlcRuntimeServiceTests", Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testTargetDirectory))
            {
                Directory.Delete(testTargetDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup exceptions
        }
    }

    /// <summary>
    /// Verifies that IsAvailable returns false and status is NotInstalled when directory does not exist on Windows and no system VLC is configured.
    /// </summary>
    [Fact]
    public void IsAvailable_NonExistentDirectory_ReturnsFalseWhenNotInstalled()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new VlcRuntimeService(httpClient, NullLogger<VlcRuntimeService>.Instance, testTargetDirectory, systemVlcDirectory: null);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.False(service.IsAvailable());
            Assert.Equal(VlcRuntimeStatus.NotInstalled, service.Status);
        }
        else
        {
            // Non-Windows discovers system VLC dynamically
            Assert.True(service.IsAvailable());
            Assert.Equal(VlcRuntimeStatus.Available, service.Status);
        }
    }

    /// <summary>
    /// Verifies that IsAvailable detects system VLC when DLLs exist in the specified system VLC directory.
    /// </summary>
    [Fact]
    public void IsAvailable_SystemVlcPresent_ReturnsTrue()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var mockSystemVlcDir = Path.Combine(testTargetDirectory, "mock-vlc");
        Directory.CreateDirectory(mockSystemVlcDir);
        File.WriteAllText(Path.Combine(mockSystemVlcDir, "libvlc.dll"), "fake-libvlc");
        File.WriteAllText(Path.Combine(mockSystemVlcDir, "libvlccore.dll"), "fake-vlccore");

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new VlcRuntimeService(
            httpClient,
            NullLogger<VlcRuntimeService>.Instance,
            Path.Combine(testTargetDirectory, "target"),
            systemVlcDirectory: mockSystemVlcDir);

        Assert.True(service.IsAvailable());
        Assert.Equal(VlcRuntimeStatus.Available, service.Status);
        Assert.Equal(mockSystemVlcDir, service.RuntimeDirectory);
    }

    /// <summary>
    /// Verifies that InstallRuntimeAsync correctly extracts x64 binaries from a nupkg archive and promotes them.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallRuntimeAsync_ValidZipArchive_ExtractsAndSetsAvailableAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var zipBytes = CreateSampleNupkgArchive(
            ("build/x64/libvlc.dll", "mock-libvlc-content"),
            ("build/x64/libvlccore.dll", "mock-libvlccore-content"),
            ("build/x64/plugins/access/libaccess_http_plugin.dll", "mock-plugin-content"),
            ("build/win-x86/libvlc.dll", "should-not-extract-x86"),
            ("build/x64/include/vlc/vlc.h", "should-not-extract-headers"));

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var service = new VlcRuntimeService(httpClient, NullLogger<VlcRuntimeService>.Instance, testTargetDirectory, systemVlcDirectory: null, expectedSha512: null);

        var progressReported = false;
        var progress = new Progress<double>(p =>
        {
            if (p > 0)
            {
                progressReported = true;
            }
        });

        var result = await service.InstallRuntimeAsync(progress);

        Assert.True(result);
        Assert.True(progressReported);
        Assert.Equal(VlcRuntimeStatus.Available, service.Status);
        Assert.True(service.IsAvailable());
        Assert.Equal(testTargetDirectory, service.RuntimeDirectory);

        // Verify extracted files
        Assert.True(File.Exists(Path.Combine(testTargetDirectory, "libvlc.dll")));
        Assert.True(File.Exists(Path.Combine(testTargetDirectory, "libvlccore.dll")));
        Assert.True(File.Exists(Path.Combine(testTargetDirectory, "plugins", "access", "libaccess_http_plugin.dll")));

        // Verify non-target files were excluded
        Assert.False(File.Exists(Path.Combine(testTargetDirectory, "vlc.h")));
        Assert.False(Directory.Exists(Path.Combine(testTargetDirectory, "include")));
        Assert.False(Directory.Exists(Path.Combine(testTargetDirectory, "win-x86")));
    }

    /// <summary>
    /// Verifies that InstallRuntimeAsync replaces an existing runtime directory atomically.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallRuntimeAsync_ExistingDirectory_ReplacesAtomicallyAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        Directory.CreateDirectory(testTargetDirectory);
        File.WriteAllText(Path.Combine(testTargetDirectory, "old_file.txt"), "stale content");

        var zipBytes = CreateSampleNupkgArchive(
            ("build/x64/libvlc.dll", "new-libvlc"),
            ("build/x64/libvlccore.dll", "new-libvlccore"));

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var service = new VlcRuntimeService(httpClient, NullLogger<VlcRuntimeService>.Instance, testTargetDirectory, systemVlcDirectory: null, expectedSha512: null);

        var result = await service.InstallRuntimeAsync();

        Assert.True(result);
        Assert.Equal(VlcRuntimeStatus.Available, service.Status);
        Assert.True(File.Exists(Path.Combine(testTargetDirectory, "libvlc.dll")));
        Assert.True(File.Exists(Path.Combine(testTargetDirectory, "libvlccore.dll")));
        Assert.False(File.Exists(Path.Combine(testTargetDirectory, "old_file.txt")));
    }

    /// <summary>
    /// Verifies that InstallRuntimeAsync rejects downloads whose SHA-512 does not match the expected hash.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallRuntimeAsync_Sha512Mismatch_RejectsAndReturnsFalseAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var zipBytes = CreateSampleNupkgArchive(
            ("build/x64/libvlc.dll", "mock-libvlc"),
            ("build/x64/libvlccore.dll", "mock-libvlccore"));

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            });

        var httpClient = new HttpClient(mockHandler.Object);
        const string wrongSha512 = "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";
        var service = new VlcRuntimeService(httpClient, NullLogger<VlcRuntimeService>.Instance, testTargetDirectory, systemVlcDirectory: null, expectedSha512: wrongSha512);

        var result = await service.InstallRuntimeAsync();

        Assert.False(result);
        Assert.Equal(VlcRuntimeStatus.Failed, service.Status);
        Assert.False(Directory.Exists(testTargetDirectory));
    }

    /// <summary>
    /// Verifies that InstallRuntimeAsync safely rejects zip archives containing zip-slip directory traversal paths.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallRuntimeAsync_ZipSlipArchive_RejectsSafelyAndReturnsFalseAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var zipBytes = CreateSampleNupkgArchive(
            ("build/x64/../../escape.dll", "malicious-escape-content"));

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var service = new VlcRuntimeService(httpClient, NullLogger<VlcRuntimeService>.Instance, testTargetDirectory, systemVlcDirectory: null, expectedSha512: null);

        var result = await service.InstallRuntimeAsync();

        Assert.False(result);
        Assert.Equal(VlcRuntimeStatus.Failed, service.Status);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escape.dll")));
    }

    /// <summary>
    /// Verifies that InstallRuntimeAsync honors cancellation token.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallRuntimeAsync_Cancelled_ThrowsOperationCanceledExceptionAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new VlcRuntimeService(httpClient, NullLogger<VlcRuntimeService>.Instance, testTargetDirectory, systemVlcDirectory: null, expectedSha512: null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InstallRuntimeAsync(cancellationToken: cts.Token));
    }

    private static byte[] CreateSampleNupkgArchive(params (string EntryName, string Content)[] entries)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return memoryStream.ToArray();
    }
}
