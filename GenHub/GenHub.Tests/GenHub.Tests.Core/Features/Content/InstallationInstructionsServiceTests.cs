using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="InstallationInstructionsService"/>.
/// </summary>
public sealed class InstallationInstructionsServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly InstallationInstructionsService _service;

    public InstallationInstructionsServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"genhub-inst-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _hashProviderMock = new Mock<IFileHashProvider>();
        _notificationServiceMock = new Mock<INotificationService>();

        _service = new InstallationInstructionsService(
            _hashProviderMock.Object,
            _notificationServiceMock.Object,
            NullLogger<InstallationInstructionsService>.Instance);
    }

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

    [Fact]
    public async Task ExecutePostInstallStepsAsync_NullOrEmptySteps_ReturnsSuccess()
    {
        var manifest = CreateBaseManifest();
        manifest.InstallationInstructions = new InstallationInstructions();

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecutePostInstallStepsAsync_UntrustedPublisher_FailsExecution()
    {
        var manifest = CreateBaseManifest();
        manifest.Publisher = new PublisherInfo
        {
            Name = "Untrusted Publisher",
            PublisherType = "untrusted_source",
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

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory);

        Assert.False(result.Success);
        Assert.Contains("not authorized to execute installation steps", result.FirstError);
    }

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

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory);

        Assert.False(result.Success);
        Assert.Contains("escapes the working directory", result.FirstError);
    }

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

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory);

        Assert.False(result.Success);
        Assert.Contains("not declared in manifest files", result.FirstError);
    }

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

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory);

        Assert.False(result.Success);
        Assert.Contains("Integrity verification failed", result.FirstError);
    }

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

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory);

        Assert.True(result.Success);
        Assert.False(File.Exists(fullPath));
    }

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
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory);

        Assert.True(result.Success);
        Assert.False(File.Exists(sourceFullPath));
        Assert.True(File.Exists(destFullPath));
        Assert.Equal("hello world", File.ReadAllText(destFullPath));
    }

    [Fact]
    public async Task ExecutePostInstallStepsAsync_RunsInstallerAndDispatchesNotification()
    {
        var scriptName = OperatingSystem.IsWindows() ? "test_installer.bat" : "test_installer.sh";
        var fullPath = Path.Combine(_tempDirectory, scriptName);
        var scriptContent = OperatingSystem.IsWindows() ? "@exit 0" : "#!/bin/sh\nexit 0\n";
        File.WriteAllText(fullPath, scriptContent);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

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
                    StatusMessage = GeneralsOnlineConstants.EacStatusMessage,
                },
            ],
        };

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory);

        Assert.True(result.Success);
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

        var result = await _service.ExecutePostInstallStepsAsync(manifest, _tempDirectory);

        Assert.False(result.Success);
        Assert.Contains("Unsupported installation step kind", result.FirstError);
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
