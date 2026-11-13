using GenHub.Core.Constants;
using GenHub.Features.GameProfiles.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Verifies that Flatpak launches expose the retail archive roots to the sandbox,
/// so a client resolving game data from its environment sees the host paths.
/// </summary>
public sealed class GameProcessManagerFlatpakBindsTests : IDisposable
{
    private readonly string _scratchRoot = Path.Combine(Path.GetTempPath(), $"genhub-flatpak-binds-{Guid.NewGuid():N}");

    /// <summary>
    /// Verifies that a null environment resolves no sandbox binds.
    /// </summary>
    [Fact]
    public void ResolveFlatpakFilesystemBinds_NullEnvironment_ReturnsEmpty()
    {
        Assert.Empty(GameProcessManager.ResolveFlatpakFilesystemBinds(null));
    }

    /// <summary>
    /// Verifies that an environment without install-path variables resolves no sandbox binds.
    /// </summary>
    [Fact]
    public void ResolveFlatpakFilesystemBinds_MissingVariables_ReturnsEmpty()
    {
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
        };

        Assert.Empty(GameProcessManager.ResolveFlatpakFilesystemBinds(environment));
    }

    /// <summary>
    /// Verifies that install-path variables naming absent directories are skipped,
    /// since Flatpak rejects binds that do not exist on the host.
    /// </summary>
    [Fact]
    public void ResolveFlatpakFilesystemBinds_MissingDirectories_AreSkipped()
    {
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.ZeroHourInstallPathVariable] = Path.Combine(_scratchRoot, "absent-zh"),
            [RetailArchiveConstants.GeneralsInstallPathVariable] = Path.Combine(_scratchRoot, "absent-generals"),
        };

        Assert.Empty(GameProcessManager.ResolveFlatpakFilesystemBinds(environment));
    }

    /// <summary>
    /// Verifies that two unrelated archive roots both reach the sandbox.
    /// </summary>
    [Fact]
    public void ResolveFlatpakFilesystemBinds_SiblingRoots_BindsBoth()
    {
        var zeroHour = Directory.CreateDirectory(Path.Combine(_scratchRoot, "zh")).FullName;
        var generals = Directory.CreateDirectory(Path.Combine(_scratchRoot, "generals")).FullName;
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.ZeroHourInstallPathVariable] = zeroHour,
            [RetailArchiveConstants.GeneralsInstallPathVariable] = generals,
        };

        var binds = GameProcessManager.ResolveFlatpakFilesystemBinds(environment);

        Assert.Equal(2, binds.Count);
        Assert.Contains(zeroHour, binds);
        Assert.Contains(generals, binds);
    }

    /// <summary>
    /// Verifies that a nested archive root collapses into its outer root, so the
    /// sandbox gets one bind covering both paths.
    /// </summary>
    [Fact]
    public void ResolveFlatpakFilesystemBinds_NestedRoots_BindsOuterOnly()
    {
        var outer = Directory.CreateDirectory(Path.Combine(_scratchRoot, "outer")).FullName;
        var inner = Directory.CreateDirectory(Path.Combine(outer, "ZH_Generals")).FullName;
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.ZeroHourInstallPathVariable] = outer,
            [RetailArchiveConstants.GeneralsInstallPathVariable] = inner + Path.DirectorySeparatorChar,
        };

        var binds = GameProcessManager.ResolveFlatpakFilesystemBinds(environment);

        Assert.Single(binds);
        Assert.Equal(outer, binds[0]);
    }

    /// <summary>
    /// Verifies that nesting collapses to the outer root regardless of variable order.
    /// </summary>
    [Fact]
    public void ResolveFlatpakFilesystemBinds_InnerListedFirst_BindsOuterOnly()
    {
        var outer = Directory.CreateDirectory(Path.Combine(_scratchRoot, "outer")).FullName;
        var inner = Directory.CreateDirectory(Path.Combine(outer, "ZH_Generals")).FullName;
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.ZeroHourInstallPathVariable] = inner,
            [RetailArchiveConstants.GeneralsInstallPathVariable] = outer,
        };

        var binds = GameProcessManager.ResolveFlatpakFilesystemBinds(environment);

        Assert.Single(binds);
        Assert.Equal(outer, binds[0]);
    }

    /// <summary>
    /// Verifies that both variables naming one directory produce a single bind.
    /// </summary>
    [Fact]
    public void ResolveFlatpakFilesystemBinds_DuplicateRoots_BindsOnce()
    {
        var root = Directory.CreateDirectory(Path.Combine(_scratchRoot, "shared")).FullName;
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.ZeroHourInstallPathVariable] = root,
            [RetailArchiveConstants.GeneralsInstallPathVariable] = root,
        };

        var binds = GameProcessManager.ResolveFlatpakFilesystemBinds(environment);

        Assert.Single(binds);
    }

    /// <summary>
    /// Verifies that GeneralsX-specific environment variables reach sandbox binds.
    /// </summary>
    [Fact]
    public void ResolveFlatpakFilesystemBinds_GeneralsXVariables_BindsBoth()
    {
        var zeroHour = Directory.CreateDirectory(Path.Combine(_scratchRoot, "zh-gx")).FullName;
        var generals = Directory.CreateDirectory(Path.Combine(_scratchRoot, "gen-gx")).FullName;
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.GeneralsXZeroHourInstallPathVariable] = zeroHour,
            [RetailArchiveConstants.GeneralsXGeneralsInstallPathVariable] = generals,
        };

        var binds = GameProcessManager.ResolveFlatpakFilesystemBinds(environment);

        Assert.Equal(2, binds.Count);
        Assert.Contains(zeroHour, binds);
        Assert.Contains(generals, binds);
    }

    /// <summary>
    /// Verifies that the working directory (workspace) and the native options directory
    /// are added to the sandbox filesystem binds.
    /// </summary>
    [Fact]
    public void ResolveFlatpakFilesystemBinds_WorkingDirectoryAndOptionsIni_BindsBoth()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_scratchRoot, "workspace")).FullName;
        var optionsDir = Directory.CreateDirectory(Path.Combine(_scratchRoot, "user-data")).FullName;
        var optionsIni = Path.Combine(optionsDir, "Options.ini");
        File.WriteAllText(optionsIni, "[Options]\nResolution = 1920 1080");

        var binds = GameProcessManager.ResolveFlatpakFilesystemBinds(
            environment: null,
            workingDirectory: workspace,
            optionsIniPath: optionsIni);

        Assert.Equal(2, binds.Count);
        Assert.Contains(workspace, binds);
        Assert.Contains(optionsDir, binds);
    }

    /// <summary>
    /// Verifies that a workspace nested inside a retail root collapses into the retail root bind.
    /// </summary>
    [Fact]
    public void ResolveFlatpakFilesystemBinds_WorkspaceInsideRetailRoot_CollapsesToOuterOnly()
    {
        var retailRoot = Directory.CreateDirectory(Path.Combine(_scratchRoot, "retail")).FullName;
        var workspace = Directory.CreateDirectory(Path.Combine(retailRoot, "nested-workspace")).FullName;
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.ZeroHourInstallPathVariable] = retailRoot,
        };

        var binds = GameProcessManager.ResolveFlatpakFilesystemBinds(
            environment: environment,
            workingDirectory: workspace);

        Assert.Single(binds);
        Assert.Equal(retailRoot, binds[0]);
    }

    /// <summary>
    /// Verifies that null or empty environment variables produce empty Flatpak env arguments.
    /// </summary>
    [Fact]
    public void ResolveFlatpakEnvironmentArguments_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(GameProcessManager.ResolveFlatpakEnvironmentArguments("com.fbraz3.GeneralsXZH", null));
        Assert.Empty(GameProcessManager.ResolveFlatpakEnvironmentArguments("com.fbraz3.GeneralsXZH", new Dictionary<string, string>()));
    }

    /// <summary>
    /// Verifies that environment variables are prefixed with --env= for sandbox forwarding.
    /// </summary>
    [Fact]
    public void ResolveFlatpakEnvironmentArguments_StandardEnvironment_PassesEnvPrefixes()
    {
        var environment = new Dictionary<string, string>
        {
            ["CUSTOM_SETTING"] = "1",
            ["FOO"] = "BAR",
        };

        var args = GameProcessManager.ResolveFlatpakEnvironmentArguments("com.example.game", environment);

        Assert.Equal(2, args.Count);
        Assert.Contains("--env=CUSTOM_SETTING=1", args);
        Assert.Contains("--env=FOO=BAR", args);
    }

    /// <summary>
    /// Verifies that for GeneralsXZH Flatpak, CNC_GENERALS_INSTALLPATH is redirected to the Zero Hour path
    /// so that run.sh changes directory into the Zero Hour directory instead of base Generals.
    /// </summary>
    [Fact]
    public void ResolveFlatpakEnvironmentArguments_GeneralsXZH_RedirectsGeneralsInstallPathToZeroHour()
    {
        var zhPath = "/home/mint/.steam/steam/steamapps/common/Command & Conquer Generals - Zero Hour/Command & Conquer Generals Zero Hour/";
        var genPath = "/home/mint/.steam/steam/steamapps/common/Command & Conquer Generals - Zero Hour/Command and Conquer Generals/";

        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.ZeroHourInstallPathVariable] = zhPath,
            [RetailArchiveConstants.GeneralsInstallPathVariable] = genPath,
            [RetailArchiveConstants.GeneralsXZeroHourInstallPathVariable] = zhPath,
            [RetailArchiveConstants.GeneralsXGeneralsInstallPathVariable] = genPath,
        };

        var args = GameProcessManager.ResolveFlatpakEnvironmentArguments("com.fbraz3.GeneralsXZH", environment);

        Assert.Contains($"--env={RetailArchiveConstants.ZeroHourInstallPathVariable}={zhPath}", args);
        Assert.Contains($"--env={RetailArchiveConstants.GeneralsXZeroHourInstallPathVariable}={zhPath}", args);
        Assert.Contains($"--env={RetailArchiveConstants.GeneralsXGeneralsInstallPathVariable}={genPath}", args);

        // Crucial: CNC_GENERALS_INSTALLPATH must point to Zero Hour, not base Generals!
        Assert.Contains($"--env={RetailArchiveConstants.GeneralsInstallPathVariable}={zhPath}", args);
        Assert.DoesNotContain($"--env={RetailArchiveConstants.GeneralsInstallPathVariable}={genPath}", args);
    }

    /// <summary>
    /// Verifies that when a working directory (workspace) is provided for GeneralsXZH Flatpak,
    /// CNC_GENERALS_INSTALLPATH, CNC_ZH_INSTALLPATH, and CNC_GENERALS_PATH all redirect to the workspace
    /// so the engine runs from the prepared workspace and avoids loading raw Patch.big from ZH_Generals.
    /// </summary>
    [Fact]
    public void ResolveFlatpakEnvironmentArguments_GeneralsXZHWithWorkspace_RedirectsAllPathsToWorkspace()
    {
        var zhPath = "/home/mint/.steam/steam/steamapps/common/Command & Conquer Generals - Zero Hour/Command & Conquer Generals Zero Hour/";
        var genPath = "/home/mint/.steam/steam/steamapps/common/Command & Conquer Generals - Zero Hour/Command & Conquer Generals Zero Hour/ZH_Generals/";
        var workspacePath = "/home/mint/.steam/steam/steamapps/.genhub-workspace/test-workspace/";

        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.ZeroHourInstallPathVariable] = zhPath,
            [RetailArchiveConstants.GeneralsInstallPathVariable] = genPath,
            [RetailArchiveConstants.GeneralsXZeroHourInstallPathVariable] = zhPath,
            [RetailArchiveConstants.GeneralsXGeneralsInstallPathVariable] = genPath,
        };

        var args = GameProcessManager.ResolveFlatpakEnvironmentArguments(
            "com.fbraz3.GeneralsXZH",
            environment,
            workingDirectory: workspacePath);

        // CNC_GENERALS_INSTALLPATH must point to workspace so Flatpak run.sh cds and sets CWD to workspace
        Assert.Contains($"--env={RetailArchiveConstants.GeneralsInstallPathVariable}={workspacePath}", args);

        // CNC_ZH_INSTALLPATH must point to workspace
        Assert.Contains($"--env={RetailArchiveConstants.GeneralsXZeroHourInstallPathVariable}={workspacePath}", args);

        // CNC_GENERALS_PATH must point to workspace (where safe supplemental archives are linked, excluding unsafe Patch.big)
        Assert.Contains($"--env={RetailArchiveConstants.GeneralsXGeneralsInstallPathVariable}={workspacePath}", args);
        Assert.DoesNotContain($"--env={RetailArchiveConstants.GeneralsXGeneralsInstallPathVariable}={genPath}", args);
    }

    /// <summary>
    /// Verifies that when no Zero Hour target path is available for GeneralsXZH Flatpak,
    /// CNC_GENERALS_INSTALLPATH is omitted rather than forwarding base Generals retail path.
    /// </summary>
    [Fact]
    public void ResolveFlatpakEnvironmentArguments_GeneralsXZHWithoutZeroHourPath_OmitsGeneralsInstallPathVariable()
    {
        var genPath = "/retail/Generals/";
        var environment = new Dictionary<string, string>
        {
            [RetailArchiveConstants.GeneralsInstallPathVariable] = genPath,
        };

        var args = GameProcessManager.ResolveFlatpakEnvironmentArguments("com.fbraz3.GeneralsXZH", environment, workingDirectory: null);

        Assert.DoesNotContain(args, a => a.StartsWith($"--env={RetailArchiveConstants.GeneralsInstallPathVariable}="));
    }

    /// <summary>
    /// Cleans up the scratch directories created by the tests.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_scratchRoot))
            {
                Directory.Delete(_scratchRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
        catch (UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
