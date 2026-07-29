using System;
using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Demonstrates why marking a workspace file executable must not be done in place when
/// the workspace is built from hard links.
/// <para>
/// The content store keys objects purely on content hash. Unix file mode lives in the
/// inode, which a hard link shares with its target, so the store cannot represent two
/// files with identical bytes and different modes. Setting the execute bit on a linked
/// workspace file therefore changes the stored blob for every profile referencing that
/// hash.
/// </para>
/// </summary>
public class ExecutablePermissionIsolationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"genhub-execisolation-{Guid.NewGuid():N}");

    private readonly UnixFileOperationsService _service;
    private readonly WorkspaceStrategyBaseTests.TestWorkspaceStrategy _strategy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutablePermissionIsolationTests"/> class.
    /// </summary>
    public ExecutablePermissionIsolationTests()
    {
        Directory.CreateDirectory(_tempDir);

        var baseService = new FileOperationsService(
            NullLogger<FileOperationsService>.Instance,
            new Mock<IDownloadService>().Object,
            new Mock<ICasService>().Object);

        _service = new UnixFileOperationsService(
            baseService,
            new Mock<ICasService>().Object,
            NullLogger<UnixFileOperationsService>.Instance);
        _strategy = new WorkspaceStrategyBaseTests.TestWorkspaceStrategy(_service);
    }

    /// <summary>
    /// Establishes the hazard: chmod through a hard link changes the target too.
    /// <para>
    /// If this ever stops being true, the workspace copy this behaviour forces could be
    /// dropped. It is asserted rather than assumed, because the whole design of
    /// <c>EnsureExecutableAsync</c> rests on it.
    /// </para>
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ChmodThroughHardLink_AlsoChangesTheTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var casBlob = Path.Combine(_tempDir, "cas-blob");
        var workspaceFile = Path.Combine(_tempDir, "workspace-file");
        await File.WriteAllTextAsync(casBlob, "engine binary");
        File.SetUnixFileMode(casBlob, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await _service.CreateHardLinkAsync(workspaceFile, casBlob);

        File.SetUnixFileMode(
            workspaceFile,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Assert.True(
            File.GetUnixFileMode(casBlob).HasFlag(UnixFileMode.UserExecute),
            "Expected chmod through a hard link to affect the shared inode. If this now "
            + "fails, the copy-before-chmod behaviour in WorkspaceStrategyBase can be revisited.");
    }

    /// <summary>
    /// The mitigation: copying first gives the workspace its own inode, so the execute
    /// bit stops at the workspace and the stored blob keeps the mode it was ingested with.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyBeforeChmod_LeavesTheStoredBlobUntouched()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var casBlob = Path.Combine(_tempDir, "cas-blob");
        var workspaceFile = Path.Combine(_tempDir, "workspace-file");
        await File.WriteAllTextAsync(casBlob, "engine binary");
        File.SetUnixFileMode(casBlob, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await _service.CreateHardLinkAsync(workspaceFile, casBlob);

        var temporaryPath = workspaceFile + ".genhub-exec-tmp";
        await _strategy.TestEnsureExecutableAsync(
            new ManifestFile { RelativePath = "workspace-file", IsExecutable = true },
            workspaceFile);

        Assert.True(File.GetUnixFileMode(workspaceFile).HasFlag(UnixFileMode.UserExecute));
        Assert.False(
            File.GetUnixFileMode(casBlob).HasFlag(UnixFileMode.UserExecute),
            "The stored blob was modified, so every other profile using this hash is affected.");

        Assert.False(
            File.Exists(temporaryPath),
            "The temporary copy must not survive; workspace validation would report it.");

        // Content must survive the round trip; a broken link is only acceptable if the
        // bytes are identical.
        Assert.Equal("engine binary", await File.ReadAllTextAsync(workspaceFile));
    }

    /// <summary>
    /// The destination must never be observable as missing or non-executable. Verification
    /// never mutates, so a workspace left with a non-executable entry point stays broken
    /// for every later launch — the failure this sequence exists to prevent.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ExecutableMaterialization_ReplacesDestinationAtomically()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var workspaceFile = Path.Combine(_tempDir, "atomic-entry-point");
        await File.WriteAllTextAsync(workspaceFile, "engine binary");
        File.SetUnixFileMode(workspaceFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var temporaryPath = workspaceFile + ".genhub-exec-tmp";
        await _strategy.TestEnsureExecutableAsync(
            new ManifestFile { RelativePath = "atomic-entry-point", IsExecutable = true },
            workspaceFile);

        Assert.True(File.Exists(workspaceFile));
        Assert.True(
            File.GetUnixFileMode(workspaceFile).HasFlag(UnixFileMode.UserExecute),
            "The replacement was already executable before it became the destination.");
        Assert.False(File.Exists(temporaryPath));
        Assert.Equal("engine binary", await File.ReadAllTextAsync(workspaceFile));
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
