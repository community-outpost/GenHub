using GenHub.Core.Constants;
using GenHub.Features.Content.Services.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Tools;

/// <summary>
/// Tests the app-owned on-demand Playwright driver provisioning.
/// </summary>
[Collection(PlaywrightEnvironmentCollection.Name)]
public sealed class ManagedPlaywrightDriverTests : IDisposable
{
    private static readonly string[] AllPlatformFolders =
    [
        "win32_x64",
        "linux-x64",
        "linux-arm64",
        "darwin-x64",
        "darwin-arm64",
    ];

    private readonly string _driverDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
    private readonly string? _originalDriverPath = Environment.GetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable);

    /// <summary>
    /// Verifies a provisioned managed driver is reused without consent or download.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_ValidManagedDriver_ReusesWithoutDownloadAsync()
    {
        // Arrange
        var driver = CreateDriver(out var consentCalls, out var downloadCalls);
        SeedManagedDriver(ModDBConstants.PlaywrightDriverVersion);

        // Act
        await driver.EnsureInstalledAsync(default);

        // Assert
        Assert.Empty(consentCalls);
        Assert.Empty(downloadCalls);
        Assert.Equal(
            _driverDirectory,
            Environment.GetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable));
    }

    /// <summary>
    /// Verifies a driver visible through the search path (shipped or pre-provisioned)
    /// is used without consent or download.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_ShippedDriverPresent_SkipsProvisioningAsync()
    {
        // Arrange
        var shippedRoot = Path.Combine(_driverDirectory, "shipped");
        var shippedNode = Path.Combine(shippedRoot, ".playwright", "node", ManagedChromiumRuntime.GetDriverPlatformFolder(), ManagedChromiumRuntime.GetDriverNodeBinaryName());
        Directory.CreateDirectory(Path.GetDirectoryName(shippedNode)!);
        await File.WriteAllTextAsync(shippedNode, "node");
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable, shippedRoot);
        var driver = CreateDriver(out var consentCalls, out var downloadCalls);

        // Act
        await driver.EnsureInstalledAsync(default);

        // Assert
        Assert.Empty(consentCalls);
        Assert.Empty(downloadCalls);
        Assert.Equal(shippedRoot, Environment.GetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable));
    }

    /// <summary>
    /// Verifies a missing driver triggers consent, downloads the pinned package, and
    /// extracts only the current platform slice plus the shared driver package.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_MissingDriver_DownloadsAndExtractsPlatformSliceAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable, null);
        var driver = CreateDriver(out var consentCalls, out var downloadCalls);

        // Act
        await driver.EnsureInstalledAsync(default);

        // Assert
        Assert.Equal([_driverDirectory], consentCalls);
        var downloadUri = Assert.Single(downloadCalls);
        Assert.Equal(
            ApiConstants.GetNuGetPackageDownloadUrl(ModDBConstants.PlaywrightDriverPackageId, ModDBConstants.PlaywrightDriverVersion),
            downloadUri);
        var platformFolder = ManagedChromiumRuntime.GetDriverPlatformFolder();
        var nodeBinaryName = ManagedChromiumRuntime.GetDriverNodeBinaryName();
        Assert.True(File.Exists(Path.Combine(_driverDirectory, ".playwright", "node", platformFolder, nodeBinaryName)));
        Assert.True(File.Exists(Path.Combine(_driverDirectory, ".playwright", "package", "cli.js")));
        foreach (var otherPlatform in AllPlatformFolders)
        {
            if (!string.Equals(otherPlatform, platformFolder, StringComparison.Ordinal))
            {
                Assert.False(Directory.Exists(Path.Combine(_driverDirectory, ".playwright", "node", otherPlatform)));
            }
        }

        Assert.False(File.Exists(Path.Combine(_driverDirectory, "microsoft.playwright.nuspec")));
        Assert.Equal(
            ModDBConstants.PlaywrightDriverVersion,
            (await File.ReadAllTextAsync(Path.Combine(_driverDirectory, "driver.version"))).Trim());
        Assert.False(File.Exists(Path.Combine(_driverDirectory, "driver.download")));
        Assert.Equal(
            _driverDirectory,
            Environment.GetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable));

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(Path.Combine(_driverDirectory, ".playwright", "node", platformFolder, nodeBinaryName));
            Assert.True((mode & UnixFileMode.UserExecute) != 0);
        }
    }

    /// <summary>
    /// Verifies declining the install aborts provisioning without downloading.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_ConsentDeclined_ThrowsWithoutDownloadingAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable, null);
        var downloadCalls = 0;
        var httpClient = new HttpClient(new StubHandler(_ =>
        {
            downloadCalls++;
            return BuildDriverPackage();
        }));
        var driver = new ManagedPlaywrightDriver(
            _driverDirectory,
            httpClient,
            _ => Task.FromResult(false),
            new Mock<ILogger>().Object);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => driver.EnsureInstalledAsync(default));

        // Assert
        Assert.Contains("declined", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, downloadCalls);
        Assert.False(Directory.Exists(Path.Combine(_driverDirectory, ".playwright")));
    }

    /// <summary>
    /// Verifies a stale managed driver version is re-provisioned.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_StaleMarker_ReprovisionsDriverAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable, null);
        SeedManagedDriver("0.0.0");
        var driver = CreateDriver(out var consentCalls, out var downloadCalls);

        // Act
        await driver.EnsureInstalledAsync(default);

        // Assert
        Assert.Single(consentCalls);
        Assert.Single(downloadCalls);
        Assert.Equal(
            ModDBConstants.PlaywrightDriverVersion,
            (await File.ReadAllTextAsync(Path.Combine(_driverDirectory, "driver.version"))).Trim());
    }

    /// <summary>
    /// Verifies package entries escaping the staging directory are skipped.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_ZipSlipEntry_SkipsEscapeAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable, null);
        var driver = CreateDriver(out _, out _, _ => BuildDriverPackage(includeTraversal: true));

        // Act
        await driver.EnsureInstalledAsync(default);

        // Assert
        Assert.False(File.Exists(Path.Combine(_driverDirectory, "escaped.txt")));
        Assert.True(File.Exists(Path.Combine(
            _driverDirectory, ".playwright", "node", ManagedChromiumRuntime.GetDriverPlatformFolder(), ManagedChromiumRuntime.GetDriverNodeBinaryName())));
    }

    /// <summary>
    /// Verifies cancellation aborts provisioning with cooperative cancellation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_CancelledBeforeConsent_ThrowsCanceledAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable, null);
        var driver = CreateDriver(out var consentCalls, out var downloadCalls);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.EnsureInstalledAsync(cts.Token));
        Assert.Empty(consentCalls);
        Assert.Empty(downloadCalls);
    }

    /// <summary>
    /// Verifies download failures surface as installation errors.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_DownloadFails_ThrowsInvalidOperationAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable, null);
        var httpClient = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));
        var driver = new ManagedPlaywrightDriver(
            _driverDirectory,
            httpClient,
            _ => Task.FromResult(true),
            new Mock<ILogger>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.EnsureInstalledAsync(default));
    }

    /// <summary>
    /// Verifies a tampered package is rejected before extraction and leaves no trace.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_HashMismatch_RejectsWithoutExtractingAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable, null);
        var driver = CreateDriver(out _, out _, expectedPackageSha256: new string('0', 64));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.EnsureInstalledAsync(default));
        Assert.False(Directory.Exists(Path.Combine(_driverDirectory, ".playwright")));
        Assert.False(File.Exists(Path.Combine(_driverDirectory, "driver.download")));
    }

    /// <summary>
    /// Verifies the pinned package hash is a well-formed lowercase SHA-256 hex string.
    /// </summary>
    [Fact]
    public void PlaywrightDriverExpectedSha256_IsLowercaseHex64()
    {
        Assert.Matches("^[0-9a-f]{64}$", ModDBConstants.PlaywrightDriverExpectedSha256);
    }

    /// <summary>
    /// Deletes the temporary driver directory and restores the process environment.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable, _originalDriverPath);
        if (Directory.Exists(_driverDirectory))
        {
            Directory.Delete(_driverDirectory, recursive: true);
        }
    }

    private static byte[] BuildDriverPackage(bool includeTraversal = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var folder in AllPlatformFolders)
            {
                var binaryName = string.Equals(folder, "win32_x64", StringComparison.Ordinal) ? "node.exe" : "node";
                WriteEntry(archive, $".playwright/node/{folder}/{binaryName}", "node-binary");
            }

            WriteEntry(archive, ".playwright/package/cli.js", "cli");
            WriteEntry(archive, ".playwright/package/package.json", "{}");

            // Entries that must never be extracted: NuGet metadata and escapes.
            WriteEntry(archive, "microsoft.playwright.nuspec", "nuspec");
            if (includeTraversal)
            {
                WriteEntry(archive, ".playwright/package/../../escaped.txt", "escaped");
            }
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private ManagedPlaywrightDriver CreateDriver(
        out List<string> consentCalls,
        out List<string> downloadCalls,
        Func<Uri?, byte[]>? packageFactory = null,
        string? expectedPackageSha256 = null)
    {
        var consents = new List<string>();
        var downloads = new List<string>();
        consentCalls = consents;
        downloadCalls = downloads;
        var factory = packageFactory ?? (_ => BuildDriverPackage());
        byte[]? packageBytes = null;
        var httpClient = new HttpClient(new StubHandler(request =>
        {
            downloads.Add(request?.AbsoluteUri ?? string.Empty);
            packageBytes ??= factory(request);
            return packageBytes;
        }));
        packageBytes ??= factory(null);
        var expectedHash = expectedPackageSha256 ?? Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        return new ManagedPlaywrightDriver(
            _driverDirectory,
            httpClient,
            path =>
            {
                consents.Add(path);
                return Task.FromResult(true);
            },
            new Mock<ILogger>().Object,
            new ManagedPlaywrightDriverOptions
            {
                ExpectedPackageSha256 = expectedHash,
            });
    }

    private void SeedManagedDriver(string version)
    {
        var platformFolder = ManagedChromiumRuntime.GetDriverPlatformFolder();
        var nodeBinaryName = ManagedChromiumRuntime.GetDriverNodeBinaryName();
        Directory.CreateDirectory(Path.Combine(_driverDirectory, ".playwright", "node", platformFolder));
        Directory.CreateDirectory(Path.Combine(_driverDirectory, ".playwright", "package"));
        File.WriteAllText(Path.Combine(_driverDirectory, ".playwright", "node", platformFolder, nodeBinaryName), "node");
        File.WriteAllText(Path.Combine(_driverDirectory, ".playwright", "package", "cli.js"), "cli");
        File.WriteAllText(Path.Combine(_driverDirectory, "driver.version"), version);
    }

    private sealed class StubHandler(Func<Uri?, byte[]> packageFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = packageFactory(request.RequestUri);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            };
            return Task.FromResult(response);
        }
    }
}
