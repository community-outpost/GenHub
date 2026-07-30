using System.Runtime.InteropServices;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests for the WorkspaceValidator class.
/// </summary>
public partial class WorkspaceValidatorTests : IDisposable
{
    private readonly Mock<ILogger<WorkspaceValidator>> _mockLogger;
    private readonly WorkspaceValidator _validator;
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _workspaceDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceValidatorTests"/> class.
    /// </summary>
    public WorkspaceValidatorTests()
    {
        _mockLogger = new Mock<ILogger<WorkspaceValidator>>();
        _validator = new WorkspaceValidator(_mockLogger.Object);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_tempDir, "source");
        _workspaceDir = Path.Combine(_tempDir, "workspace");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_workspaceDir);
    }

    /// <summary>
    /// Tests validation of a valid workspace configuration.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateConfigurationAsync_ValidConfiguration_ReturnsSuccess()
    {
        // Arrange
        var config = CreateValidConfiguration();

        // Act
        var result = await _validator.ValidateConfigurationAsync(config);

        // Assert
        Assert.NotNull(result);

        // Allow warnings but not errors
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);

        // If there are warnings about empty manifests, that's acceptable for this test
        var manifestWarnings = result.Issues.Where(i => i.Message.Contains("Manifest must contain at least one file"));
        Assert.True(manifestWarnings.All(w => w.Severity == ValidationSeverity.Warning));
    }

    /// <summary>
    /// Tests validation fails when required properties are missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateConfigurationAsync_MissingRequiredProperties_ReturnsErrors()
    {
        // Arrange
        var config = new WorkspaceConfiguration
        {
            Id = string.Empty,
            BaseInstallationPath = string.Empty,
            WorkspaceRootPath = string.Empty,
            Manifests = [new() { Files = [], }],
        };

        // Act
        var result = await _validator.ValidateConfigurationAsync(config);

        // Assert
        Assert.True(result.Issues.Count(i => i.Severity == ValidationSeverity.Error) >= 3);
    }

    /// <summary>
    /// Tests validation fails when source directory doesn't exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateConfigurationAsync_NonExistentSourcePath_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfiguration();
        config.BaseInstallationPath = Path.Combine(_tempDir, "nonexistent");

        // Act
        var result = await _validator.ValidateConfigurationAsync(config);

        // Assert
        Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.DirectoryMissing && i.Severity == ValidationSeverity.Error);
    }

    /// <summary>
    /// Tests validation fails when manifest has no files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateConfigurationAsync_EmptyManifest_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfiguration();
        config.Manifests = [new() { Files = [], }];

        // Act
        var result = await _validator.ValidateConfigurationAsync(config);

        // Assert
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    /// <summary>
    /// Tests prerequisite validation for strategies requiring admin rights.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidatePrerequisitesAsync_AdminRequired_ValidatesCorrectly()
    {
        // Arrange
        var mockStrategy = new Mock<IWorkspaceStrategy>();
        mockStrategy.Setup(s => s.Name).Returns("Test Strategy");

        // Use reflection to simulate admin requirement
        var strategyType = mockStrategy.Object.GetType();
        var propAdmin = strategyType.GetProperty("RequiresAdminRights");
        if (propAdmin?.CanWrite == true)
        {
            propAdmin.SetValue(mockStrategy.Object, true);
        }

        // Create config from paths
        var config = new WorkspaceConfiguration
        {
            Id = Path.GetFileName(_workspaceDir),
            BaseInstallationPath = _sourceDir,
            WorkspaceRootPath = Path.GetDirectoryName(_workspaceDir) ?? _workspaceDir,
            Manifests = [], // Empty for this test
            GameClient = new GameClient { Id = "test" },
            Strategy = WorkspaceStrategy.FullCopy,
        };

        // Act
        var result = await _validator.ValidatePrerequisitesAsync(mockStrategy.Object, config, default);

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>
    /// Tests prerequisite validation for different volume scenarios.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidatePrerequisitesAsync_DifferentVolumes_ReturnsWarning()
    {
        // Arrange
        var mockStrategy = new Mock<IWorkspaceStrategy>();
        mockStrategy.Setup(s => s.Name).Returns("Hard Link Strategy");

        // Create a concrete strategy that requires same volume for testing
        var fileOps = new Mock<IFileOperationsService>();
        var logger = new Mock<ILogger<HardLinkStrategy>>();
        var hardLinkStrategy = new HardLinkStrategy(fileOps.Object, logger.Object);

        // Create paths on potentially different volumes
        var sourcePath = _sourceDir;
        var destPath = Path.Combine(Path.GetTempPath(), "different", Guid.NewGuid().ToString());

        // Create config from paths
        var config = new WorkspaceConfiguration
        {
            Id = Path.GetFileName(destPath),
            BaseInstallationPath = sourcePath,
            WorkspaceRootPath = Path.GetDirectoryName(destPath) ?? destPath,
            Manifests = [], // Empty for this test
            GameClient = new GameClient { Id = "test" },
            Strategy = WorkspaceStrategy.HardLink,
        };

        // Act
        var result = await _validator.ValidatePrerequisitesAsync(hardLinkStrategy, config, default);

        // Assert
        Assert.NotNull(result);

        // The warning should appear if paths are on different volumes
        var volumeWarning = result.Issues.FirstOrDefault(i => i.Path == "VolumeCheck");
        if (Path.GetPathRoot(sourcePath) != Path.GetPathRoot(destPath))
        {
            Assert.NotNull(volumeWarning);
            Assert.Equal(ValidationSeverity.Warning, volumeWarning.Severity);
        }
    }

    /// <summary>
    /// Tests disk space validation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidatePrerequisitesAsync_InsufficientDiskSpace_ReturnsWarning()
    {
        // Arrange - Use a concrete strategy that can return large disk usage
        var fileOps = new Mock<IFileOperationsService>();
        var logger = new Mock<ILogger<FullCopyStrategy>>();
        var strategy = new FullCopyStrategy(fileOps.Object, logger.Object);

        // Create a configuration with large files to trigger disk space warning
        var largeFileManifest = new ContentManifest
        {
            Files =
            [
                new() { RelativePath = "huge.bin", Size = long.MaxValue / 2 },
            ],
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            Manifests = [largeFileManifest],
            Strategy = WorkspaceStrategy.FullCopy,
            BaseInstallationPath = _sourceDir,
            WorkspaceRootPath = Path.GetDirectoryName(_workspaceDir) ?? _workspaceDir,
            GameClient = new GameClient { Id = "test" },
        };

        // Mock EstimateDiskUsage to return a huge value by using the manifest
        var mockStrategyWithLargeUsage = new Mock<IWorkspaceStrategy>();
        mockStrategyWithLargeUsage.Setup(s => s.Name).Returns("Full Copy Strategy");
        mockStrategyWithLargeUsage.Setup(s => s.RequiresAdminRights).Returns(false);
        mockStrategyWithLargeUsage.Setup(s => s.RequiresSameVolume).Returns(false);
        mockStrategyWithLargeUsage.Setup(s => s.EstimateDiskUsage(It.IsAny<WorkspaceConfiguration>()))
                                  .Returns(long.MaxValue / 2);

        // Act
        var result = await _validator.ValidatePrerequisitesAsync(mockStrategyWithLargeUsage.Object, config, default);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.InsufficientSpace ||
                                           (i.Severity == ValidationSeverity.Warning && i.Message.Contains("disk space")));
    }

    /// <summary>
    /// An execute bit for an identity other than the effective process identity must not
    /// make a workspace entry point appear executable — validation repairs the entry
    /// point on a workspace-owned copy instead of reporting a warning.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateWorkspaceAsync_OtherOnlyExecuteBit_RepairsEntryPoint()
    {
        // Root bypasses the permission bits entirely: faccessat reports execute access for
        // an other-only bit, so the behaviour under test does not exist for uid 0. Checked
        // via geteuid rather than the user name, which is wrong under `sudo -E` and for any
        // uid-0 account named otherwise.
        if (OperatingSystem.IsWindows() || GetEffectiveUserId() == 0)
        {
            return;
        }

        var executablePath = Path.Combine(_workspaceDir, "client");
        await File.WriteAllTextAsync(executablePath, "engine binary");
        File.SetUnixFileMode(
            executablePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherExecute);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = executablePath,
        };

        var result = await _validator.ValidateWorkspaceAsync(workspaceInfo);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.DoesNotContain(
            result.Data.Issues,
            issue => issue.IssueType == ValidationIssueType.AccessDenied);
        Assert.True(File.GetUnixFileMode(executablePath).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("engine binary", await File.ReadAllTextAsync(executablePath));
    }

    /// <summary>
    /// A workspace bricked before executable modes were applied atomically — entry point
    /// present, execute bit lost — is repaired so launch preparation can proceed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureEntryPointExecutableAsync_BrickedEntryPoint_RestoresExecuteMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var executablePath = Path.Combine(_workspaceDir, "client");
        await File.WriteAllTextAsync(executablePath, "engine binary");
        File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = "client",
        };

        var result = await _validator.EnsureEntryPointExecutableAsync(workspaceInfo);

        Assert.True(result.Success);
        Assert.True(result.Data);
        Assert.True(File.GetUnixFileMode(executablePath).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("engine binary", await File.ReadAllTextAsync(executablePath));
        Assert.Empty(Directory.GetFiles(_workspaceDir, "*.genhub-exec-tmp-*"));
    }

    /// <summary>
    /// An entry point that is already executable is left alone.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureEntryPointExecutableAsync_AlreadyExecutable_ReportsNoRepair()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var executablePath = Path.Combine(_workspaceDir, "client");
        await File.WriteAllTextAsync(executablePath, "engine binary");
        var originalMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        File.SetUnixFileMode(executablePath, originalMode);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = executablePath,
        };

        var result = await _validator.EnsureEntryPointExecutableAsync(workspaceInfo);

        Assert.True(result.Success);
        Assert.False(result.Data);
        Assert.Equal(originalMode, File.GetUnixFileMode(executablePath));
    }

    /// <summary>
    /// A missing entry point is an error, not something repair may create.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureEntryPointExecutableAsync_MissingEntryPoint_FailsWithoutCreatingFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var executablePath = Path.Combine(_workspaceDir, "missing-client");

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = executablePath,
        };

        var result = await _validator.EnsureEntryPointExecutableAsync(workspaceInfo);

        Assert.False(result.Success);
        Assert.False(File.Exists(executablePath));
    }

    /// <summary>
    /// Validation keeps reporting a missing entry point as an error rather than creating one.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateWorkspaceAsync_MissingEntryPoint_ReportsErrorWithoutCreatingFile()
    {
        var executablePath = Path.Combine(_workspaceDir, "missing-client");

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = executablePath,
        };

        var result = await _validator.ValidateWorkspaceAsync(workspaceInfo);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains(
            result.Data.Issues,
            issue => issue.IssueType == ValidationIssueType.MissingFile
                && issue.Severity == ValidationSeverity.Error);
        Assert.False(File.Exists(executablePath));
    }

    /// <summary>
    /// A rooted entry point outside the workspace root is refused without being touched,
    /// even when the outside directory shares the root as a name prefix.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureEntryPointExecutableAsync_RootedPathOutsideWorkspace_RefusesWithoutMutation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // The sibling shares the workspace root as a prefix, so a containment check
        // without the trailing separator would wrongly accept it.
        var evilDir = _workspaceDir + "-evil";
        Directory.CreateDirectory(evilDir);
        var outsidePath = Path.Combine(evilDir, "client");
        await File.WriteAllTextAsync(outsidePath, "outside binary");
        var originalMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(outsidePath, originalMode);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = outsidePath,
        };

        var result = await _validator.EnsureEntryPointExecutableAsync(workspaceInfo);

        Assert.False(result.Success);
        Assert.Contains("outside the workspace root", result.FirstError);
        Assert.Equal(originalMode, File.GetUnixFileMode(outsidePath));
        Assert.Equal("outside binary", await File.ReadAllTextAsync(outsidePath));
    }

    /// <summary>
    /// A relative entry point that traverses out of the workspace root is refused
    /// without being touched.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureEntryPointExecutableAsync_TraversalPath_RefusesWithoutMutation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outsidePath = Path.Combine(_sourceDir, "client");
        await File.WriteAllTextAsync(outsidePath, "outside binary");
        var originalMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(outsidePath, originalMode);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine("..", "source", "client"),
        };

        var result = await _validator.EnsureEntryPointExecutableAsync(workspaceInfo);

        Assert.False(result.Success);
        Assert.Contains("outside the workspace root", result.FirstError);
        Assert.Equal(originalMode, File.GetUnixFileMode(outsidePath));
    }

    /// <summary>
    /// Strategies store the entry point as an absolute path inside the workspace, so a
    /// rooted in-workspace path must still be repaired.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureEntryPointExecutableAsync_RootedPathInsideWorkspace_StillRepairs()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var executablePath = Path.Combine(_workspaceDir, "client");
        await File.WriteAllTextAsync(executablePath, "engine binary");
        File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = executablePath,
        };

        var result = await _validator.EnsureEntryPointExecutableAsync(workspaceInfo);

        Assert.True(result.Success);
        Assert.True(result.Data);
        Assert.True(File.GetUnixFileMode(executablePath).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("engine binary", await File.ReadAllTextAsync(executablePath));
    }

    /// <summary>
    /// Validation reports an entry point that escapes the workspace root as an error and
    /// leaves the outside file untouched.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateWorkspaceAsync_EntryPointOutsideWorkspace_ReportsErrorWithoutMutation()
    {
        var outsidePath = Path.Combine(_sourceDir, "client");
        await File.WriteAllTextAsync(outsidePath, "outside binary");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(outsidePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = outsidePath,
        };

        var result = await _validator.ValidateWorkspaceAsync(workspaceInfo);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains(
            result.Data.Issues,
            issue => issue.IssueType == ValidationIssueType.UnexpectedFile
                && issue.Severity == ValidationSeverity.Error
                && issue.Message.Contains("outside the workspace root"));

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(File.GetUnixFileMode(outsidePath).HasFlag(UnixFileMode.UserExecute));
        }

        Assert.Equal("outside binary", await File.ReadAllTextAsync(outsidePath));
    }

    /// <summary>
    /// Lexical containment cannot see through links, so a symlinked intermediate
    /// directory pointing outside the workspace must make the repair refuse without
    /// touching the outside file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureEntryPointExecutableAsync_SymlinkedIntermediateDirectory_RefusesWithoutMutation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outsideDir = Path.Combine(_sourceDir, "payload");
        Directory.CreateDirectory(outsideDir);
        var outsidePath = Path.Combine(outsideDir, "client");
        await File.WriteAllTextAsync(outsidePath, "outside binary");
        var originalMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(outsidePath, originalMode);

        Directory.CreateSymbolicLink(Path.Combine(_workspaceDir, "bin"), outsideDir);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine("bin", "client"),
        };

        var result = await _validator.EnsureEntryPointExecutableAsync(workspaceInfo);

        Assert.False(result.Success);
        Assert.Contains("is a symlink", result.FirstError);
        Assert.Equal(originalMode, File.GetUnixFileMode(outsidePath));
        Assert.Equal("outside binary", await File.ReadAllTextAsync(outsidePath));
    }

    /// <summary>
    /// A symlinked leaf executable in an ordinary workspace is replaced with a private
    /// executable copy while the symlink target keeps its bytes and mode — the same
    /// store-safe behaviour materialisation applies.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureEntryPointExecutableAsync_SymlinkedLeafExecutable_RepairsCopyAndLeavesTargetUntouched()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var targetPath = Path.Combine(_sourceDir, "client-target");
        await File.WriteAllTextAsync(targetPath, "engine binary");
        var targetMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(targetPath, targetMode);

        var executablePath = Path.Combine(_workspaceDir, "client");
        File.CreateSymbolicLink(executablePath, targetPath);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = executablePath,
        };

        var result = await _validator.EnsureEntryPointExecutableAsync(workspaceInfo);

        Assert.True(result.Success);
        Assert.True(result.Data);
        Assert.Null(new FileInfo(executablePath).LinkTarget);
        Assert.True(File.GetUnixFileMode(executablePath).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("engine binary", await File.ReadAllTextAsync(executablePath));
        Assert.Equal(targetMode, File.GetUnixFileMode(targetPath));
        Assert.Equal("engine binary", await File.ReadAllTextAsync(targetPath));
    }

    /// <summary>
    /// Temporary swap names carry a fresh GUID, so a pre-existing file left at an
    /// old-style temporary name is never clobbered by a repair.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureEntryPointExecutableAsync_PreExistingTemporaryFile_IsNotClobbered()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var executablePath = Path.Combine(_workspaceDir, "client");
        await File.WriteAllTextAsync(executablePath, "engine binary");
        File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var stalePath = executablePath + ".genhub-exec-tmp";
        await File.WriteAllTextAsync(stalePath, "precious leftover");

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = executablePath,
        };

        var result = await _validator.EnsureEntryPointExecutableAsync(workspaceInfo);

        Assert.True(result.Success);
        Assert.True(result.Data);
        Assert.True(File.GetUnixFileMode(executablePath).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("precious leftover", await File.ReadAllTextAsync(stalePath));
    }

    /// <summary>
    /// Windows has no execute bit, so the repair is a no-op that leaves the file untouched.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureEntryPointExecutableAsync_OnWindows_IsANoOp()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var executablePath = Path.Combine(_workspaceDir, "client.exe");
        await File.WriteAllTextAsync(executablePath, "engine binary");
        var lastWrite = File.GetLastWriteTimeUtc(executablePath);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = executablePath,
        };

        var result = await _validator.EnsureEntryPointExecutableAsync(workspaceInfo);

        Assert.True(result.Success);
        Assert.False(result.Data);
        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(executablePath));
    }

    /// <summary>
    /// A workspace entry point executable by the current identity remains valid.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateWorkspaceAsync_ExecutableEntryPoint_HasNoAccessError()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var executablePath = Path.Combine(_workspaceDir, "client");
        await File.WriteAllTextAsync(executablePath, "engine binary");
        File.SetUnixFileMode(
            executablePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = executablePath,
        };

        var result = await _validator.ValidateWorkspaceAsync(workspaceInfo);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.DoesNotContain(
            result.Data.Issues,
            issue => issue.IssueType == ValidationIssueType.AccessDenied);
    }

    /// <summary>
    /// Disposes of test resources.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates a valid workspace configuration for testing.
    /// </summary>
    /// <returns>A valid workspace configuration.</returns>
    private WorkspaceConfiguration CreateValidConfiguration()
    {
        return new WorkspaceConfiguration
        {
            Id = "test-workspace",
            Manifests =
            [
                new()
                {
                    Files =
                    [
                        new() { RelativePath = "generals.exe", Size = 1000000, IsExecutable = true },
                        new() { RelativePath = "config.ini", Size = 500 },
                    ],
                },
            ],
            BaseInstallationPath = _sourceDir,
            WorkspaceRootPath = _workspaceDir,
            GameClient = new GameClient { Id = "test-version" },
            Strategy = WorkspaceStrategy.FullCopy,
        };
    }

    /// <summary>
    /// Effective user ID, POSIX <c>geteuid(2)</c>. Declared here because the production
    /// equivalent is internal to the GenHub assembly.
    /// </summary>
    /// <returns>The effective user ID; 0 is root.</returns>
    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();
}
