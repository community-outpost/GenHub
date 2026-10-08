using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Notifications;
using GenHub.Features.Content.Services.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Tools;

/// <summary>
/// Tests the app-owned Playwright Chromium runtime and request-header propagation.
/// </summary>
[Collection(PlaywrightEnvironmentCollection.Name)]
public sealed class ManagedChromiumRuntimeTests : IDisposable
{
    private readonly string _runtimeDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
    private readonly string? _originalBrowserPath = Environment.GetEnvironmentVariable(ManagedChromiumRuntime.BrowserPathEnvironmentVariable);
    private readonly string? _originalDriverPath = Environment.GetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable);

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
    /// Verifies lifecycle callbacks are invoked when Chromium installation starts and succeeds.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WhenInstalling_InvokesLifecycleCallbacksAsync()
    {
        // Arrange
        var executablePath = Path.Combine(_runtimeDirectory, "chromium.exe");
        var startingCalled = false;
        bool? completedSuccess = null;
        var chromium = new Mock<IBrowserType>(MockBehavior.Strict);
        chromium.SetupGet(browser => browser.ExecutablePath).Returns(executablePath);
        var runtime = new ManagedChromiumRuntime(
            _runtimeDirectory,
            _ =>
            {
                Directory.CreateDirectory(_runtimeDirectory);
                File.WriteAllText(executablePath, "browser");
                return 0;
            },
            _ => Task.FromResult(true),
            new Mock<ILogger>().Object,
            callbacks: new ManagedChromiumRuntimeCallbacks
            {
                OnInstallStarting = () => startingCalled = true,
                OnInstallCompleted = success => completedSuccess = success,
            });

        // Act
        await runtime.EnsureInstalledAsync(chromium.Object, default);

        // Assert
        Assert.True(startingCalled);
        Assert.True(completedSuccess);
    }

    /// <summary>
    /// Verifies lifecycle completion callback is not invoked when Chromium installation is canceled.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WhenCanceled_DoesNotInvokeCompletedCallbackAsync()
    {
        // Arrange
        var executablePath = Path.Combine(_runtimeDirectory, "chromium.exe");
        var startingCalled = false;
        bool? completedSuccess = null;
        using var cts = new CancellationTokenSource();

        var chromium = new Mock<IBrowserType>(MockBehavior.Strict);
        chromium.SetupGet(browser => browser.ExecutablePath).Returns(executablePath);
        var runtime = new ManagedChromiumRuntime(
            _runtimeDirectory,
            _ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
            _ => Task.FromResult(true),
            new Mock<ILogger>().Object,
            callbacks: new ManagedChromiumRuntimeCallbacks
            {
                OnInstallStarting = () => startingCalled = true,
                OnInstallCompleted = success => completedSuccess = success,
            });

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.EnsureInstalledAsync(chromium.Object, cts.Token));
        Assert.True(startingCalled);
        Assert.Null(completedSuccess);
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
    /// Verifies that when notification service is provided, EnsureInstalledAsync creates a download
    /// notification scope, installs successfully, and completes the scope with success notification.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WithNotificationService_ShowsNotificationsAndCompletesSuccessAsync()
    {
        // Arrange
        var executablePath = Path.Combine(_runtimeDirectory, "chromium.exe");
        var installerCalls = 0;
        var startCalls = 0;
        var completedSuccess = false;
        var chromium = new Mock<IBrowserType>(MockBehavior.Strict);
        chromium.SetupGet(browser => browser.ExecutablePath).Returns(executablePath);

        var notificationService = new Mock<INotificationService>();

        var runtime = new ManagedChromiumRuntime(
            _runtimeDirectory,
            _ =>
            {
                installerCalls++;
                Directory.CreateDirectory(_runtimeDirectory);
                File.WriteAllText(executablePath, "browser");
                return 0;
            },
            _ => Task.FromResult(true),
            new Mock<ILogger>().Object,
            notificationService: notificationService.Object,
            callbacks: new ManagedChromiumRuntimeCallbacks
            {
                OnInstallStarting = () => startCalls++,
                OnInstallCompleted = success => completedSuccess = success,
            });

        // Act
        await runtime.EnsureInstalledAsync(chromium.Object, default);

        // Assert
        Assert.Equal(1, installerCalls);
        Assert.Equal(1, startCalls);
        Assert.True(completedSuccess);
        notificationService.Verify(n => n.Show(It.IsAny<NotificationMessage>()), Times.AtLeastOnce);
        notificationService.Verify(n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Verifies that when installer fails, EnsureInstalledAsync completes failure on the scope,
    /// triggers onInstallCompleted(false), and throws InvalidOperationException.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WhenInstallerFails_CompletesFailureOnScopeAndThrowsAsync()
    {
        // Arrange
        var executablePath = Path.Combine(_runtimeDirectory, "chromium.exe");
        var completedSuccess = true;
        var chromium = new Mock<IBrowserType>(MockBehavior.Strict);
        chromium.SetupGet(browser => browser.ExecutablePath).Returns(executablePath);

        var notificationService = new Mock<INotificationService>();

        var runtime = new ManagedChromiumRuntime(
            _runtimeDirectory,
            _ => 1,
            _ => Task.FromResult(true),
            new Mock<ILogger>().Object,
            notificationService: notificationService.Object,
            callbacks: new ManagedChromiumRuntimeCallbacks
            {
                OnInstallCompleted = success => completedSuccess = success,
            });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.EnsureInstalledAsync(chromium.Object, default));

        Assert.False(completedSuccess);
        notificationService.Verify(n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Verifies that when installer is canceled during execution, EnsureInstalledAsync marks the scope canceled
    /// and invokes onInstallCanceled callback.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WhenInstallerThrowsCancellation_CompletesCanceledAndInvokesCallbackAsync()
    {
        // Arrange
        var executablePath = Path.Combine(_runtimeDirectory, "chromium.exe");
        var canceledCalled = false;
        using var cts = new CancellationTokenSource();

        var chromium = new Mock<IBrowserType>(MockBehavior.Strict);
        chromium.SetupGet(browser => browser.ExecutablePath).Returns(executablePath);

        var notificationService = new Mock<INotificationService>();

        var runtime = new ManagedChromiumRuntime(
            _runtimeDirectory,
            _ => throw new OperationCanceledException(),
            _ => Task.FromResult(true),
            new Mock<ILogger>().Object,
            notificationService: notificationService.Object,
            callbacks: new ManagedChromiumRuntimeCallbacks
            {
                OnInstallCanceled = () => canceledCalled = true,
            });

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.EnsureInstalledAsync(chromium.Object, cts.Token));

        Assert.True(canceledCalled);
        notificationService.Verify(n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
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
    /// Verifies staged download measurement accumulates per-file high-water marks so deleted
    /// phase archives stay counted, and ignores files outside Playwright's staging pattern.
    /// </summary>
    [Fact]
    public void MeasureStagedDownloadBytes_AccumulatesHighWaterMarksAcrossDeletedPhases()
    {
        // Arrange
        var stagingDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var runtime = new ManagedChromiumRuntime(
                _runtimeDirectory,
                _ => 0,
                _ => Task.FromResult(true),
                new Mock<ILogger>().Object);
            var highWaterMarks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            File.WriteAllBytes(Path.Combine(stagingDirectory, "playwright-download-chromium-win32-x64.zip"), new byte[100]);
            File.WriteAllBytes(Path.Combine(stagingDirectory, "unrelated.tmp"), new byte[5000]);

            // Act
            var first = runtime.MeasureStagedDownloadBytes(stagingDirectory, highWaterMarks);

            // Assert
            Assert.Equal(100, first);

            // Act: Playwright deletes each archive after extraction, then stages the next phase.
            File.Delete(Path.Combine(stagingDirectory, "playwright-download-chromium-win32-x64.zip"));
            File.WriteAllBytes(Path.Combine(stagingDirectory, "playwright-download-ffmpeg-win64.zip"), new byte[50]);
            var second = runtime.MeasureStagedDownloadBytes(stagingDirectory, highWaterMarks);

            // Assert
            Assert.Equal(150, second);
        }
        finally
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies staged download measurement returns zero when no archives were ever staged.
    /// </summary>
    [Fact]
    public void MeasureStagedDownloadBytes_WithoutStagedArchives_ReturnsZero()
    {
        // Arrange
        var runtime = new ManagedChromiumRuntime(
            _runtimeDirectory,
            _ => 0,
            _ => Task.FromResult(true),
            new Mock<ILogger>().Object);

        // Act
        var total = runtime.MeasureStagedDownloadBytes(
            Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N")),
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));

        // Assert
        Assert.Equal(0, total);
    }

    /// <summary>
    /// Deletes the temporary runtime directory and restores the process environment.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.BrowserPathEnvironmentVariable, _originalBrowserPath);
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable, _originalDriverPath);
        if (Directory.Exists(_runtimeDirectory))
        {
            Directory.Delete(_runtimeDirectory, recursive: true);
        }
    }
}
