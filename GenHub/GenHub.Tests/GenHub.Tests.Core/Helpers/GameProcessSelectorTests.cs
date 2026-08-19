using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Launching;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="GameProcessSelector"/>.
/// </summary>
public class GameProcessSelectorTests
{
    /// <summary>A real client whose name is longer than a Unix kernel will report.</summary>
    private const string LongClientName = "GeneralsOnlineZH_60";

    private static readonly DateTime Now = new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The name a Unix kernel reports for <see cref="LongClientName"/>.</summary>
    private static readonly string TruncatedClientName = LongClientName[..ProcessConstants.UnixProcessNameMaxLength];

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

    /// <summary>
    /// A Unix kernel keeps only <see cref="ProcessConstants.UnixProcessNameMaxLength"/> characters
    /// of a process name, so every client whose name is longer — which is most of the ones this
    /// adoption path exists for — reports a truncated name and the full one survives only in the
    /// image path. Matching on the reported name alone finds none of them.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_MatchesACandidateWhoseKernelTruncatedItsName()
    {
        var candidates = new[]
        {
            new GameProcessCandidate(1, TruncatedClientName, Now, Path.Combine(Workspace, LongClientName)),
        };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, LongClientName, Workspace, Now);

        Assert.NotNull(selected);
        Assert.Equal(1, selected.ProcessId);
    }

    /// <summary>
    /// Two clients that share a truncated name are still different clients, and the image path is
    /// what tells them apart. Matching on the truncated name alone would adopt either one.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_RejectsATruncatedNameBelongingToADifferentClient()
    {
        var otherClient = TruncatedClientName + "H_61";
        var candidates = new[]
        {
            new GameProcessCandidate(1, TruncatedClientName, Now, Path.Combine(Workspace, otherClient)),
        };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, LongClientName, Workspace, Now);

        Assert.Null(selected);
    }

    /// <summary>
    /// With no image path to read, the truncated name the kernel reports is the only evidence
    /// there is, so it has to be accepted where the kernel truncates and nowhere else.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_WithoutAnImagePath_FallsBackToTheTruncatedProcessName()
    {
        var candidates = new[] { new GameProcessCandidate(1, TruncatedClientName, Now, null) };

        var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, LongClientName, null, Now);

        Assert.Equal(!OperatingSystem.IsWindows(), selected is not null);
    }

    /// <summary>
    /// Enumeration matches against the name the kernel kept, so a longer name has to be shortened
    /// to the same prefix before it is asked for. Windows reports names in full.
    /// </summary>
    [Fact]
    public void GetDiscoveryName_ShortensNamesTheUnixKernelWouldTruncate()
    {
        var discoveryName = GameProcessSelector.GetDiscoveryName(LongClientName);

        Assert.Equal(OperatingSystem.IsWindows() ? LongClientName : TruncatedClientName, discoveryName);
    }

    /// <summary>
    /// A name the kernel keeps whole is asked for exactly as it is on every platform.
    /// </summary>
    [Fact]
    public void GetDiscoveryName_LeavesNamesTheKernelKeepsWhole()
    {
        Assert.Equal("generalszh", GameProcessSelector.GetDiscoveryName("generalszh"));
    }

    /// <summary>
    /// The operating system reports a fully symlink-resolved image path while a configured working
    /// directory keeps whatever spelling it was given, so residence has to be decided against the
    /// real directory rather than the two spellings of it.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_MatchesAWorkingDirectoryReachedThroughASymlink()
    {
        var root = CreateTempRoot();
        try
        {
            var real = Path.Combine(root, "real", "workspace");
            Directory.CreateDirectory(real);

            var link = Path.Combine(root, "link");
            if (!TryCreateDirectorySymbolicLink(link, Path.Combine(root, "real")))
            {
                // The platform will not let this account create links, so there is nothing to test.
                return;
            }

            var candidates = new[]
            {
                new GameProcessCandidate(1, LongClientName, Now, Path.Combine(real, LongClientName)),
            };

            var selected = GameProcessSelector.SelectSpawnedGameProcess(
                candidates, LongClientName, Path.Combine(link, "workspace"), Now);

            Assert.NotNull(selected);
            Assert.Equal(1, selected.ProcessId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Residence follows the volume rather than a fixed string rule: a case-insensitive volume —
    /// the macOS and Windows default — must not reject a differently cased spelling of the very
    /// directory the game runs from, and a case-sensitive one must keep two such directories apart.
    /// </summary>
    [Fact]
    public void SelectSpawnedGameProcess_FollowsTheVolumeCaseRulesWhenComparingResidence()
    {
        var root = CreateTempRoot();
        try
        {
            var onDisk = Path.Combine(root, "Workspace");
            Directory.CreateDirectory(onDisk);

            var lowerCased = Path.Combine(root, "workspace");
            var candidates = new[]
            {
                new GameProcessCandidate(1, LongClientName, Now, Path.Combine(onDisk, LongClientName)),
            };

            var selected = GameProcessSelector.SelectSpawnedGameProcess(
                candidates, LongClientName, lowerCased, Now);

            Assert.Equal(Directory.Exists(lowerCased), selected is not null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A launcher whose start time cannot be read leaves nothing to separate the child it spawned
    /// from an instance of the same game already running in the same workspace, so adoption is
    /// declined outright rather than gambling on the recency window.
    /// </summary>
    [Fact]
    public void SelectAdoptableGameProcess_WithoutALauncherStartTime_AdoptsNothing()
    {
        var candidates = new[] { Candidate(1, LongClientName, Now, Workspace) };

        var selected = GameProcessSelector.SelectAdoptableGameProcess(
            candidates, LongClientName, Workspace, launcherStartTime: null);

        Assert.Null(selected);
    }

    /// <summary>
    /// A known launcher start time disqualifies anything that was already running when the
    /// launcher started, however recently it started.
    /// </summary>
    [Fact]
    public void SelectAdoptableGameProcess_RejectsACandidateThatPredatesTheLauncher()
    {
        var launcherStartTime = Now.AddSeconds(-2);
        var candidates = new[] { Candidate(1, LongClientName, launcherStartTime.AddSeconds(-1), Workspace) };

        var selected = GameProcessSelector.SelectAdoptableGameProcess(
            candidates, LongClientName, Workspace, launcherStartTime);

        Assert.Null(selected);
    }

    /// <summary>
    /// The process the launcher started is the one adoption is for.
    /// </summary>
    [Fact]
    public void SelectAdoptableGameProcess_AdoptsTheChildStartedAfterTheLauncher()
    {
        var launcherStartTime = Now.AddSeconds(-2);
        var candidates = new[]
        {
            Candidate(1, LongClientName, launcherStartTime.AddSeconds(-1), Workspace),
            Candidate(2, LongClientName, launcherStartTime.AddSeconds(1), Workspace),
        };

        var selected = GameProcessSelector.SelectAdoptableGameProcess(
            candidates, LongClientName, Workspace, launcherStartTime);

        Assert.NotNull(selected);
        Assert.Equal(2, selected.ProcessId);
    }

    /// <summary>
    /// A child can be recorded as starting in the same clock tick as the launcher that spawned it,
    /// so the launcher's own start time has to qualify rather than disqualify.
    /// </summary>
    [Fact]
    public void SelectAdoptableGameProcess_AcceptsACandidateStartedAtTheLauncherStartTime()
    {
        var launcherStartTime = Now.AddSeconds(-2);
        var candidates = new[] { Candidate(1, LongClientName, launcherStartTime, Workspace) };

        var selected = GameProcessSelector.SelectAdoptableGameProcess(
            candidates, LongClientName, Workspace, launcherStartTime);

        Assert.NotNull(selected);
        Assert.Equal(1, selected.ProcessId);
    }

    /// <summary>
    /// A launcher may take longer than <see cref="ProcessConstants.EarlyExitThresholdSeconds"/> to
    /// make its child enumerable, and the discovery timeout the caller polls with is configurable
    /// well past that. The child still started with this launch, so it must be adopted rather than
    /// left running with nothing tracking it. Anchored to the real clock: the adoption path takes
    /// no time of its own, so any recency window reintroduced here would have to read that clock.
    /// </summary>
    [Fact]
    public void SelectAdoptableGameProcess_AdoptsAChildOlderThanTheRecencyWindow()
    {
        var launcherStartTime = DateTime.UtcNow.AddSeconds(-(ProcessConstants.EarlyExitThresholdSeconds + 20));
        var candidates = new[] { Candidate(1, LongClientName, launcherStartTime.AddSeconds(1), Workspace) };

        var selected = GameProcessSelector.SelectAdoptableGameProcess(
            candidates, LongClientName, Workspace, launcherStartTime);

        Assert.NotNull(selected);
        Assert.Equal(1, selected.ProcessId);
    }

    private static GameProcessCandidate Candidate(int id, string name, DateTime startTime, string directory) =>
        new(id, name, startTime, Path.Combine(directory, name + ".exe"));

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "genhub-selector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static bool TryCreateDirectorySymbolicLink(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
