using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Features.GameProfiles.Infrastructure;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using Xunit.Abstractions;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Starts a real process through a workspace symlink to a hash-named copy of a system executable and
/// checks that discovery finds it the way <see cref="GameProcessManager"/> does. Each platform
/// reports a mix of the link and target identities, so this pins what the operating system really does.
/// </summary>
public sealed class SymlinkedProcessDiscoveryTests(ITestOutputHelper output) : IDisposable
{
    private const string ObjectHash = "ab12cd34ef567890ab12cd34ef567890ab12cd34ef567890ab12cd34ef567890";
    private const string EntryHash = "e0e0000000000000000000000000000000000000000000000000000000000000";
    private const int DiscoveryAttempts = 50;
    private const int DiscoveryDelayMs = 100;
    private const int ToolTimeoutMs = 30_000;
    private const int KillWaitMs = 5_000;

    private readonly string _root = Directory.CreateTempSubdirectory("GenHub.SymlinkedProcessDiscoveryTests.").FullName;

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of the temp directory.
        }
    }

    /// <summary>
    /// A game started through a CAS symlink is discovered with the identities the launcher resolves.
    /// </summary>
    [Fact]
    public void ProcessStartedThroughACasSymlink_IsDiscoveredWithTheResolvedIdentities()
    {
        var store = Directory.CreateDirectory(Path.Combine(_root, "objects", "ab")).FullName;
        var workspace = Directory.CreateDirectory(Path.Combine(_root, "workspace")).FullName;
        var blob = Path.Combine(store, ObjectHash);
        var link = Path.Combine(workspace, GameClientConstants.GeneralsOnline60HzExecutable);

        var (source, arguments) = GetLongRunningExecutable();
        File.Copy(source, blob);
        if (!PrepareCopiedExecutable(blob) || !TryCreateSymbolicLink(link, blob))
        {
            output.WriteLine("Skipped: this host cannot run a copied executable through a symbolic link.");
            return;
        }

        File.WriteAllText(Path.Combine(workspace, GameClientConstants.GeneralsOnlineEacLauncherExecutable), "bootstrapper");
        var identitiesResult = GameLauncher.DetermineMonitoringTarget(
            BuildManifests(),
            Path.Combine(workspace, GameClientConstants.GeneralsOnlineEacLauncherExecutable),
            workspace,
            WorkspaceStrategy.SymlinkOnly,
            LaunchEntryPointResolver.ResolveExpectedChildProcessName(GameClientConstants.GeneralsOnlineEacLauncherExecutable),
            NullLogger.Instance,
            localizationService: null);
        Assert.True(identitiesResult.Success, identitiesResult.FirstError);
        var identities = identitiesResult.Data!;

        using var process = Process.Start(new ProcessStartInfo(link) { Arguments = arguments, UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var selected = Discover(identities);
            process.Refresh();
            output.WriteLine($"ProcessName={process.ProcessName}");
            output.WriteLine($"MainModule={process.MainModule?.FileName}");

            Assert.NotNull(selected);
            Assert.Equal(process.Id, selected.ProcessId);
        }
        finally
        {
            try
            {
                process.Kill();
                process.WaitForExit(KillWaitMs);
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }
        }
    }

    private static GameProcessCandidate? Discover(IReadOnlyList<GameProcessIdentity> identities)
    {
        for (var attempt = 0; attempt < DiscoveryAttempts; attempt++)
        {
            var processes = GameProcessManager.GetProcessesByNames(identities.Select(identity => identity.ProcessName));
            try
            {
                var candidates = GameProcessManager.BuildCandidates(processes, NullLogger.Instance);
                var selected = GameProcessSelector.SelectSpawnedGameProcess(candidates, identities, DateTime.UtcNow);
                if (selected is not null)
                {
                    return selected;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }

            Thread.Sleep(DiscoveryDelayMs);
        }

        return null;
    }

    private static (string Source, string Arguments) GetLongRunningExecutable() =>
        OperatingSystem.IsWindows()
            ? (Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n 30 127.0.0.1")
            : ("/bin/sleep", "30");

    /// <summary>
    /// Makes the copy runnable. macOS kills a copied platform binary until it is re-signed ad hoc.
    /// </summary>
    private static bool PrepareCopiedExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        try
        {
            using var codesign = Process.Start(new ProcessStartInfo("/usr/bin/codesign", ["-s", "-", "-f", path])
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            })!;
            codesign.StandardError.ReadToEnd();
            codesign.StandardOutput.ReadToEnd();
            return codesign.WaitForExit(ToolTimeoutMs) && codesign.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool TryCreateSymbolicLink(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows without Developer Mode or elevation cannot create symbolic links.
            return false;
        }
    }

    private static List<ContentManifest> BuildManifests() =>
    [
        new ContentManifest
        {
            Name = "GeneralsOnline",
            ContentType = ContentType.GameClient,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = GameClientConstants.GeneralsOnlineEacLauncherExecutable,
                    Hash = EntryHash,
                    SourceType = ContentSourceType.ContentAddressable,
                    IsExecutable = true,
                },
                new ManifestFile
                {
                    RelativePath = GameClientConstants.GeneralsOnline60HzExecutable,
                    Hash = ObjectHash,
                    SourceType = ContentSourceType.ContentAddressable,
                },
            ],
        },
    ];
}
