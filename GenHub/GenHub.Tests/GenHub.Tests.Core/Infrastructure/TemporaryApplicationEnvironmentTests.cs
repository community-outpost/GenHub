using GenHub.Tests.Core.Collections;
using GenHub.Tests.Shared;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace GenHub.Tests.Core.Infrastructure;

/// <summary>
/// Verifies that <see cref="TemporaryApplicationEnvironment"/> cleans up without failing the test that used it.
/// </summary>
[Collection(ApplicationEnvironmentCollection.Name)]
public class TemporaryApplicationEnvironmentTests
{
    private const string HomeVariable = "HOME";

    /// <summary>
    /// Verifies that disposing restores the environment and deletes the tree when nothing holds it.
    /// </summary>
    [Fact]
    public void Dispose_WhenNothingInterferes_RestoresEnvironmentAndDeletesTree()
    {
        var originalHome = Environment.GetEnvironmentVariable(HomeVariable);
        using var environment = new TemporaryApplicationEnvironment();
        File.WriteAllText(Path.Combine(environment.AppDataPath, "late.log"), "entry");
        Assert.Equal(environment.RootPath, Environment.GetEnvironmentVariable(HomeVariable));

        ((IDisposable)environment).Dispose();

        Assert.Equal(originalHome, Environment.GetEnvironmentVariable(HomeVariable));
        Assert.False(Directory.Exists(environment.RootPath));
    }

    /// <summary>
    /// Verifies that disposing does not throw when a file inside the tree cannot be removed.
    /// </summary>
    [NonRootFact]
    public void Dispose_WhenTreeCannotBeDeleted_LeavesTreeWithoutThrowing()
    {
        var originalHome = Environment.GetEnvironmentVariable(HomeVariable);
        using var environment = new TemporaryApplicationEnvironment();
        var heldFile = CreateHeldFile(environment);

        try
        {
            using (BlockDeletion(environment.AppDataPath, heldFile))
            {
                var failure = Record.Exception(((IDisposable)environment).Dispose);

                Assert.Null(failure);
                Assert.True(File.Exists(heldFile));
                Assert.Equal(originalHome, Environment.GetEnvironmentVariable(HomeVariable));
            }
        }
        finally
        {
            DeleteIfPresent(environment.RootPath);
        }
    }

    /// <summary>
    /// Verifies that a retry deletes the tree once the block on the first attempt is released.
    /// </summary>
    [NonRootFact]
    public void Dispose_WhenBlockIsReleasedDuringCleanup_DeletesTree()
    {
        using var environment = new TemporaryApplicationEnvironment();
        var heldFile = CreateHeldFile(environment);
        var block = BlockDeletion(environment.AppDataPath, heldFile);
        using var listener = new ReleaseOnRetryListener(environment.RootPath, block);
        Trace.Listeners.Add(listener);

        try
        {
            ((IDisposable)environment).Dispose();

            Assert.True(listener.Released, "The first delete attempt was not blocked.");
            Assert.False(Directory.Exists(environment.RootPath));
        }
        finally
        {
            Trace.Listeners.Remove(listener);
            block.Dispose();
            DeleteIfPresent(environment.RootPath);
        }
    }

    /// <summary>
    /// Verifies that disposing does not throw while another thread keeps creating files inside the tree.
    /// </summary>
    /// <returns>A task that completes when the writer has stopped.</returns>
    [Fact]
    public async Task Dispose_WhenFilesAreCreatedConcurrently_DoesNotThrowAsync()
    {
        using var environment = new TemporaryApplicationEnvironment();
        using var stop = new CancellationTokenSource();
        using var firstWrite = new ManualResetEventSlim();
        var writer = Task.Factory.StartNew(
            () => WriteUntilStopped(environment.AppDataPath, firstWrite, stop.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        try
        {
            Assert.True(firstWrite.Wait(TimeSpan.FromSeconds(10)), "The background writer did not start.");

            var failure = Record.Exception(((IDisposable)environment).Dispose);

            Assert.Null(failure);
        }
        finally
        {
            stop.Cancel();
            await writer.WaitAsync(TimeSpan.FromSeconds(10));
            DeleteIfPresent(environment.RootPath);
        }
    }

    private static string CreateHeldFile(TemporaryApplicationEnvironment environment)
    {
        var heldFile = Path.Combine(environment.AppDataPath, "held.log");
        File.WriteAllText(heldFile, "entry");
        return heldFile;
    }

    private static IDisposable BlockDeletion(string directoryPath, string heldFile)
    {
        if (OperatingSystem.IsWindows())
        {
            return File.Open(heldFile, System.IO.FileMode.Open, FileAccess.Read, FileShare.None);
        }

        return new ReadOnlyDirectory(directoryPath);
    }

    private static void WriteUntilStopped(string directoryPath, ManualResetEventSlim firstWrite, CancellationToken stopToken)
    {
        var index = 0;
        while (!stopToken.IsCancellationRequested)
        {
            try
            {
                Directory.CreateDirectory(directoryPath);
                File.WriteAllText(Path.Combine(directoryPath, $"late-{index++}.log"), "entry");
                firstWrite.Set();
            }
            catch (IOException)
            {
                // Cleanup can remove the directory between the create and the write. The writer keeps going.
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup can delete a directory that is mid-removal on Windows. The writer keeps going.
                continue;
            }
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class ReleaseOnRetryListener(string rootPath, IDisposable block) : TraceListener
    {
        public bool Released { get; private set; }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            ReleaseIfRetry(eventType, message);
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        {
            ReleaseIfRetry(eventType, args is null ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format ?? string.Empty, args));
        }

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
        }

        private void ReleaseIfRetry(TraceEventType eventType, string? message)
        {
            if (Released || eventType != TraceEventType.Information || message?.Contains(rootPath, StringComparison.Ordinal) != true)
            {
                return;
            }

            block.Dispose();
            Released = true;
        }
    }

    [UnsupportedOSPlatform("windows")]
    private sealed class ReadOnlyDirectory : IDisposable
    {
        private readonly string _path;
        private readonly UnixFileMode _originalMode;

        public ReadOnlyDirectory(string path)
        {
            _path = path;
            _originalMode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                File.SetUnixFileMode(_path, _originalMode);
            }
        }
    }
}
