using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="ReplayCheckpointService"/>.
/// </summary>
public sealed class ReplayCheckpointServiceTests : IDisposable
{
    private readonly Mock<IProfileLauncherFacade> _mockLauncherFacade = new();
    private readonly Mock<IGameProcessManager> _mockProcessManager = new();
    private readonly string _tempSaveDir;
    private readonly ReplayCheckpointService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayCheckpointServiceTests"/> class.
    /// </summary>
    public ReplayCheckpointServiceTests()
    {
        _tempSaveDir = Path.Combine(Path.GetTempPath(), "GenHubSaveTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempSaveDir);

        _mockProcessManager
            .Setup(p => p.TerminateProcessAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _service = new ReplayCheckpointService(
            _mockLauncherFacade.Object,
            _mockProcessManager.Object,
            NullLogger<ReplayCheckpointService>.Instance,
            _tempSaveDir);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempSaveDir))
        {
            try
            {
                Directory.Delete(_tempSaveDir, recursive: true);
            }
            catch
            {
                // Best effort test cleanup
            }
        }
    }

    /// <summary>
    /// Verifies that GetSaveDirectory returns the native Save directory in Documents for both game types when no override is provided.
    /// </summary>
    [Fact]
    public void GetSaveDirectory_ReturnsSavePathUnderDocuments()
    {
        var defaultService = new ReplayCheckpointService(
            _mockLauncherFacade.Object,
            _mockProcessManager.Object,
            NullLogger<ReplayCheckpointService>.Instance);

        var zhPath = defaultService.GetSaveDirectory(GameType.ZeroHour);
        var genPath = defaultService.GetSaveDirectory(GameType.Generals);

        Assert.Contains(GameSettingsConstants.FolderNames.ZeroHour, zhPath);
        Assert.EndsWith(ReplayManagerConstants.SaveFolderName, zhPath);

        Assert.Contains(GameSettingsConstants.FolderNames.Generals, genPath);
        Assert.EndsWith(ReplayManagerConstants.SaveFolderName, genPath);
    }

    /// <summary>
    /// Verifies that MintCheckpointAsync returns failure when targetFrame is zero or negative.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MintCheckpointAsync_TargetFrameNonPositive_ReturnsFailure()
    {
        var replay = new ReplayFile
        {
            FileName = "Test.rep",
            FullPath = @"C:\Games\Replays\Test.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "test-profile", Name = "Test Profile" };

        var result = await _service.MintCheckpointAsync(replay, profile, 0);

        Assert.False(result.Success);
        Assert.Contains("greater than zero", result.FirstError);
    }

    /// <summary>
    /// Verifies that MintCheckpointAsync successfully discovers the generated checkpoint save on exit.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MintCheckpointAsync_ProcessExitsAndSaveExists_ReturnsSuccess()
    {
        const int targetFrame = 88888;
        var saveFilePath = Path.Combine(_tempSaveDir, $"cp_Tournament_{targetFrame}.sav");

        var replay = new ReplayFile
        {
            FileName = "Tournament.rep",
            FullPath = @"C:\Games\Replays\Tournament.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 4096,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-zh", Name = "Zero Hour Profile" };

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync(
                profile.Id,
                false,
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Callback(() => File.WriteAllBytes(saveFilePath, [0x01, 0x02, 0x03]))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(new GameLaunchInfo
            {
                LaunchId = "launch-mint",
                ProfileId = profile.Id,
                WorkspaceId = "ws-mint",
                ProcessInfo = new GameProcessInfo
                {
                    ProcessId = 99999,
                    ProcessName = "generalszh",
                    StartTime = DateTime.UtcNow,
                },
            }));

        _mockProcessManager
            .Setup(p => p.GetProcessInfoAsync(99999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProcessInfo>.CreateFailure("Process has exited"));

        var result = await _service.MintCheckpointAsync(replay, profile, targetFrame);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(targetFrame, result.Data.TargetFrame);
        Assert.Equal($"cp_Tournament_{targetFrame}.sav", result.Data.FileName);
        Assert.Equal("Tournament.rep", result.Data.AssociatedReplayFileName);
    }

    /// <summary>
    /// Verifies that GetCheckpointsForReplayAsync filters by replay name and ignores non-matching files.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GetCheckpointsForReplayAsync_IgnoresNonMatchingSaveFiles()
    {
        var validFile = Path.Combine(_tempSaveDir, "cp_Sample_7777.sav");
        var otherReplayFile = Path.Combine(_tempSaveDir, "cp_Other_8888.sav");
        var nonCheckpointFile = Path.Combine(_tempSaveDir, "mycp_5.sav");

        await File.WriteAllBytesAsync(validFile, [1, 2]);
        await File.WriteAllBytesAsync(otherReplayFile, [5, 6]);
        await File.WriteAllBytesAsync(nonCheckpointFile, [3, 4]);

        var replay = new ReplayFile
        {
            FileName = "Sample.rep",
            FullPath = @"C:\Games\Replays\Sample.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
        };

        var checkpoints = await _service.GetCheckpointsForReplayAsync(replay);

        Assert.Contains(checkpoints, cp => cp.TargetFrame == 7777);
        Assert.DoesNotContain(checkpoints, cp => cp.TargetFrame == 8888);
        Assert.DoesNotContain(checkpoints, cp => cp.FileName == "mycp_5.sav");
    }

    /// <summary>
    /// Verifies that ResumeReplayAsync passes -quickstart, -loadsave, and -resumereplay CLI arguments.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ResumeReplayAsync_PassesCorrectCliArguments()
    {
        var replay = new ReplayFile
        {
            FileName = "EpicBattle.rep",
            FullPath = @"C:\Games\Replays\EpicBattle.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-123", Name = "ZH Checkpoint Profile" };
        var checkpoint = new ReplayCheckpointInfo
        {
            FileName = "cp_EpicBattle_12000.sav",
            TargetFrame = 12000,
            FilePath = Path.Combine(_tempSaveDir, "cp_EpicBattle_12000.sav"),
            CreatedAt = DateTime.UtcNow,
            FileSizeBytes = 4096,
            AssociatedReplayFileName = "EpicBattle.rep",
        };

        IReadOnlyDictionary<string, string>? capturedArgs = null;

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync(
                profile.Id,
                false,
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Callback<string, bool, CancellationToken, IReadOnlyDictionary<string, string>?>((id, skip, ct, args) => capturedArgs = args)
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(new GameLaunchInfo
            {
                LaunchId = "launch-1",
                ProfileId = profile.Id,
                WorkspaceId = "ws-1",
                ProcessInfo = new GameProcessInfo
                {
                    ProcessId = 1234,
                    ProcessName = "generalszh",
                    StartTime = DateTime.UtcNow,
                },
            }));

        var result = await _service.ResumeReplayAsync(replay, profile, checkpoint);

        Assert.True(result.Success);
        Assert.NotNull(capturedArgs);
        Assert.Equal(string.Empty, capturedArgs[ReplayManagerConstants.CliQuickStart]);
        Assert.Equal("cp_EpicBattle_12000.sav", capturedArgs[ReplayManagerConstants.CliLoadSave]);
        Assert.Equal("EpicBattle.rep", capturedArgs[ReplayManagerConstants.CliResumeReplay]);
    }

    /// <summary>
    /// Verifies that TakeoverMatchAsync passes -quickstart, -loadsave, and -resumeas CLI arguments.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TakeoverMatchAsync_PassesCorrectCliArguments()
    {
        var replay = new ReplayFile
        {
            FileName = "EpicBattle.rep",
            FullPath = @"C:\Games\Replays\EpicBattle.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-123", Name = "ZH Checkpoint Profile" };
        var checkpoint = new ReplayCheckpointInfo
        {
            FileName = "cp_EpicBattle_12000.sav",
            TargetFrame = 12000,
            FilePath = Path.Combine(_tempSaveDir, "cp_EpicBattle_12000.sav"),
            CreatedAt = DateTime.UtcNow,
            FileSizeBytes = 4096,
            AssociatedReplayFileName = "EpicBattle.rep",
        };

        IReadOnlyDictionary<string, string>? capturedArgs = null;

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync(
                profile.Id,
                false,
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Callback<string, bool, CancellationToken, IReadOnlyDictionary<string, string>?>((id, skip, ct, args) => capturedArgs = args)
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(new GameLaunchInfo
            {
                LaunchId = "launch-2",
                ProfileId = profile.Id,
                WorkspaceId = "ws-1",
                ProcessInfo = new GameProcessInfo
                {
                    ProcessId = 1234,
                    ProcessName = "generalszh",
                    StartTime = DateTime.UtcNow,
                },
            }));

        var result = await _service.TakeoverMatchAsync(replay, profile, checkpoint, 2);

        Assert.True(result.Success);
        Assert.NotNull(capturedArgs);
        Assert.Equal(string.Empty, capturedArgs[ReplayManagerConstants.CliQuickStart]);
        Assert.Equal("cp_EpicBattle_12000.sav", capturedArgs[ReplayManagerConstants.CliLoadSave]);
        Assert.Equal("2", capturedArgs[ReplayManagerConstants.CliResumeAs]);
    }

    /// <summary>
    /// Verifies that DeleteCheckpointAsync deletes the file and returns true.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task DeleteCheckpointAsync_ExistingFile_DeletesAndReturnsTrue()
    {
        var tempFile = Path.Combine(_tempSaveDir, "temp_delete_test.sav");
        await File.WriteAllBytesAsync(tempFile, [100]);

        var checkpoint = new ReplayCheckpointInfo
        {
            FilePath = tempFile,
            FileName = Path.GetFileName(tempFile),
            TargetFrame = 5000,
            CreatedAt = DateTime.UtcNow,
            FileSizeBytes = 1,
        };

        Assert.True(File.Exists(tempFile));
        var deleted = await _service.DeleteCheckpointAsync(checkpoint);
        Assert.True(deleted);
        Assert.False(File.Exists(tempFile));
    }

    /// <summary>
    /// Verifies that CancelActiveMint can be safely called when no minting is active.
    /// </summary>
    [Fact]
    public void CancelActiveMint_WhenNoActiveMint_DoesNotThrow()
    {
        var ex = Record.Exception(() => _service.CancelActiveMint());
        Assert.Null(ex);
    }

    /// <summary>
    /// Verifies that MintCheckpointAsync fails with a timeout error when the process does not exit in time
    /// and no save file was produced.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MintCheckpointAsync_ProcessTimesOut_ReturnsTimeoutFailure()
    {
        var serviceWithShortTimeout = new ReplayCheckpointService(
            _mockLauncherFacade.Object,
            _mockProcessManager.Object,
            NullLogger<ReplayCheckpointService>.Instance,
            customSaveDirectory: _tempSaveDir,
            mintTimeout: TimeSpan.FromMilliseconds(50));

        var replay = new ReplayFile
        {
            FileName = "TimeoutTest.rep",
            FullPath = @"C:\Games\Replays\TimeoutTest.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-timeout", Name = "Timeout Profile" };

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync(
                profile.Id,
                false,
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyDictionary<string, string>>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(new GameLaunchInfo
            {
                LaunchId = "launch-timeout",
                ProfileId = profile.Id,
                WorkspaceId = "ws-timeout",
                ProcessInfo = new GameProcessInfo
                {
                    ProcessId = 77777,
                    ProcessName = "generalszh",
                    StartTime = DateTime.UtcNow,
                },
            }));

        _mockProcessManager
            .Setup(p => p.GetProcessInfoAsync(77777, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProcessInfo>.CreateSuccess(new GameProcessInfo
            {
                ProcessId = 77777,
                ProcessName = "generalszh",
                StartTime = DateTime.UtcNow,
                IsRunning = true,
            }));

        var result = await serviceWithShortTimeout.MintCheckpointAsync(replay, profile, 5000);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.FirstError);
    }

    /// <summary>
    /// Verifies that MintCheckpointAsync successfully recovers the save file if it was created
    /// even if process monitoring times out.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MintCheckpointAsync_ProcessTimesOut_SaveFileExists_RecoversCheckpoint()
    {
        const int targetFrame = 6000;
        var saveFilePath = Path.Combine(_tempSaveDir, $"cp_RecoverTest_{targetFrame}.sav");

        var serviceWithShortTimeout = new ReplayCheckpointService(
            _mockLauncherFacade.Object,
            _mockProcessManager.Object,
            NullLogger<ReplayCheckpointService>.Instance,
            customSaveDirectory: _tempSaveDir,
            mintTimeout: TimeSpan.FromMilliseconds(50));

        var replay = new ReplayFile
        {
            FileName = "RecoverTest.rep",
            FullPath = @"C:\Games\Replays\RecoverTest.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-recover", Name = "Recover Profile" };

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync(
                profile.Id,
                false,
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Callback(() => File.WriteAllBytes(saveFilePath, [0x05, 0x06]))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(new GameLaunchInfo
            {
                LaunchId = "launch-recover",
                ProfileId = profile.Id,
                WorkspaceId = "ws-recover",
                ProcessInfo = new GameProcessInfo
                {
                    ProcessId = 66666,
                    ProcessName = "generalszh",
                    StartTime = DateTime.UtcNow,
                },
            }));

        _mockProcessManager
            .Setup(p => p.GetProcessInfoAsync(66666, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProcessInfo>.CreateSuccess(new GameProcessInfo
            {
                ProcessId = 66666,
                ProcessName = "generalszh",
                StartTime = DateTime.UtcNow,
                IsRunning = true,
            }));

        var result = await serviceWithShortTimeout.MintCheckpointAsync(replay, profile, targetFrame);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(targetFrame, result.Data.TargetFrame);
        Assert.Equal($"cp_RecoverTest_{targetFrame}.sav", result.Data.FileName);
    }

    /// <summary>
    /// Verifies that MintCheckpointAsync fails if a stale save file was pre-existing and not updated.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MintCheckpointAsync_PreExistingSaveNotUpdated_ReturnsFailure()
    {
        const int targetFrame = 4444;
        var saveFilePath = Path.Combine(_tempSaveDir, $"cp_Stale_{targetFrame}.sav");
        await File.WriteAllBytesAsync(saveFilePath, [0x01]);
        File.SetLastWriteTimeUtc(saveFilePath, DateTime.UtcNow.AddMinutes(-5));

        var replay = new ReplayFile
        {
            FileName = "Stale.rep",
            FullPath = @"C:\Games\Replays\Stale.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-stale", Name = "Stale Profile" };

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync(
                profile.Id,
                false,
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyDictionary<string, string>>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(new GameLaunchInfo
            {
                LaunchId = "launch-stale",
                ProfileId = profile.Id,
                WorkspaceId = "ws-stale",
                ProcessInfo = new GameProcessInfo
                {
                    ProcessId = 55555,
                    ProcessName = "generalszh",
                    StartTime = DateTime.UtcNow,
                },
            }));

        _mockProcessManager
            .Setup(p => p.GetProcessInfoAsync(55555, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProcessInfo>.CreateFailure("Process not found"));

        var result = await _service.MintCheckpointAsync(replay, profile, targetFrame);

        Assert.False(result.Success);
        Assert.Contains("stale file detected", result.FirstError);
    }

    /// <summary>
    /// Verifies that ResumeReplayAsync fails if the checkpoint belongs to a different replay.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ResumeReplayAsync_MismatchedReplayFileName_ReturnsFailure()
    {
        var replay = new ReplayFile
        {
            FileName = "ReplayA.rep",
            FullPath = @"C:\Games\Replays\ReplayA.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-1", Name = "Test Profile" };
        var checkpoint = new ReplayCheckpointInfo
        {
            FileName = "cp_ReplayB_5000.sav",
            TargetFrame = 5000,
            FilePath = Path.Combine(_tempSaveDir, "cp_ReplayB_5000.sav"),
            CreatedAt = DateTime.UtcNow,
            FileSizeBytes = 1024,
            AssociatedReplayFileName = "ReplayB.rep",
        };

        var result = await _service.ResumeReplayAsync(replay, profile, checkpoint);

        Assert.False(result.Success);
        Assert.Contains("does not belong to replay", result.FirstError);
    }

    /// <summary>
    /// Verifies that TakeoverMatchAsync fails if the checkpoint belongs to a different replay.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TakeoverMatchAsync_MismatchedReplayFileName_ReturnsFailure()
    {
        var replay = new ReplayFile
        {
            FileName = "ReplayA.rep",
            FullPath = @"C:\Games\Replays\ReplayA.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-1", Name = "Test Profile" };
        var checkpoint = new ReplayCheckpointInfo
        {
            FileName = "cp_ReplayB_5000.sav",
            TargetFrame = 5000,
            FilePath = Path.Combine(_tempSaveDir, "cp_ReplayB_5000.sav"),
            CreatedAt = DateTime.UtcNow,
            FileSizeBytes = 1024,
            AssociatedReplayFileName = "ReplayB.rep",
        };

        var result = await _service.TakeoverMatchAsync(replay, profile, checkpoint, 1);

        Assert.False(result.Success);
        Assert.Contains("does not belong to replay", result.FirstError);
    }

    /// <summary>
    /// Verifies that MintCheckpointAsync terminates the launched game process on timeout.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MintCheckpointAsync_ProcessTimesOut_TerminatesGameProcess()
    {
        var serviceWithShortTimeout = new ReplayCheckpointService(
            _mockLauncherFacade.Object,
            _mockProcessManager.Object,
            NullLogger<ReplayCheckpointService>.Instance,
            customSaveDirectory: _tempSaveDir,
            mintTimeout: TimeSpan.FromMilliseconds(50));

        var replay = new ReplayFile
        {
            FileName = "TimeoutKill.rep",
            FullPath = @"C:\Games\Replays\TimeoutKill.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-timeout", Name = "Timeout Profile" };

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync(
                profile.Id,
                false,
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyDictionary<string, string>>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(new GameLaunchInfo
            {
                LaunchId = "launch-timeout-kill",
                ProfileId = profile.Id,
                WorkspaceId = "ws-timeout-kill",
                ProcessInfo = new GameProcessInfo
                {
                    ProcessId = 88881,
                    ProcessName = "generalszh",
                    StartTime = DateTime.UtcNow,
                },
            }));

        _mockProcessManager
            .Setup(p => p.GetProcessInfoAsync(88881, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProcessInfo>.CreateSuccess(new GameProcessInfo
            {
                ProcessId = 88881,
                ProcessName = "generalszh",
                StartTime = DateTime.UtcNow,
                IsRunning = true,
            }));

        var result = await serviceWithShortTimeout.MintCheckpointAsync(replay, profile, 5000);

        Assert.False(result.Success);
        _mockProcessManager.Verify(p => p.TerminateProcessAsync(88881, CancellationToken.None), Times.Once);
    }
}
