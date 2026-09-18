using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for the pre-spawn retail archive root check.
/// </summary>
/// <remarks>
/// When these roots are wrong the engine aborts during initialisation with a generic
/// crash that names nothing the user can act on. Everything here is about refusing to
/// spawn and naming the offending root instead.
/// </remarks>
public class RetailArchiveRootValidationTests : IDisposable
{
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetailArchiveRootValidationTests"/> class.
    /// </summary>
    public RetailArchiveRootValidationTests()
    {
        _tempDir = Directory.CreateTempSubdirectory("GenHub.ArchiveRootTests.").FullName;
    }

    /// <summary>
    /// A root holding archives is accepted.
    /// </summary>
    [Fact]
    public void Validate_WithArchivesPresent_Accepts()
    {
        var root = CreateRoot("valid", withArchive: true);

        Assert.Null(Validate(InstallationWithZeroHour(root)));
    }

    /// <summary>
    /// A stale installation path must be rejected. This is the case the earlier
    /// implementation missed: the environment builder drops a nonexistent path, so
    /// validating only the environment skipped it and the launch proceeded.
    /// </summary>
    [Fact]
    public void Validate_WithNonexistentRoot_RejectsRatherThanSkipping()
    {
        // Rejection is non-Windows behaviour by design: Windows resolves install paths from
        // the registry and never reads these variables, so validation skips there. The
        // Windows side is asserted by Validate_OnWindows_SkipsEntirely.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var missing = Path.Combine(_tempDir, "gone");

        var error = Validate(InstallationWithZeroHour(missing));

        Assert.NotNull(error);
        Assert.Contains(missing, error);
    }

    /// <summary>
    /// A root that exists but holds no archives would produce an empty game.
    /// </summary>
    [Fact]
    public void Validate_WithNoArchives_Rejects()
    {
        // Rejection is non-Windows behaviour by design: Windows resolves install paths from
        // the registry and never reads these variables, so validation skips there. The
        // Windows side is asserted by Validate_OnWindows_SkipsEntirely.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var root = CreateRoot("empty", withArchive: false);

        var error = Validate(InstallationWithZeroHour(root));

        Assert.NotNull(error);
        Assert.Contains("no .big archives", error);
    }

    /// <summary>
    /// An unreadable root is reported rather than treated as archive-free, so the message
    /// points at the permission rather than at the content.
    /// </summary>
    [Fact]
    public void Validate_WithUnreadableRoot_ReportsTheReadFailure()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Environment.UserName == "root")
        {
            return;
        }

        var root = CreateRoot("unreadable", withArchive: true);
        File.SetUnixFileMode(root, UnixFileMode.UserWrite);

        try
        {
            var error = Validate(InstallationWithZeroHour(root));

            Assert.NotNull(error);
            Assert.Contains("could not be read", error);
        }
        finally
        {
            File.SetUnixFileMode(
                root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// A game that is simply not installed declares no path and is not an error.
    /// </summary>
    [Fact]
    public void Validate_WithNoDeclaredPath_Accepts()
    {
        Assert.Null(Validate(InstallationWithZeroHour(null)));
    }

    /// <summary>
    /// Retail data copied from a disc or a Windows machine is frequently upper-cased. On a
    /// case-sensitive volume a case-sensitive glob finds nothing, so a valid root is rejected
    /// and the launch blocked — the opposite of what this check exists to do.
    /// </summary>
    /// <remarks>
    /// Only exercises the regression on a case-sensitive volume, which in practice means Linux
    /// CI. The <see cref="SearchOption"/> overload this replaced matches with
    /// <see cref="MatchCasing.PlatformDefault"/>, and that is already case-insensitive on macOS
    /// and Windows — so this passes there whether or not the fix is present. Verified by
    /// reverting the fix locally and watching it still pass.
    /// </remarks>
    [Fact]
    public void Validate_WithUpperCasedArchive_Accepts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var root = Directory.CreateDirectory(Path.Combine(_tempDir, "uppercased")).FullName;
        File.WriteAllText(Path.Combine(root, "INIZH.BIG"), "archive");
        File.WriteAllText(Path.Combine(root, "TEXTURES.BIG"), "archive");

        Assert.Null(Validate(InstallationWithZeroHour(root)));
    }

    /// <summary>
    /// Disposes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // A test left a directory unreadable; not worth failing the run over.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Windows never reads these variables — it resolves install paths from the registry —
    /// so a layout without loose top-level archives must not fail the launch there.
    /// </summary>
    [Fact]
    public void Validate_OnWindows_SkipsEntirely()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var root = CreateRoot("windows-no-archives", withArchive: false);

        Assert.Null(Validate(InstallationWithZeroHour(root)));
    }

    /// <summary>
    /// A profile that sets the root explicitly still gets the trailing separator. The engine
    /// concatenates this value with the archive filename directly, so without one it builds
    /// paths like "/path/toINIZH.big" and silently mounts nothing — the failure this whole
    /// check exists to prevent.
    /// </summary>
    [Fact]
    public void AddArchiveRoot_WithProfileOverrideMissingSeparator_StillNormalizes()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var root = CreateRoot("profile-override", withArchive: true).TrimEnd(Path.DirectorySeparatorChar);
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.ZeroHourInstallPathVariable] = root,
        };

        BuildEnvironment(environment, InstallationWithZeroHour(root));

        Assert.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            environment[RetailArchiveConstants.ZeroHourInstallPathVariable]);
        Assert.StartsWith(root, environment[RetailArchiveConstants.ZeroHourInstallPathVariable]);
    }

    /// <summary>
    /// Zero Hour is an expansion and mounts the base Generals archives too, so a declared
    /// Generals root that is broken must fail the launch — otherwise the game runs without
    /// base content, the same silent failure one directory over.
    /// </summary>
    [Fact]
    public void Validate_LaunchingZeroHour_AlsoRejectsABrokenGeneralsRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var zeroHour = CreateRoot("zh-ok", withArchive: true);
        var generals = CreateRoot("gen-empty", withArchive: false);

        var error = ValidateFor(GameType.ZeroHour, generals, zeroHour);

        Assert.NotNull(error);
        Assert.Contains(generals, error);
    }

    /// <summary>
    /// A Zero Hour installation carrying the base archives itself declares no separate
    /// Generals root, and that is not an error.
    /// </summary>
    [Fact]
    public void Validate_LaunchingZeroHour_WithNoGeneralsRootDeclared_Accepts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var zeroHour = CreateRoot("zh-standalone", withArchive: true);

        Assert.Null(ValidateFor(GameType.ZeroHour, null, zeroHour));
    }

    /// <summary>
    /// When Zero Hour has an empty bundled ZH_Generals directory, it is not treated as a valid
    /// archive root, and when no other Generals root is declared, validation accepts the launch
    /// so that base content in the workspace can still be mounted by the engine.
    /// </summary>
    [Fact]
    public void Validate_LaunchingZeroHour_WithEmptyBundledZhGenerals_Accepts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var zeroHour = Directory.CreateDirectory(Path.Combine(_tempDir, "zh-steam-empty-bundled")).FullName;
        File.WriteAllText(Path.Combine(zeroHour, "INIZH.big"), "archive");
        Directory.CreateDirectory(Path.Combine(zeroHour, GameClientConstants.ZhGeneralsDirectory));

        var error = ValidateFor(GameType.ZeroHour, null, zeroHour);

        Assert.Null(error);
    }

    /// <summary>
    /// AddRetailArchiveRoots does not populate CNC_GENERALS_INSTALLPATH when bundled ZH_Generals contains no archives.
    /// </summary>
    [Fact]
    public void AddRetailArchiveRoots_ForZeroHour_WithEmptyBundledZhGenerals_DoesNotSetGeneralsInstallPath()
    {
        var zeroHour = Directory.CreateDirectory(Path.Combine(_tempDir, "zh-steam-empty-env")).FullName;
        Directory.CreateDirectory(Path.Combine(zeroHour, GameClientConstants.ZhGeneralsDirectory));

        var installation = new GameInstallation(
            Path.GetTempPath(),
            GameInstallationType.Steam,
            new Mock<ILogger<GameInstallation>>().Object);
        installation.SetPaths(null, zeroHour);

        var env = new Dictionary<string, string>();
        BuildEnvironment(env, installation);

        Assert.False(env.ContainsKey(RetailArchiveConstants.GeneralsInstallPathVariable));
    }

    /// <summary>
    /// When Zero Hour has no separate Generals root, but has a bundled ZH_Generals directory with base archives,
    /// validation accepts.
    /// </summary>
    [Fact]
    public void Validate_LaunchingZeroHour_WithBundledZhGenerals_Accepts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var zeroHour = Directory.CreateDirectory(Path.Combine(_tempDir, "zh-steam")).FullName;
        File.WriteAllText(Path.Combine(zeroHour, "INIZH.big"), "archive");
        var zhGenerals = Directory.CreateDirectory(Path.Combine(zeroHour, GameClientConstants.ZhGeneralsDirectory)).FullName;
        File.WriteAllText(Path.Combine(zhGenerals, "Textures.big"), "base archive");

        Assert.Null(ValidateFor(GameType.ZeroHour, null, zeroHour));
    }

    /// <summary>
    /// AddRetailArchiveRoots populates CNC_GENERALS_INSTALLPATH from bundled ZH_Generals when separate Generals root is null.
    /// </summary>
    [Fact]
    public void AddRetailArchiveRoots_ForZeroHour_WithBundledZhGenerals_SetsGeneralsInstallPath()
    {
        var zeroHour = Directory.CreateDirectory(Path.Combine(_tempDir, "zh-steam-env")).FullName;
        var zhGenerals = Directory.CreateDirectory(Path.Combine(zeroHour, GameClientConstants.ZhGeneralsDirectory)).FullName;
        File.WriteAllText(Path.Combine(zhGenerals, "Textures.big"), "base archive");

        var installation = new GameInstallation(
            Path.GetTempPath(),
            GameInstallationType.Steam,
            new Mock<ILogger<GameInstallation>>().Object);
        installation.SetPaths(null, zeroHour);

        var env = new Dictionary<string, string>();
        BuildEnvironment(env, installation);

        Assert.True(env.ContainsKey(RetailArchiveConstants.GeneralsInstallPathVariable));
        Assert.StartsWith(zhGenerals, env[RetailArchiveConstants.GeneralsInstallPathVariable]);
    }

    /// <summary>
    /// AddRetailArchiveRoots prioritizes an explicit Generals path over bundled ZH_Generals when both are present.
    /// </summary>
    [Fact]
    public void AddRetailArchiveRoots_ForZeroHour_WithExplicitGeneralsPath_PrefersExplicitPath()
    {
        var generals = Directory.CreateDirectory(Path.Combine(_tempDir, "gen-explicit")).FullName;
        var zeroHour = Directory.CreateDirectory(Path.Combine(_tempDir, "zh-steam-both")).FullName;
        var zhGenerals = Directory.CreateDirectory(Path.Combine(zeroHour, GameClientConstants.ZhGeneralsDirectory)).FullName;

        var installation = new GameInstallation(
            Path.GetTempPath(),
            GameInstallationType.Steam,
            new Mock<ILogger<GameInstallation>>().Object);
        installation.SetPaths(generals, zeroHour);

        var env = new Dictionary<string, string>();
        BuildEnvironment(env, installation);

        Assert.True(env.ContainsKey(RetailArchiveConstants.GeneralsInstallPathVariable));
        Assert.StartsWith(generals, env[RetailArchiveConstants.GeneralsInstallPathVariable]);
    }

    /// <summary>
    /// Launching Generals must not fail over a stale Zero Hour root: it does not read it.
    /// </summary>
    [Fact]
    public void Validate_LaunchingGenerals_IgnoresABrokenZeroHourRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var generals = CreateRoot("gen-ok", withArchive: true);
        var staleZeroHour = Path.Combine(_tempDir, "zh-gone");

        Assert.Null(ValidateFor(GameType.Generals, generals, staleZeroHour));
    }

    // Zero Hour throughout: these fixtures declare only a Zero Hour path, and validation is
    // scoped to the launching game so the sibling Generals root is deliberately untouched.
    private static string? Validate(GameInstallation installation) =>
        (string?)typeof(GameLauncher)
            .GetMethod("ValidateRetailArchiveRoots", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new Dictionary<string, string>(), installation, GameType.ZeroHour]);

    private static GameInstallation InstallationWithZeroHour(string? zeroHourPath)
    {
        var installation = new GameInstallation(
            Path.GetTempPath(),
            GameInstallationType.Retail,
            new Mock<ILogger<GameInstallation>>().Object);
        installation.SetPaths(null, zeroHourPath);
        return installation;
    }

    private static void BuildEnvironment(Dictionary<string, string> environment, GameInstallation installation) =>
        typeof(GameLauncher)
            .GetMethod("AddRetailArchiveRoots", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [environment, installation]);

    private static string? ValidateFor(GameType gameType, string? generalsPath, string? zeroHourPath)
    {
        var installation = new GameInstallation(
            Path.GetTempPath(),
            GameInstallationType.Retail,
            new Mock<ILogger<GameInstallation>>().Object);
        installation.SetPaths(generalsPath, zeroHourPath);

        return (string?)typeof(GameLauncher)
            .GetMethod("ValidateRetailArchiveRoots", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new Dictionary<string, string>(), installation, gameType]);
    }

    private string CreateRoot(string name, bool withArchive)
    {
        var root = Directory.CreateDirectory(Path.Combine(_tempDir, name)).FullName;
        if (withArchive)
        {
            File.WriteAllText(Path.Combine(root, "INIZH.big"), "archive");
            File.WriteAllText(Path.Combine(root, "Textures.big"), "archive");
        }

        return root;
    }
}
