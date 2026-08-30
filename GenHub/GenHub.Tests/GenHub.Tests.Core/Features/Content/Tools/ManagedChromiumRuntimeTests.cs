using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Notifications;
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
        IReadOnlyList<string>? installerArguments = null;
        string? consentPath = null;
        var chromium = new Mock<IBrowserType>(MockBehavior.Strict);
        chromium.SetupGet(browser => browser.ExecutablePath).Returns(executablePath);
        var runtime = new ManagedChromiumRuntime(
            _runtimeDirectory,
            arguments =>
            {
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
        Assert.Equal(["install", "chromium"], installerArguments);
        Assert.True(File.Exists(executablePath));
        Assert.Equal(_runtimeDirectory, Environment.GetEnvironmentVariable(ManagedChromiumRuntime.BrowserPathEnvironmentVariable));
    }

    /// <summary>
    /// Verifies that when a notification service is provided, progress notifications are displayed and updated.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WithNotificationService_ShowsAndUpdatesProgressNotificationAsync()
    {
        // Arrange
        var executablePath = Path.Combine(_runtimeDirectory, "chromium.exe");
        var chromium = new Mock<IBrowserType>(MockBehavior.Strict);
        chromium.SetupGet(browser => browser.ExecutablePath).Returns(executablePath);

        var notificationServiceMock = new Mock<INotificationService>(MockBehavior.Loose);
        NotificationMessage? shownNotification = null;
        notificationServiceMock
            .Setup(n => n.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg => shownNotification = msg);

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
            notificationServiceMock.Object);

        // Act
        await runtime.EnsureInstalledAsync(chromium.Object, default);

        // Assert
        Assert.NotNull(shownNotification);
        Assert.Equal(ModDBConstants.ChromiumInstallTitle, shownNotification.Title);
        Assert.Equal(ModDBConstants.ChromiumDownloadingMessage, shownNotification.Message);
        notificationServiceMock.Verify(n => n.Show(It.IsAny<NotificationMessage>()), Times.Once);
        notificationServiceMock.Verify(n => n.Update(It.IsAny<Guid>(), ModDBConstants.ChromiumReadyMessage, ModDBConstants.ChromiumReadyTitle), Times.Once);
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
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.EnsureInstalledAsync(chromium.Object, default));

        // Assert
        Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    /// Deletes the temporary runtime directory and restores the process environment.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ManagedChromiumRuntime.BrowserPathEnvironmentVariable, _originalBrowserPath);
        if (Directory.Exists(_runtimeDirectory))
        {
            Directory.Delete(_runtimeDirectory, recursive: true);
        }
    }
}
