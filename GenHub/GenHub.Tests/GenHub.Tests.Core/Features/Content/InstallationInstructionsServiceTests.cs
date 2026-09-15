using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="InstallationInstructionsService"/>.
/// </summary>
public sealed class InstallationInstructionsServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IUserSettingsService> _userSettingsServiceMock;
    private readonly UserSettings _userSettings;
    private readonly InstallationInstructionsService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationInstructionsServiceTests"/> class.
    /// </summary>
    public InstallationInstructionsServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"genhub-inst-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _hashProviderMock = new Mock<IFileHashProvider>();
        _notificationServiceMock = new Mock<INotificationService>();
        _userSettingsServiceMock = new Mock<IUserSettingsService>();
        _userSettings = new UserSettings();

        _userSettingsServiceMock.Setup(u => u.Get()).Returns(_userSettings);
        _userSettingsServiceMock.Setup(u => u.Update(It.IsAny<Action<UserSettings>>()))
            .Callback<Action<UserSettings>>(action => action(_userSettings));

        _service = new InstallationInstructionsService(
            _hashProviderMock.Object,
            _notificationServiceMock.Object,
            _userSettingsServiceMock.Object,
            NullLogger<InstallationInstructionsService>.Instance);
    }

    /// <summary>
    /// Cleans up temporary resources after test execution.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup error
            }
        }
    }

    /// <summary>
    /// Verifies that executing post-install steps succeeds when no steps are declared.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_NullOrEmptySteps_ReturnsSuccess()
    {
        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions();

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory);

        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies that executing installer steps from an untrusted provider fails even if manifest metadata claims to be trusted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_UntrustedProvider_FailsExecution()
    {
        var manifest = CreateBaseManifest();
        manifest.Publisher = new PublisherInfo
        {
            Name = GeneralsOnlineConstants.PublisherName,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
        };
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Run Malicious Executable",
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = "malicious.exe",
                },
            ],
        };

        // Manifest claims GeneralsOnline, but providerSource is untrusted
        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: "untrusted_source");

        Assert.False(result.Success);
        Assert.Contains("not authorized to execute installation steps", result.FirstError);
    }

    /// <summary>
    /// Verifies that mutating steps like RemoveFile and RenameFile fail and do not modify files on disk when provider is untrusted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_UntrustedProvider_MutatingSteps_FailExecution()
    {
        var importantFilePath = Path.Combine(_tempDirectory, "important.dat");
        var sourceFilePath = Path.Combine(_tempDirectory, "source.dat");
        var destFilePath = Path.Combine(_tempDirectory, "dest.dat");

        await File.WriteAllTextAsync(importantFilePath, "important content");
        await File.WriteAllTextAsync(sourceFilePath, "source content");

        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Delete Something",
                    Kind = InstallationStepKind.RemoveFile,
                    TargetRelativePath = "important.dat",
                },
                new InstallationStep
                {
                    Name = "Rename Something",
                    Kind = InstallationStepKind.RenameFile,
                    TargetRelativePath = "source.dat",
                    DestinationRelativePath = "dest.dat",
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: "untrusted_source");

        Assert.False(result.Success);
        Assert.Contains("not authorized to execute installation steps", result.FirstError);
        Assert.True(File.Exists(importantFilePath));
        Assert.True(File.Exists(sourceFilePath));
        Assert.False(File.Exists(destFilePath));
    }

    /// <summary>
    /// Verifies that paths attempting directory traversal are rejected.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_PathTraversalTarget_FailsExecution()
    {
        var manifest = CreateBaseManifest();
        manifest.Publisher = new PublisherInfo
        {
            Name = GeneralsOnlineConstants.PublisherName,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
        };
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Traverse Path",
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = @"../../outside.exe",
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.Contains("escapes the working directory", result.FirstError);
    }

    /// <summary>
    /// Verifies that installer executables not declared in the manifest files list fail.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_FileNotInManifest_FailsExecution()
    {
        var targetFile = "installer.exe";
        var fullPath = Path.Combine(_tempDirectory, targetFile);
        File.WriteAllText(fullPath, "binary content");

        var manifest = CreateBaseManifest();
        manifest.Publisher = new PublisherInfo
        {
            Name = GeneralsOnlineConstants.PublisherName,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
        };
        manifest.Files = []; // Empty files list - installer not declared
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Run Undeclared Installer",
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = targetFile,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.Contains("not declared in manifest files", result.FirstError);
    }

    /// <summary>
    /// Verifies that hash mismatch during installer integrity check fails execution.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_HashMismatch_FailsExecution()
    {
        var targetFile = "installer.exe";
        var fullPath = Path.Combine(_tempDirectory, targetFile);
        File.WriteAllText(fullPath, "binary content");

        _hashProviderMock
            .Setup(h => h.ComputeFileHashAsync(fullPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync("actual_hash_value");

        var manifest = CreateBaseManifest();
        manifest.Publisher = new PublisherInfo
        {
            Name = GeneralsOnlineConstants.PublisherName,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
        };
        manifest.Files =
        [
            new ManifestFile
            {
                RelativePath = targetFile,
                Hash = "expected_different_hash",
            },
        ];
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Run Corrupted Installer",
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = targetFile,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.Contains("Integrity verification failed", result.FirstError);
    }

    /// <summary>
    /// Verifies that remove file steps successfully delete the target file.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_RemoveFile_DeletesTargetFile()
    {
        var fileToRemove = "temp_cache.tmp";
        var fullPath = Path.Combine(_tempDirectory, fileToRemove);
        File.WriteAllText(fullPath, "temporary content");

        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Remove Cache",
                    Kind = InstallationStepKind.RemoveFile,
                    TargetRelativePath = fileToRemove,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.True(result.Success);
        Assert.False(File.Exists(fullPath));
    }

    /// <summary>
    /// Verifies that rename file steps successfully move target files.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_RenameFile_MovesTargetFile()
    {
        var sourceFile = "source.txt";
        var destFile = Path.Combine("subfolder", "dest.txt");
        var sourceFullPath = Path.Combine(_tempDirectory, sourceFile);
        var destFullPath = Path.Combine(_tempDirectory, destFile);

        File.WriteAllText(sourceFullPath, "hello world");

        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Rename File",
                    Kind = InstallationStepKind.RenameFile,
                    TargetRelativePath = sourceFile,
                    DestinationRelativePath = destFile,
                    StepKey = "test_rename_step",
                    RunOnce = true,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.True(result.Success);
        Assert.False(File.Exists(sourceFullPath));
        Assert.True(File.Exists(destFullPath));
        Assert.Equal("hello world", File.ReadAllText(destFullPath));
        Assert.True(_userSettings.IsInstallationStepExecuted("test_rename_step"));
    }

    /// <summary>
    /// Verifies that verified installer execution runs and dispatches user notifications.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_RunsInstallerAndDispatchesNotification()
    {
        var scriptName = OperatingSystem.IsWindows() ? "test_installer.exe" : "test_installer.sh";
        var fullPath = Path.Combine(_tempDirectory, scriptName);

        if (OperatingSystem.IsWindows())
        {
            var systemCmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            File.Copy(systemCmd, fullPath, overwrite: true);
        }
        else
        {
            File.WriteAllText(fullPath, "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        const string expectedHash = "test_installer_hash";
        _hashProviderMock
            .Setup(h => h.ComputeFileHashAsync(fullPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedHash);

        var manifest = CreateBaseManifest();
        manifest.Publisher = new PublisherInfo
        {
            Name = GeneralsOnlineConstants.PublisherName,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
        };
        manifest.Files =
        [
            new ManifestFile
            {
                RelativePath = scriptName,
                Hash = expectedHash,
            },
        ];
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = GeneralsOnlineConstants.EacStepName,
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = scriptName,
                    Arguments = OperatingSystem.IsWindows() ? ["/c", "exit", "0"] : [],
                    StatusMessage = GeneralsOnlineConstants.EacStatusMessage,
                    StepKey = GeneralsOnlineConstants.EacStepKey,
                    RunOnce = true,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.True(result.Success);
        Assert.True(_userSettings.IsInstallationStepExecuted(GeneralsOnlineConstants.EacStepKey));
        _notificationServiceMock.Verify(
            n => n.ShowInfo(
                GeneralsOnlineConstants.EacStepName,
                GeneralsOnlineConstants.EacStatusMessage,
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
        _notificationServiceMock.Verify(
            n => n.ShowSuccess(
                "Installation Step Completed",
                It.Is<string>(msg => msg.Contains(GeneralsOnlineConstants.EacStepName)),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that run-once steps already recorded in user settings are skipped.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_RunOnceStepAlreadyExecuted_SkipsExecution()
    {
        var scriptName = "installer.bat";
        var manifest = CreateBaseManifest();
        manifest.Publisher = new PublisherInfo
        {
            Name = GeneralsOnlineConstants.PublisherName,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
        };
        manifest.Files =
        [
            new ManifestFile { RelativePath = scriptName },
        ];
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = GeneralsOnlineConstants.EacStepName,
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = scriptName,
                    StepKey = GeneralsOnlineConstants.EacStepKey,
                    RunOnce = true,
                },
            ],
        };

        // Mark as already executed
        _userSettings.RecordInstallationStepExecuted(GeneralsOnlineConstants.EacStepKey);

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.True(result.Success);

        // Notification should NOT be shown for skipped step
        _notificationServiceMock.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that forcing execution re-runs run-once steps even if recorded in settings.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_RunOnceStepWithForceTrue_ExecutesEvenIfRecorded()
    {
        var scriptName = OperatingSystem.IsWindows() ? "test_force_installer.exe" : "test_force_installer.sh";
        var fullPath = Path.Combine(_tempDirectory, scriptName);

        if (OperatingSystem.IsWindows())
        {
            var systemCmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            File.Copy(systemCmd, fullPath, overwrite: true);
        }
        else
        {
            File.WriteAllText(fullPath, "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        const string expectedHash = "test_force_hash";
        _hashProviderMock
            .Setup(h => h.ComputeFileHashAsync(fullPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedHash);

        var manifest = CreateBaseManifest();
        manifest.Publisher = new PublisherInfo
        {
            Name = GeneralsOnlineConstants.PublisherName,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
        };
        manifest.Files =
        [
            new ManifestFile
            {
                RelativePath = scriptName,
                Hash = expectedHash,
            },
        ];
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = GeneralsOnlineConstants.EacStepName,
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = scriptName,
                    Arguments = OperatingSystem.IsWindows() ? ["/c", "exit", "0"] : [],
                    StatusMessage = GeneralsOnlineConstants.EacStatusMessage,
                    StepKey = GeneralsOnlineConstants.EacStepKey,
                    RunOnce = true,
                },
            ],
        };

        // Mark as already executed in settings
        _userSettings.RecordInstallationStepExecuted(GeneralsOnlineConstants.EacStepKey);

        // Force execution
        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline, force: true);

        Assert.True(result.Success);
        _notificationServiceMock.Verify(
            n => n.ShowInfo(
                GeneralsOnlineConstants.EacStepName,
                GeneralsOnlineConstants.EacStatusMessage,
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that unknown installation step kinds return failure.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_UnknownKind_ReturnsFailure()
    {
        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Unknown Step",
                    Kind = InstallationStepKind.Unknown,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.Contains("Unsupported installation step kind", result.FirstError);
    }

    /// <summary>
    /// Verifies that elevated steps fail with an unsupported result on non-Windows platforms.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_ElevationOnNonWindows_ReturnsFailure()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var scriptName = "elevated_script.sh";
        var fullPath = Path.Combine(_tempDirectory, scriptName);
        File.WriteAllText(fullPath, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        const string expectedHash = "elevated_hash";
        _hashProviderMock
            .Setup(h => h.ComputeFileHashAsync(fullPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedHash);

        var manifest = CreateBaseManifest();
        manifest.Files =
        [
            new ManifestFile
            {
                RelativePath = scriptName,
                Hash = expectedHash,
            },
        ];
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Elevated Step",
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = scriptName,
                    RequiresElevation = true,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.Contains("requires administrator elevation, which is only supported on Windows", result.FirstError);
    }

    /// <summary>
    /// Verifies that remove file steps reject paths that escape the working directory.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_RemoveFile_PathTraversalTarget_FailsExecution()
    {
        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Remove Escape",
                    Kind = InstallationStepKind.RemoveFile,
                    TargetRelativePath = "../../outside.tmp",
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.Contains("escapes the working directory", result.FirstError);
    }

    /// <summary>
    /// Verifies that rename file steps reject source paths that escape the working directory.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_RenameFile_SourcePathTraversal_FailsExecution()
    {
        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Rename Source Escape",
                    Kind = InstallationStepKind.RenameFile,
                    TargetRelativePath = "../../outside.tmp",
                    DestinationRelativePath = "dest.tmp",
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.Contains("escapes the working directory", result.FirstError);
    }

    /// <summary>
    /// Verifies that rename file steps reject destination paths that escape the working directory.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_RenameFile_DestinationPathTraversal_FailsExecution()
    {
        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Rename Destination Escape",
                    Kind = InstallationStepKind.RenameFile,
                    TargetRelativePath = "source.tmp",
                    DestinationRelativePath = "../../outside.tmp",
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory, providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.Contains("escapes the working directory", result.FirstError);
    }

    /// <summary>
    /// Verifies that cancellation token terminates the running process and throws OperationCanceledException.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_CallerCancellation_TerminatesProcessAndThrows()
    {
        var scriptName = OperatingSystem.IsWindows() ? "sleep_installer.exe" : "sleep_installer.sh";
        var fullPath = Path.Combine(_tempDirectory, scriptName);

        if (OperatingSystem.IsWindows())
        {
            var systemCmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            File.Copy(systemCmd, fullPath, overwrite: true);
        }
        else
        {
            File.WriteAllText(fullPath, "#!/bin/sh\nsleep 30\n");
            File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        const string expectedHash = "sleep_hash";
        _hashProviderMock
            .Setup(h => h.ComputeFileHashAsync(fullPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedHash);

        var manifest = CreateBaseManifest();
        manifest.Files =
        [
            new ManifestFile
            {
                RelativePath = scriptName,
                Hash = expectedHash,
            },
        ];
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Long Running Step",
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = scriptName,
                    Arguments = OperatingSystem.IsWindows() ? ["/c", "ping", "-n", "30", "127.0.0.1"] : [],
                },
            ],
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.ExecutePostInstallStepsAsync(
                manifest,
                _tempDirectory,
                providerSource: PublisherTypeConstants.GeneralsOnline,
                cancellationToken: cts.Token));
    }

    /// <summary>
    /// Verifies that when a precondition is fulfilled, execution is skipped and the step key is recorded.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_PreconditionFulfilled_SkipsExecutionAndRecordsStepKey()
    {
        var preconditionMock = new Mock<IInstallationStepPrecondition>();
        preconditionMock.Setup(p => p.CanHandle(It.IsAny<InstallationStep>(), It.IsAny<ContentManifest>())).Returns(true);
        preconditionMock.Setup(p => p.IsAlreadyFulfilled(It.IsAny<InstallationStep>(), It.IsAny<ContentManifest>())).Returns(true);

        var serviceWithPrecondition = new InstallationInstructionsService(
            _hashProviderMock.Object,
            _notificationServiceMock.Object,
            _userSettingsServiceMock.Object,
            [preconditionMock.Object],
            NullLogger<InstallationInstructionsService>.Instance);

        const string stepKey = "test:precondition:step";
        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Preconditioned Step",
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = "nonexistent.exe",
                    StepKey = stepKey,
                    RunOnce = true,
                },
            ],
        };

        var result = await serviceWithPrecondition.ExecutePostInstallStepsAsync(
            manifest,
            _tempDirectory,
            providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.True(result.Success);
        Assert.True(_userSettings.IsInstallationStepExecuted(stepKey));
    }

    /// <summary>
    /// Verifies that verification fails when a step target file has no declared hash in the manifest.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_NoDeclaredHash_FailsVerification()
    {
        var scriptName = "installer_nohash.exe";
        var fullPath = Path.Combine(_tempDirectory, scriptName);
        File.WriteAllText(fullPath, "binary content");

        var manifest = CreateBaseManifest();
        manifest.Files =
        [
            new ManifestFile
            {
                RelativePath = scriptName,
                Hash = string.Empty,
            },
        ];
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "No Hash Step",
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = scriptName,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(
            manifest,
            _tempDirectory,
            providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.Contains("has no declared hash", result.FirstError);
    }

    /// <summary>
    /// Verifies that an installer process exiting with a non-zero exit code produces an execution failure.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_NonZeroExitCode_FailsExecution()
    {
        var scriptName = OperatingSystem.IsWindows() ? "exit_error.cmd" : "exit_error.sh";
        var fullPath = Path.Combine(_tempDirectory, scriptName);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(fullPath, "exit /b 42\r\n");
        }
        else
        {
            File.WriteAllText(fullPath, "#!/bin/sh\nexit 42\n");
            File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        const string expectedHash = "exit_error_hash";
        _hashProviderMock
            .Setup(h => h.ComputeFileHashAsync(fullPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedHash);

        var manifest = CreateBaseManifest();
        manifest.Files =
        [
            new ManifestFile
            {
                RelativePath = scriptName,
                Hash = expectedHash,
            },
        ];
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Failing Step",
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = scriptName,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(
            manifest,
            _tempDirectory,
            providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.Contains("failed with exit code", result.FirstError);
    }

    /// <summary>
    /// Verifies that a successful RunOnce step persists its key immediately even if a subsequent step fails.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_RunOnceStep_PersistsKeyImmediatelyEvenIfLaterStepFails()
    {
        var successFile = "success.tmp";
        var fullPath = Path.Combine(_tempDirectory, successFile);
        await File.WriteAllTextAsync(fullPath, "temporary");

        const string step1Key = "step:runonce:first";
        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Step 1 Remove",
                    Kind = InstallationStepKind.RemoveFile,
                    TargetRelativePath = successFile,
                    StepKey = step1Key,
                    RunOnce = true,
                },
                new InstallationStep
                {
                    Name = "Step 2 Unknown Kind",
                    Kind = InstallationStepKind.Unknown,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(
            manifest,
            _tempDirectory,
            providerSource: PublisherTypeConstants.GeneralsOnline);

        Assert.False(result.Success);
        Assert.False(File.Exists(fullPath));
        Assert.True(_userSettings.IsInstallationStepExecuted(step1Key));
        _userSettingsServiceMock.Verify(u => u.SaveAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// Verifies that an already-executed RunOnce step is skipped without failing provider authorization.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExecutePostInstallStepsAsync_RunOnceAlreadyExecuted_DoesNotFailAuthorizationForUntrustedProvider()
    {
        const string stepKey = "step:untrusted:runonce";
        _userSettings.RecordInstallationStepExecuted(stepKey);

        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = "Already Executed Step",
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = "installer.exe",
                    StepKey = stepKey,
                    RunOnce = true,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(
            manifest,
            _tempDirectory,
            providerSource: "untrusted_source");

        Assert.True(result.Success);
    }

    private static ContentManifest CreateBaseManifest() => new()
    {
        Id = "1.0.test.gameclient.variant",
        Name = "Test Manifest",
        Version = "1.0.0",
        ContentType = ContentType.GameClient,
        TargetGame = GameType.ZeroHour,
    };
}
