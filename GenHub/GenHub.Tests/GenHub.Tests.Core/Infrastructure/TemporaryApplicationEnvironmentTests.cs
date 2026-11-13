using GenHub.Tests.Core.Collections;
using GenHub.Tests.Shared;
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
        var environment = new TemporaryApplicationEnvironment();
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
        var environment = new TemporaryApplicationEnvironment();
        var heldFile = Path.Combine(environment.AppDataPath, "held.log");
        File.WriteAllText(heldFile, "entry");

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
    /// Verifies that disposing does not throw while another thread keeps creating files inside the tree.
    /// </summary>
    /// <returns>A task that completes when the writer has stopped.</returns>
    [Fact]
    public async Task Dispose_WhenFilesAreCreatedConcurrently_DoesNotThrow()
    {
        var environment = new TemporaryApplicationEnvironment();
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
            }
            catch (UnauthorizedAccessException)
            {
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
