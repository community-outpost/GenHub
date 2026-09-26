using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Verifies that deleting a profile removes its workspace, workspace CAS references, and user data.
/// </summary>
public sealed class GameProfileManagerDeletionCleanupTests : IDisposable
{
    private readonly ProfileDeletionFixture _fixture = new();

    /// <inheritdoc/>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Verifies that a direct profile delete leaves nothing behind after a reload.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteProfileAsync_RemovesWorkspaceRefsAndUserDataSoGcCanFreeObjectsAsync()
    {
        await _fixture.ArrangeProfileWithDataAsync();

        var result = await _fixture.CreateProfileManager().DeleteProfileAsync(_fixture.Profile.Id);

        Assert.True(result.Success, result.FirstError);
        await _fixture.AssertProfileAndDataRemovedAsync();
    }

    /// <summary>
    /// Verifies that the manifest scrub used by Settings "Delete manifests" cleans up the orphaned profiles it deletes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ScrubDeletedManifestReferencesAsync_WhenProfileOrphaned_RemovesWorkspaceRefsAndUserDataAsync()
    {
        await _fixture.ArrangeProfileWithDataAsync();

        var result = await _fixture.CreateProfileManager().ScrubDeletedManifestReferencesAsync([ProfileDeletionFixture.MapManifestId]);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(1, result.Data!.DeletedProfilesCount);
        Assert.Empty(result.Data.FailedProfileNames);
        await _fixture.AssertProfileAndDataRemovedAsync();
    }

    /// <summary>
    /// Verifies that references left behind by a workspace whose directory and metadata are already gone are removed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteProfileAsync_WhenWorkspaceAlreadyGone_RemovesLeftoverRefsAsync()
    {
        await _fixture.ArrangeProfileWithDataAsync();
        Directory.Delete(_fixture.WorkspacePath, recursive: true);
        _fixture.Profile.ActiveWorkspaceId = string.Empty;
        await _fixture.CreateRepository().SaveProfileAsync(_fixture.Profile);

        var result = await _fixture.CreateProfileManager().DeleteProfileAsync(_fixture.Profile.Id);

        Assert.True(result.Success, result.FirstError);
        await _fixture.AssertProfileAndDataRemovedAsync();
    }

    /// <summary>
    /// Verifies that a user data cleanup failure keeps the profile so the delete can be retried, and reports why.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteProfileAsync_WhenUserDataCannotBeRemoved_KeepsProfileAndReportsFailureAsync()
    {
        await _fixture.ArrangeProfileWithDataAsync();
        _fixture.FileOperations
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileHashVerification.Failed);

        var result = await _fixture.CreateProfileManager().DeleteProfileAsync(_fixture.Profile.Id);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("user data", StringComparison.OrdinalIgnoreCase));
        Assert.True((await _fixture.CreateRepository().LoadProfileAsync(_fixture.Profile.Id)).Success);
        Assert.True(File.Exists(_fixture.UserDataManifestPath));

        _fixture.FileOperations
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileHashVerification.Match);

        var retry = await _fixture.CreateProfileManager().DeleteProfileAsync(_fixture.Profile.Id);

        Assert.True(retry.Success, retry.FirstError);
        await _fixture.AssertProfileAndDataRemovedAsync();
    }

    /// <summary>
    /// Verifies that a running profile is not deleted and none of its data is touched.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteProfileAsync_WhenProfileRunning_RefusesAndKeepsDataAsync()
    {
        await _fixture.ArrangeProfileWithDataAsync();
        _fixture.LaunchRegistry
            .Setup(r => r.GetAllActiveLaunchesAsync())
            .ReturnsAsync(
            [
                new GameLaunchInfo
                {
                    LaunchId = "launch-1",
                    ProfileId = _fixture.Profile.Id,
                    WorkspaceId = _fixture.Profile.Id,
                    ProcessInfo = new GameProcessInfo { ProcessId = Environment.ProcessId },
                },
            ]);

        var result = await _fixture.CreateProfileManager().DeleteProfileAsync(_fixture.Profile.Id);

        Assert.False(result.Success);
        Assert.True((await _fixture.CreateRepository().LoadProfileAsync(_fixture.Profile.Id)).Success);
        Assert.True(Directory.Exists(_fixture.WorkspacePath));
        Assert.True(File.Exists(_fixture.WorkspaceRefsPath));
        Assert.True(File.Exists(_fixture.UserDataManifestPath));
        Assert.NotEqual(ProfileDeletionFixture.OriginalMapContent, await File.ReadAllTextAsync(_fixture.DeployedMapPath));
    }

    /// <summary>An exited process does not block deletion while its registry entry is stale.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task DeleteProfileAsync_StaleLaunchEntry_AllowsCleanupAsync()
    {
        await _fixture.ArrangeProfileWithDataAsync();
        _fixture.LaunchRegistry.Setup(r => r.GetAllActiveLaunchesAsync()).ReturnsAsync(
        [
            new GameLaunchInfo
            {
                LaunchId = "stale-launch",
                WorkspaceId = _fixture.Profile.Id,
                ProfileId = _fixture.Profile.Id,
                ProcessInfo = new GameProcessInfo { ProcessId = int.MaxValue },
            },
        ]);

        var result = await _fixture.CreateProfileManager().DeleteProfileAsync(_fixture.Profile.Id);

        Assert.True(result.Success, result.FirstError);
        await _fixture.AssertProfileAndDataRemovedAsync();
    }

    /// <summary>Workspace cleanup propagates a cancelled token rather than returning a failure.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task CleanupWorkspaceAsync_Cancelled_ThrowsAsync()
    {
        await _fixture.ArrangeProfileWithDataAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _fixture.CreateWorkspaceManager().CleanupWorkspaceAsync(_fixture.Profile.Id, cts.Token));
        Assert.True(Directory.Exists(_fixture.WorkspacePath));
    }

    /// <summary>
    /// Verifies that a cancelled delete throws and keeps the profile.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteProfileAsync_WhenCancelled_ThrowsAndKeepsProfileAsync()
    {
        await _fixture.ArrangeProfileWithDataAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _fixture.CreateProfileManager().DeleteProfileAsync(_fixture.Profile.Id, cts.Token));

        Assert.True((await _fixture.CreateRepository().LoadProfileAsync(_fixture.Profile.Id)).Success);
    }
}
