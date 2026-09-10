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
public sealed class ReplayCheckpointServiceTests
{
    private readonly Mock<IProfileLauncherFacade> _mockLauncherFacade = new();
    private readonly Mock<IGameProcessManager> _mockProcessManager = new();
    private readonly ReplayCheckpointService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayCheckpointServiceTests"/> class.
    /// </summary>
    public ReplayCheckpointServiceTests()
    {
        _service = new ReplayCheckpointService(
            _mockLauncherFacade.Object,
            _mockProcessManager.Object,
            NullLogger<ReplayCheckpointService>.Instance);
    }

    /// <summary>
    /// Verifies that GetSaveDirectory returns the native Save directory in Documents for both game types.
    /// </summary>
    [Fact]
    public void GetSaveDirectory_ReturnsSavePathUnderDocuments()
    {
        var zhPath = _service.GetSaveDirectory(GameType.ZeroHour);
        var genPath = _service.GetSaveDirectory(GameType.Generals);

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
            FullPath = "C:\\Games\\Replays\\Test.rep",
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
    /// Verifies that ResumeReplayAsync passes -loadsave and -resumereplay CLI arguments.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ResumeReplayAsync_PassesCorrectCliArguments()
    {
        var replay = new ReplayFile
        {
            FileName = "EpicBattle.rep",
            FullPath = "C:\\Games\\Replays\\EpicBattle.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-123", Name = "ZH Checkpoint Profile" };
        var checkpoint = new ReplayCheckpointInfo
        {
            FileName = "cp_12000.sav",
            TargetFrame = 12000,
            FilePath = "C:\\Games\\Save\\cp_12000.sav",
            CreatedAt = DateTime.UtcNow,
            FileSizeBytes = 4096,
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
        Assert.Equal("cp_12000.sav", capturedArgs[ReplayManagerConstants.CliLoadSave]);
        Assert.Equal("EpicBattle.rep", capturedArgs[ReplayManagerConstants.CliResumeReplay]);
    }

    /// <summary>
    /// Verifies that TakeoverMatchAsync passes -loadsave and -resumeas CLI arguments.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TakeoverMatchAsync_PassesCorrectCliArguments()
    {
        var replay = new ReplayFile
        {
            FileName = "EpicBattle.rep",
            FullPath = "C:\\Games\\Replays\\EpicBattle.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
        };
        var profile = new GameProfile { Id = "profile-123", Name = "ZH Checkpoint Profile" };
        var checkpoint = new ReplayCheckpointInfo
        {
            FileName = "cp_12000.sav",
            TargetFrame = 12000,
            FilePath = "C:\\Games\\Save\\cp_12000.sav",
            CreatedAt = DateTime.UtcNow,
            FileSizeBytes = 4096,
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
        Assert.Equal("cp_12000.sav", capturedArgs[ReplayManagerConstants.CliLoadSave]);
        Assert.Equal("2", capturedArgs[ReplayManagerConstants.CliResumeAs]);
    }

    /// <summary>
    /// Verifies that DeleteCheckpointAsync deletes the file and returns true.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task DeleteCheckpointAsync_ExistingFile_DeletesAndReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var checkpoint = new ReplayCheckpointInfo
            {
                FilePath = tempFile,
                FileName = Path.GetFileName(tempFile),
                TargetFrame = 5000,
                CreatedAt = DateTime.UtcNow,
                FileSizeBytes = 100,
            };

            Assert.True(File.Exists(tempFile));
            var deleted = await _service.DeleteCheckpointAsync(checkpoint);
            Assert.True(deleted);
            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
