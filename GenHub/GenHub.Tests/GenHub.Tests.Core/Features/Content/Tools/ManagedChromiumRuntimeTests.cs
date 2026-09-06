using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Models.Common;
using GenHub.Features.Content.Services.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Tools;

/// <summary>
/// Tests the app-owned Playwright Chromium runtime and request-header propagation.
/// </summary>
public sealed class ManagedChromiumRuntimeTests : IDisposable
{
    private readonly string _runtimeDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
    private readonly string? _originalBrowserPath = Environment.GetEnvironmentVariable(ManagedChromiumRuntime.BrowserPathEnvironmentVariable);
    private readonly string? _originalDriverPath = Environment.GetEnvironmentVariable(ManagedChromiumRuntime.DriverPathEnvironmentVariable);

    /// <summary>
    /// Verifies a pre-provisioned app-owned Chromium executable does not trigger another install.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_ExistingManagedExecutable_DoesNotRunInstallerAsync()
    {
        // Arrange
        var executablePath = Path.Combine(_runtimeDirectory, "chromium.exe");
        Directory.CreateDirectory(_runtimeDirectory);
        await File.WriteAllTextAsync(executablePath, "browser");
        var installerCalls = 0;
        var consentCalls = 0;
        var chromium = new Mock<IBrowserType>(MockBehavior.Strict);
        chromium.SetupGet(browser => browser.ExecutablePath).Returns(executablePath);
        var runtime = new ManagedChromiumRuntime(
            _runtimeDirectory,
            _ =>
            {
                installerCalls++;
                return 0;
            },
            _ =>
            {
                consentCalls++;
                return Task.FromResult(true);
            },
            new Mock<ILogger>().Object);

        // Act
        await runtime.EnsureInstalledAsync(chromium.Object, default);

        // Assert
        Assert.Equal(0, installerCalls);
        Assert.Equal(0, consentCalls);
        Assert.Equal(_runtimeDirectory, Environment.GetEnvironmentVariable(ManagedChromiumRuntime.BrowserPathEnvironmentVariable));
    }

    /// <summary>
    /// Verifies a clean app profile asks for consent, then invokes Playwright's Chromium installer
    /// exactly once and accepts the executable it produces, without consulting a system browser.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_MissingManagedExecutable_InstallsChromiumOnceAsync()
    {
        // Arrange
        var executablePath = Path.Combine(_runtimeDirectory, "chromium.exe");
        var installerCalls = 0;
        IReadOnlyList<string>? installerArguments = null;
        string? consentPath = null;
        var chromium = new Mock<IBrowserType>(MockBehavior.Strict);
        chromium.SetupGet(browser => browser.ExecutablePath).Returns(executablePath);
        var runtime = new ManagedChromiumRuntime(
            _runtimeDirectory,
            arguments =>
            {
                installerCalls++;
                installerArguments = arguments;
                Directory.CreateDirectory(_runtimeDirectory);
                File.WriteAllText(executablePath, "browser");
                return 0;
            },
            path =>
            {
                consentPath = path;
                return Task.FromResult(true);
            },
            new Mock<ILogger>().Object);

        // Act
        await runtime.EnsureInstalledAsync(chromium.Object, default);

        // Assert
        Assert.Equal(_runtimeDirectory, consentPath);
        Assert.Equal(1, installerCalls);
        Assert.Equal(["install", "chromium"], installerArguments);
        Assert.True(File.Exists(executablePath));
        Assert.Equal(_runtimeDirectory, Environment.GetEnvironmentVariable(ManagedChromiumRuntime.BrowserPathEnvironmentVariable));
    }

    /// <summary>
    /// Verifies declining the install consent dialog cancels provisioning without downloading.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_ConsentDeclined_ThrowsWithoutInstallingAsync()
    {
        // Arrange
        var executablePath = Path.Combine(_runtimeDirectory, "chromium.exe");
        var installerCalls = 0;
        var chromium = new Mock<IBrowserType>(MockBehavior.Strict);
        chromium.SetupGet(browser => browser.ExecutablePath).Returns(executablePath);
        var runtime = new ManagedChromiumRuntime(
            _runtimeDirectory,
            _ =>
            {
                installerCalls++;
                return 0;
            },
            _ => Task.FromResult(false),
            new Mock<ILogger>().Object);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.EnsureInstalledAsync(chromium.Object, default));

        // Assert
        Assert.Contains("declined", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, installerCalls);
        Assert.False(File.Exists(executablePath));
    }

    /// <summary>
    /// Verifies browser downloads retain ModDB's referer and other safe request headers while
    /// excluding headers Chromium must own itself.
    /// </summary>
    [Fact]
    public void BuildSafeDownloadHeaders_PreservesRefererAndFiltersUnsafeHeaders()
    {
        // Arrange
        var download = new DownloadConfiguration { UserAgent = "GenHub Test Agent" };
        download.Headers["Referer"] = "https://www.moddb.com/mods/example";
        download.Headers["Accept"] = "application/octet-stream";
        download.Headers["Host"] = "should-not-be-forwarded";
        download.Headers["Content-Length"] = "123";

        // Act
        var headers = PlaywrightService.BuildSafeDownloadHeaders(download);

        // Assert
        Assert.Equal("https://www.moddb.com/mods/example", headers["Referer"]);
        Assert.Equal("application/octet-stream", headers["Accept"]);
        Assert.Equal("GenHub Test Agent", headers["User-Agent"]);
        Assert.DoesNotContain("Host", headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content-Length", headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies IsDownloadNavigationException recognizes direct download triggers from Playwright navigation errors.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="isDownloadCompleted">Whether the download TCS has completed.</param>
    /// <param name="expected">The expected classification result.</param>
    [Theory]
    [InlineData("Download is starting", false, true)]
    [InlineData("net::ERR_ABORTED at https://media.moddb.com/file.zip", true, true)]
    [InlineData("net::ERR_ABORTED at https://media.moddb.com/file.zip", false, false)]
    [InlineData("net::ERR_CONNECTION_REFUSED", true, false)]
    public void IsDownloadNavigationException_RecognizesDownloadTriggerErrors(string message, bool isDownloadCompleted, bool expected)
    {
        // Arrange
        var ex = new PlaywrightException(message);
        var downloadTcs = new TaskCompletionSource<IDownload>();
        if (isDownloadCompleted)
        {
            downloadTcs.SetResult(new Mock<IDownload>().Object);
        }

        // Act
        var result = PlaywrightService.IsDownloadNavigationException(ex, downloadTcs);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Deletes the temporary runtime directory and restores the process environment.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.BrowserPathEnvironmentVariable, _originalBrowserPath);
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverPathEnvironmentVariable, _originalDriverPath);
        if (Directory.Exists(_runtimeDirectory))
        {
            Directory.Delete(_runtimeDirectory, recursive: true);
        }
    }
}
