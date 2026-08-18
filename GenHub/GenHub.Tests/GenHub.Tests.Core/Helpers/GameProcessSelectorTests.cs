using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Launching;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="GameProcessSelector"/>.
/// </summary>
public class GameProcessSelectorTests
{
    private static readonly DateTime Now = new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);

    // Native separators on both platforms: a real workspace path never mixes them, and comparing
    // like-for-like is what the non-separator tests are meant to exercise.
    private static readonly string Workspace = Path.Combine(Path.GetTempPath(), "genhub-workspace", "generalsonline");

    /// <summary>
    /// The spawned game is identified by the name the caller expects, not by the launcher's name.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_MatchesTheExpectedNameCaseInsensitively()
    {
        var candidates = new[]
        {
            Candidate(1, "EAC_LaunchGeneralsOnline", Now.AddSeconds(-2), Workspace),
            Candidate(2, "GENERALSONLINEZH_60", Now.AddSeconds(-1), Workspace),
        };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, "generalsonlinezh_60", Workspace, Now);

        Assert.NotNull(selected);
        Assert.Equal(2, selected.ProcessId);
    }

    /// <summary>
    /// A same-named process that predates the launch is somebody else's, not the child we spawned.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_RejectsCandidatesStartedBeforeTheRecencyWindow()
    {
        var stale = Now.AddSeconds(-(ProcessConstants.EarlyExitThresholdSeconds + 1));
        var candidates = new[] { Candidate(1, "GeneralsOnlineZH_60", stale, Workspace) };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, "GeneralsOnlineZH_60", Workspace, Now);

        Assert.Null(selected);
    }

    /// <summary>
    /// A process that was already running when the launcher started cannot be the child it
    /// spawned. On Unix this filter is the only thing separating an adoptable child from an
    /// instance of the same game the user started earlier from the same workspace.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_RejectsCandidatesStartedBeforeTheLauncher()
    {
        var launcherStartTime = Now.AddSeconds(-2);
        var candidates = new[] { Candidate(1, "GeneralsOnlineZH_60", launcherStartTime.AddSeconds(-1), Workspace) };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(
            candidates, "GeneralsOnlineZH_60", Workspace, Now, launcherStartTime);

        Assert.Null(selected);
    }

    /// <summary>
    /// A child can be recorded as starting in the same clock tick as the launcher that spawned it,
    /// so the launcher's own start time has to qualify rather than disqualify.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_AcceptsACandidateStartedAtTheLauncherStartTime()
    {
        var launcherStartTime = Now.AddSeconds(-2);
        var candidates = new[] { Candidate(1, "GeneralsOnlineZH_60", launcherStartTime, Workspace) };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(
            candidates, "GeneralsOnlineZH_60", Workspace, Now, launcherStartTime);

        Assert.NotNull(selected);
        Assert.Equal(1, selected.ProcessId);
    }

    /// <summary>
    /// Workspace residence must be required even when only one candidate matches the name — a lone
    /// same-named process anywhere on the machine used to be accepted unconditionally.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_RejectsALoneCandidateOutsideTheWorkingDirectory()
    {
        var candidates = new[] { Candidate(1, "GeneralsOnlineZH_60", Now, "/somewhere/else") };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, "GeneralsOnlineZH_60", Workspace, Now);

        Assert.Null(selected);
    }

    /// <summary>
    /// Residence cannot be proven for a process whose image path is unreadable, so it is not
    /// accepted while a working directory is being enforced.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_RejectsCandidatesWithAnUnknownExecutablePath()
    {
        var candidates = new[] { new GameProcessCandidate(1, "GeneralsOnlineZH_60", Now, null) };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, "GeneralsOnlineZH_60", Workspace, Now);

        Assert.Null(selected);
    }

    /// <summary>
    /// With no working directory to enforce, name and recency are the only available evidence.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_WithoutAWorkingDirectory_AcceptsOnNameAndRecency()
    {
        var candidates = new[] { new GameProcessCandidate(1, "GeneralsOnlineZH_60", Now, null) };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, "GeneralsOnlineZH_60", null, Now);

        Assert.NotNull(selected);
        Assert.Equal(1, selected.ProcessId);
    }

    /// <summary>
    /// When several qualify, the newest is the one this launch just spawned.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_PrefersTheMostRecentlyStartedCandidate()
    {
        var candidates = new[]
        {
            Candidate(1, "GeneralsOnlineZH_60", Now.AddSeconds(-5), Workspace),
            Candidate(2, "GeneralsOnlineZH_60", Now.AddSeconds(-1), Workspace),
            Candidate(3, "GeneralsOnlineZH_60", Now.AddSeconds(-3), Workspace),
        };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, "GeneralsOnlineZH_60", Workspace, Now);

        Assert.NotNull(selected);
        Assert.Equal(2, selected.ProcessId);
    }

    /// <summary>
    /// A trailing separator on the working directory is a formatting difference, not a mismatch.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_IgnoresTrailingSeparatorsOnTheWorkingDirectory()
    {
        var candidates = new[] { Candidate(1, "GeneralsOnlineZH_60", Now, Workspace) };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(
            candidates, "GeneralsOnlineZH_60", Workspace + Path.DirectorySeparatorChar, Now);

        Assert.NotNull(selected);
        Assert.Equal(1, selected.ProcessId);
    }

    /// <summary>
    /// Separator style is a spelling difference, not a location difference. Windows accepts both
    /// forms, so a working directory and a process image path can legitimately disagree on which
    /// one they use and still name the same directory. Only discriminating on Windows: elsewhere
    /// both separator constants are '/', and a backslash is a legal file name character that must
    /// not be treated as a separator.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_IgnoresSeparatorStyleWhenComparingResidence()
    {
        var candidates = new[] { Candidate(1, "GeneralsOnlineZH_60", Now, Workspace) };
        var alternateSpelling = Workspace.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var selected = GameProcessSelector.SelectSpawnedGameProcess(
            candidates, "GeneralsOnlineZH_60", alternateSpelling, Now);

        Assert.NotNull(selected);
        Assert.Equal(1, selected.ProcessId);
    }

    /// <summary>
    /// Nothing matching the expected name means no adoption.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_WithNoNameMatch_ReturnsNull()
    {
        var candidates = new[] { Candidate(1, "EAC_LaunchGeneralsOnline", Now, Workspace) };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, "GeneralsOnlineZH_60", Workspace, Now);

        Assert.Null(selected);
    }

    private static GameProcessCandidate Candidate(int id, string name, DateTime startTime, string directory) =>
        new(id, name, startTime, Path.Combine(directory, name + ".exe"));
}
