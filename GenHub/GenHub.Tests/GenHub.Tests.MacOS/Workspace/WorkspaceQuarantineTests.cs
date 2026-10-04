using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Diagnostics;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.MacOS.Workspace;

/// <summary>
/// Tests that a quarantined native install yields a workspace macOS will run, under every strategy.
/// </summary>
public sealed class WorkspaceQuarantineTests : IDisposable
{
    private const string EngineName = "generalszh";
    private const string LibraryName = "libSDL3.dylib";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"genhub-quarantine-{Guid.NewGuid():N}");
    private readonly string _installDir;
    private readonly string _workspaceRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceQuarantineTests"/> class.
    /// </summary>
    public WorkspaceQuarantineTests()
    {
        _installDir = Path.Combine(_root, "install");
        _workspaceRoot = Path.Combine(_root, "workspaces");
        Directory.CreateDirectory(_installDir);
        Directory.CreateDirectory(_workspaceRoot);
    }

    /// <summary>
    /// A browser download quarantines every extracted file. The engine and its libraries
    /// must reach the workspace without the attribute, and the user's install must keep it,
    /// because a link that shares the original would otherwise carry it into the launch.
    /// </summary>
    /// <param name="strategyType">The strategy under test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Theory]
    [InlineData(WorkspaceStrategy.HardLink)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.FullCopy)]
    public async Task PrepareAsync_QuarantinedNativeInstall_WorkspaceCodeIsNotQuarantinedAsync(WorkspaceStrategy strategyType)
    {
        var engine = CreateQuarantinedFile(EngineName, executable: true);
        var library = CreateQuarantinedFile(LibraryName, executable: false);

        var workspace = await CreateStrategy(strategyType).PrepareAsync(CreateConfiguration(strategyType), null, CancellationToken.None);

        Assert.True(workspace.IsPrepared, string.Join("; ", workspace.ValidationIssues.Select(i => i.Message)));
        Assert.True(File.Exists(Path.Combine(workspace.WorkspacePath, EngineName)), $"{strategyType} did not create the engine.");
        Assert.True(File.Exists(Path.Combine(workspace.WorkspacePath, LibraryName)), $"{strategyType} did not create the library.");
        Assert.False(HasQuarantine(Path.Combine(workspace.WorkspacePath, EngineName)), $"{strategyType} left the engine quarantined.");
        Assert.False(HasQuarantine(Path.Combine(workspace.WorkspacePath, LibraryName)), $"{strategyType} left the library quarantined.");
        Assert.True(HasQuarantine(engine), $"{strategyType} changed the user's engine binary.");
        Assert.True(HasQuarantine(library), $"{strategyType} changed the user's library.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // Best-effort cleanup for temporary test files.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup for temporary test files.
        }
    }

    private static IWorkspaceStrategy CreateStrategy(WorkspaceStrategy strategyType)
    {
        var fileOperations = new UnixFileOperationsService(
            new FileOperationsService(NullLogger<FileOperationsService>.Instance, new Mock<IDownloadService>().Object, new Mock<ICasService>().Object),
            new Mock<ICasService>().Object,
            NullLogger<UnixFileOperationsService>.Instance);

        return strategyType switch
        {
            WorkspaceStrategy.HardLink => new HardLinkStrategy(fileOperations, NullLogger<HardLinkStrategy>.Instance),
            WorkspaceStrategy.SymlinkOnly => new SymlinkOnlyStrategy(fileOperations, NullLogger<SymlinkOnlyStrategy>.Instance),
            WorkspaceStrategy.HybridCopySymlink => new HybridCopySymlinkStrategy(fileOperations, NullLogger<HybridCopySymlinkStrategy>.Instance),
            WorkspaceStrategy.FullCopy => new FullCopyStrategy(fileOperations, NullLogger<FullCopyStrategy>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(strategyType)),
        };
    }

    private static bool HasQuarantine(string path) => RunXattr("-p", "com.apple.quarantine", path) == 0;

    private static int RunXattr(params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/xattr", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        outputTask.GetAwaiter().GetResult();
        errorTask.GetAwaiter().GetResult();
        process.WaitForExit();
        return process.ExitCode;
    }

    private string CreateQuarantinedFile(string name, bool executable)
    {
        var path = Path.Combine(_installDir, name);
        File.WriteAllText(path, name);
        if (executable && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
        }

        Assert.Equal(0, RunXattr("-w", "com.apple.quarantine", "0081;00000000;Safari;", path));
        return path;
    }

    private WorkspaceConfiguration CreateConfiguration(WorkspaceStrategy strategy) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Strategy = strategy,
        WorkspaceRootPath = _workspaceRoot,
        BaseInstallationPath = _installDir,
        GameClient = new GameClient { Id = "quarantine", ExecutablePath = EngineName, GameType = GameType.ZeroHour },
        Manifests =
        [
            new ContentManifest
            {
                Id = ManifestId.Create("1.0.test.gameclient.quarantine"),
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                EntryPoint = EngineName,
                Files =
                [
                    new() { RelativePath = EngineName, Size = EngineName.Length, IsExecutable = true, SourceType = ContentSourceType.GameInstallation },
                    new() { RelativePath = LibraryName, Size = LibraryName.Length, SourceType = ContentSourceType.GameInstallation },
                ],
            },
        ],
    };
}
