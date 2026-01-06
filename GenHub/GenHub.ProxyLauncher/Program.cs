using System.Diagnostics;
using System.Text.Json;

namespace GenHub.ProxyLauncher;

/// <summary>
/// Entry point for the GenHub Proxy Launcher.
/// This sidecar executable is used to bridge Steam launches to GenHub workspaces.
/// </summary>
internal class Program
{
    private const string ConfigFileName = ProxyConstants.ConfigFileName;

    /// <summary>
    /// Main entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The exit code of the process.</returns>
    private static async Task<int> Main(string[] args)
    {
        // Prevent multiple instances from running simultaneously
        // This can happen when Steam triggers the proxy again while it's already running
        const string mutexName = ProxyConstants.SingleInstanceMutexName;
        using var mutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is already running - silently exit
            // No logging here since we don't want to spam the log
            return 0;
        }

        int finalExitCode = 0;

        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                // Fallback: If no config, try to launch the original game if it exists as .bak
                // This is a safety measure if someone runs the proxy manually without GenHub setup
                return await TryLaunchBackupAsync(baseDir, args);
            }

            var configJson = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<ProxyConfig>(configJson);

            if (config == null || string.IsNullOrWhiteSpace(config.TargetExecutable))
            {
                LogError("Invalid configuration: TargetExecutable is missing.");
                return 1;
            }

            // Log configuration for debugging
            LogInfo($"Proxy Launcher started at {DateTime.Now}");
            LogInfo($"Configuration loaded from: {configPath}");
            LogInfo($"Target Executable: {config.TargetExecutable}");
            LogInfo($"Working Directory: {config.WorkingDirectory ?? Path.GetDirectoryName(config.TargetExecutable)}");
            LogInfo($"Arguments: {(config.Arguments != null ? string.Join(" ", config.Arguments) : "(none)")}");

            // Validate target executable exists
            if (!File.Exists(config.TargetExecutable))
            {
                var errorMsg = $"Target executable not found: {config.TargetExecutable}";
                LogError(errorMsg);
                return 1;
            }

            // Validate working directory exists
            var workingDir = config.WorkingDirectory ?? Path.GetDirectoryName(config.TargetExecutable);
            if (!Directory.Exists(workingDir))
            {
                var errorMsg = $"Working directory not found: {workingDir}";
                LogError(errorMsg);
                return 1;
            }

            // Prepare process start info
            var startInfo = new ProcessStartInfo
            {
                FileName = config.TargetExecutable,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = false, // Allow the game window to appear
            };

            // Detect whether Steam launched us. Some titles do not set the common env flags even when launched by Steam.
            var steamContext = IsSteamLaunched();
            if (!steamContext && !string.IsNullOrWhiteSpace(config.SteamAppId))
            {
                // Previously we exited after asking Steam to relaunch via steam:// which left the target unstarted and
                // resulted in "invalid license". Now we continue and inject Steam env + steam_appid.txt ourselves.
                LogInfo("Steam context not detected from environment; continuing with injected Steam env instead of exiting.");
            }

            // Propagate Steam identifiers so overlay/playtime work even when launched outside Steam.
            if (!string.IsNullOrWhiteSpace(config.SteamAppId))
            {
                // Ensure steam_appid.txt exists both where the proxy runs (working dir) and where the target exe resides.
                EnsureSteamAppId(config.SteamAppId, workingDir);
                var targetDir = Path.GetDirectoryName(config.TargetExecutable) ?? workingDir;
                if (!string.Equals(targetDir, workingDir, StringComparison.OrdinalIgnoreCase))
                {
                    EnsureSteamAppId(config.SteamAppId, targetDir);
                }

                // Inject common Steam env vars so SteamAPI sees the app even if Steam didn't set them for the proxy.
                startInfo.Environment["SteamAppId"] = config.SteamAppId;
                startInfo.Environment["SteamGameId"] = config.SteamAppId;
                startInfo.Environment["SteamClientLaunch"] = "1";
                startInfo.Environment["SteamEnv"] = "1";
                startInfo.Environment["SteamOverlayGameId"] = config.SteamAppId;
            }

            // Pass through arguments from the config first, then any command line args passed to this proxy
            // Steam might pass args like -quickstart etc.
            // BUT: Steam's %command% might pass the original exe path - filter those out
            var arguments = new List<string>();

            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (config.Arguments != null)
            {
                foreach (var arg in config.Arguments)
                {
                    if (string.IsNullOrWhiteSpace(arg))
                    {
                        continue;
                    }

                    if (dedupe.Add(arg))
                    {
                        arguments.Add(arg);
                    }
                }
            }

            if (args.Length > 0)
            {
                // Filter out exe paths that Steam passes via %command%
                // These are typically the original game executable paths
                foreach (var arg in args)
                {
                    // Skip arguments that match the current executable (the proxy itself)
                    // This handles Steam's %command% expansion which passes the full path to this executable
                    var cleanArg = arg.Trim('"');
                    if (string.Equals(cleanArg, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                    {
                        LogInfo($"Filtering out Steam %command% executable arg (matches ProcessPath): {arg}");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(arg))
                    {
                        continue;
                    }

                    // Avoid double-applying flags like -win when Steam passes them via %command%
                    if (dedupe.Add(arg))
                    {
                        arguments.Add(arg);
                    }
                }
            }

            startInfo.Arguments = string.Join(" ", arguments);

            // Some game launchers (like Community Patch) check their own executable path location.
            // If the target exe is NOT in the working directory, we need to copy it there temporarily.
            string? tempExePath = null;
            var targetExeDir = Path.GetDirectoryName(config.TargetExecutable) ?? string.Empty;
            if (!string.Equals(targetExeDir, workingDir, StringComparison.OrdinalIgnoreCase))
            {
                // Copy the target exe to a temp file in the working directory
                var exeName = Path.GetFileNameWithoutExtension(config.TargetExecutable);
                var tempExeName = $"{exeName}_genhub_temp_{Guid.NewGuid():N}.exe";
                tempExePath = Path.Combine(workingDir!, tempExeName);

                LogInfo($"Target exe not in working directory - creating temp copy at: {tempExePath}");
                try
                {
                    File.Copy(config.TargetExecutable, tempExePath, overwrite: true);
                    startInfo.FileName = tempExePath;
                    LogInfo($"Temp copy created successfully");
                }
                catch (Exception ex)
                {
                    LogError($"Failed to create temp copy: {ex.Message}");

                    // Fall back to original path
                    tempExePath = null;
                }
            }

            // Log the full command line being executed
            LogInfo($"Launching: \"{startInfo.FileName}\" {startInfo.Arguments}");
            LogInfo($"Working Directory: {startInfo.WorkingDirectory}");

            // Launch the target game
            var sw = Stopwatch.StartNew();
            var launchStartUtc = DateTime.UtcNow;
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                var errorMsg = $"Failed to start target process: {config.TargetExecutable}";
                LogError(errorMsg);
                return 1;
            }

            LogInfo($"Process started successfully. PID: {process.Id}");

            // Important for Steam: We must wait for the game to exit.
            // Steam watches THIS process. If we exit, Steam thinks the game stopped.
            await process.WaitForExitAsync();
            sw.Stop();

            finalExitCode = process.ExitCode;
            LogInfo($"Process exited. Exit Code: {finalExitCode}, Duration: {(int)sw.Elapsed.TotalSeconds}s");

            // Some launchers spawn the real game and then exit quickly.
            // If that happens, keep THIS proxy alive by waiting on the spawned child process.
            // Steam tracks the process it started (the proxy). If we exit, playtime/overlay stop.
            if (sw.Elapsed.TotalSeconds < 30)
            {
                var baseName = Path.GetFileNameWithoutExtension(startInfo.FileName);
                var spawned = TryFindSpawnedProcess(baseName, startInfo.WorkingDirectory, launchStartUtc, process.Id);
                if (spawned != null)
                {
                    LogInfo($"Detected spawned process {spawned.Id} for {baseName}; waiting for it to exit to preserve Steam tracking.");
                    try
                    {
                        // Restart stopwatch to track total session time
                        sw.Restart();
                        await spawned.WaitForExitAsync();
                        sw.Stop();
                        finalExitCode = spawned.ExitCode;
                        LogInfo($"Spawned process exited. Exit Code: {finalExitCode}, Total Session Duration: {(int)sw.Elapsed.TotalSeconds}s");
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error waiting for spawned process: {ex.Message}");
                    }
                }
            }

            // Log information about problematic exits, but do not show a message box.
            // Note: C&C games often exit with non-zero codes (e.g. 0xc0000005) on normal shutdown.
            // We only log an error if it happened quickly, suggesting it didn't even start.
            // Ensure we just log completion and exit
            LogInfo($"Process completed. Final Exit Code: {finalExitCode}");

            // Cleanup: Delete temp exe copy if we created one
            if (tempExePath != null && File.Exists(tempExePath))
            {
                try
                {
                    File.Delete(tempExePath);
                    LogInfo($"Cleaned up temp exe: {tempExePath}");
                }
                catch (Exception ex)
                {
                    LogError($"Failed to cleanup temp exe: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Critical error in proxy launcher: {ex.Message}");
            finalExitCode = 1;
        }

        return finalExitCode;
    }

    /// <summary>
    /// Ensures that steam_appid.txt exists in the specified directory with the correct AppID.
    /// </summary>
    /// <param name="appId">The Steam AppID.</param>
    /// <param name="directory">The directory to check.</param>
    private static void EnsureSteamAppId(string appId, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            var path = Path.Combine(directory, "steam_appid.txt");
            var needsWrite = true;

            if (File.Exists(path))
            {
                var current = File.ReadAllText(path).Trim();
                needsWrite = current != appId;
                if (needsWrite)
                {
                    File.Delete(path);
                }
            }

            if (needsWrite)
            {
                File.WriteAllText(path, appId);
                LogInfo($"steam_appid.txt written to {path} (AppId {appId})");
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to ensure steam_appid.txt in {directory}: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects if the current process was launched by Steam.
    /// </summary>
    /// <returns>True if Steam environment variables are detected.</returns>
    private static bool IsSteamLaunched()
    {
        // Steam typically sets one or more of these when launching a game.
        // We use a relaxed check to avoid tight coupling to a single flag.
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SteamClientLaunch"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SteamEnv"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SteamTenfoot"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SteamGameId"));
    }

    /// <summary>
    /// Attempts to find a process spawned by the launcher that matches the target game.
    /// </summary>
    /// <param name="baseName">The name of the process to find.</param>
    /// <param name="workingDir">The expected working directory.</param>
    /// <param name="launchStartUtc">The time when the launch started.</param>
    /// <param name="excludedPid">The PID of the launcher itself to exclude.</param>
    /// <returns>The found process, or null if not found.</returns>
    private static Process? TryFindSpawnedProcess(string baseName, string? workingDir, DateTime launchStartUtc, int excludedPid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return null;
            }

            // Give the launcher a moment to spawn the real game.
            Thread.Sleep(ProxyConstants.LauncherToGameSpawnDelayMs);

            var candidates = Process.GetProcessesByName(baseName);
            foreach (var p in candidates)
            {
                try
                {
                    if (p.Id == excludedPid)
                    {
                        continue;
                    }

                    // StartTime is local time; compare with a small grace window.
                    var startUtc = p.StartTime.ToUniversalTime();
                    if (startUtc < launchStartUtc.AddSeconds(-2))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(workingDir))
                    {
                        var exePath = p.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(exePath))
                        {
                            var exeDir = Path.GetDirectoryName(exePath);
                            if (!string.IsNullOrWhiteSpace(exeDir) &&
                                !string.Equals(Path.GetFullPath(exeDir), Path.GetFullPath(workingDir), StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }
                    }

                    return p;
                }
                catch
                {
                    // Access to MainModule can fail (permissions); ignore and keep scanning.
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Attempts to launch a backup of the original game executable if it exists.
    /// </summary>
    /// <param name="baseDir">The base directory.</param>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The exit code of the launched process, or 1 if not found.</returns>
    private static async Task<int> TryLaunchBackupAsync(string baseDir, string[] args)
    {
        // Try to find generals.exe.ghbak or similar (using standardized extension)
        // This is a naive heuristic, mainly for safety
        var exeName = Path.GetFileName(Environment.ProcessPath);
        var backupPath = Path.Combine(baseDir, exeName + global::GenHub.Core.Constants.SteamConstants.BackupExtension);

        if (File.Exists(backupPath))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = backupPath,
                WorkingDirectory = baseDir,
                Arguments = string.Join(" ", args),
                UseShellExecute = false,
            };

            var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return process.ExitCode;
            }
        }

        return 1;
    }

    // Simple helper to show error since we might not have console
    // In a real scenario, we might want to log to a file

    /// <summary>
    /// Displays a message box with the specified message and title.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="title">The title of the message box.</param>
    private static void MessageBox(string message, string title)
    {
        // User explicitly requested to remove all message boxes
        // Just log the error
        LogError($"{title}: {message}");
    }

    /// <summary>
    /// Logs an informational message to the proxy log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    private static void LogInfo(string message)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ProxyConstants.LogFileName);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INFO: {message}{Environment.NewLine}");
        }
        catch
        {
            /* Ignore logging errors */
        }
    }

    /// <summary>
    /// Logs an error message to the proxy log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    private static void LogError(string message)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ProxyConstants.LogFileName);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}{Environment.NewLine}");
        }
        catch
        {
            /* Ignore logging errors */
        }
    }

    private class ProxyConfig
    {
        public string? TargetExecutable { get; set; }

        public string? WorkingDirectory { get; set; }

        public string[]? Arguments { get; set; }

        public string? SteamAppId { get; set; }
    }
}