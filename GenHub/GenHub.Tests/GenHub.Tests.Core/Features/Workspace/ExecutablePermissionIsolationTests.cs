using System;
using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
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
    public async Task ChmodThroughHardLink_AlsoChangesTheTargetAsync()
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

        const string message =
            "Expected chmod through a hard link to affect the shared inode. If this now "
            + "fails, the copy-before-chmod behaviour in WorkspaceStrategyBase can be revisited.";

        Assert.True(
            File.GetUnixFileMode(casBlob).HasFlag(UnixFileMode.UserExecute),
            message);
    }

    /// <summary>
    /// The mitigation: copying first gives the workspace its own inode, so the execute
    /// bit stops at the workspace and the stored blob keeps the mode it was ingested with.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyBeforeChmod_LeavesTheStoredBlobUntouchedAsync()
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

        await _strategy.TestEnsureExecutableAsync(
            new ManifestFile { RelativePath = "workspace-file", IsExecutable = true },
            workspaceFile);

        Assert.True(File.GetUnixFileMode(workspaceFile).HasFlag(UnixFileMode.UserExecute));
        Assert.False(
            File.GetUnixFileMode(casBlob).HasFlag(UnixFileMode.UserExecute),
            "The stored blob was modified, so every other profile using this hash is affected.");

        Assert.Empty(Directory.GetFiles(_tempDir, "*.genhub-exec-tmp-*"));

        // Content must survive the round trip; a broken link is only acceptable if the
        // bytes are identical.
        Assert.Equal("engine binary", await File.ReadAllTextAsync(workspaceFile));
    }

    /// <summary>
    /// The destination must never be observable as missing or non-executable. Validation
    /// can restore a lost execute bit on the entry point, but on no other executable the
    /// manifest names, so materialisation must not expose either state in the first place.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ExecutableMaterialization_ReplacesDestinationAtomicallyAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var workspaceFile = Path.Combine(_tempDir, "atomic-entry-point");
        await File.WriteAllTextAsync(workspaceFile, "engine binary");
        File.SetUnixFileMode(workspaceFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await _strategy.TestEnsureExecutableAsync(
            new ManifestFile { RelativePath = "atomic-entry-point", IsExecutable = true },
            workspaceFile);

        Assert.True(File.Exists(workspaceFile));
        Assert.True(
            File.GetUnixFileMode(workspaceFile).HasFlag(UnixFileMode.UserExecute),
            "The replacement was already executable before it became the destination.");
        Assert.Empty(Directory.GetFiles(_tempDir, "*.genhub-exec-tmp-*"));
        Assert.Equal("engine binary", await File.ReadAllTextAsync(workspaceFile));
    }

    /// <summary>
    /// Workspaces bricked before materialisation became atomic can hold an entry point
    /// that is still hard-linked into the content store with its execute bit lost. The
    /// validator's repair must restore the bit the same way materialisation grants it:
    /// on a private copy, so the stored blob keeps the mode it was ingested with.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RepairingABrickedEntryPoint_LeavesTheStoredBlobUntouchedAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var casBlob = Path.Combine(_tempDir, "cas-blob");
        var entryPoint = Path.Combine(_tempDir, "entry-point");
        await File.WriteAllTextAsync(casBlob, "engine binary");
        File.SetUnixFileMode(casBlob, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await _service.CreateHardLinkAsync(entryPoint, casBlob);

        var validator = new WorkspaceValidator(NullLogger<WorkspaceValidator>.Instance);
        var result = await validator.EnsureEntryPointExecutableAsync(new WorkspaceInfo
        {
            Id = "bricked-workspace",
            WorkspacePath = _tempDir,
            ExecutablePath = entryPoint,
        });

        Assert.True(result.Success);
        Assert.True(result.Data);
        Assert.True(File.GetUnixFileMode(entryPoint).HasFlag(UnixFileMode.UserExecute));
        Assert.False(
            File.GetUnixFileMode(casBlob).HasFlag(UnixFileMode.UserExecute),
            "The stored blob was modified, so every other profile using this hash is affected.");
        Assert.Equal("engine binary", await File.ReadAllTextAsync(entryPoint));
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
