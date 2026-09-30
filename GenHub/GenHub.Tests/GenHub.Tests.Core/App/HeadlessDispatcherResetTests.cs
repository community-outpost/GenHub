using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.App;

/// <summary>
/// Verifies that <see cref="TestAppBuilder"/> starts each headless test on a working UI dispatcher.
/// </summary>
public class HeadlessDispatcherResetTests
{
    /// <summary>
    /// Verifies that a dispatcher created by background work before the headless platform initializes is replaced,
    /// so layout passes run and awaiting inside the test does not fail in <see cref="Dispatcher.PushFrame"/>.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StaleDispatcherBeforePlatformSetup_IsReplacedBeforeTheTestRunsAsync()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(StaleDispatcherAppBuilder));

        var (supportsRunLoops, width) = await session.Dispatch(
            async () =>
            {
                var border = new Border { Width = 100, Height = 50 };
                var window = new Window { Width = 400, Height = 300, Content = border };
                window.Show();
                try
                {
                    Dispatcher.UIThread.RunJobs();
                    border.Width = 200;
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(1);
                    return (Dispatcher.UIThread.SupportsRunLoops, border.Bounds.Width);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.True(supportsRunLoops);
        Assert.Equal(200, width);
    }

    /// <summary>
    /// Builds the test application after a background thread reads <see cref="Dispatcher.UIThread"/> in the gap
    /// between the session reset and the headless platform setup, as leaked work from an earlier test can.
    /// </summary>
    private static class StaleDispatcherAppBuilder
    {
        /// <summary>
        /// Creates the application builder that leaves a stale dispatcher behind before platform setup.
        /// </summary>
        /// <returns>The configured application builder.</returns>
        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = TestAppBuilder.BuildAvaloniaApp();
            var registerRuntime = builder.RuntimePlatformServicesInitializer!;
            return builder.UseRuntimePlatformSubsystem(
                () =>
                {
                    registerRuntime();
                    Task.Run(() => Dispatcher.UIThread).Wait();
                },
                builder.RuntimePlatformServicesName!);
        }
    }
}
