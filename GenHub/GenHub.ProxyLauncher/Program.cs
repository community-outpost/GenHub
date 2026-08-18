using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var mutexName = GetScopedMutexName(baseDir);

        using var mutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            LogError($"Another instance of proxy launcher is already running for directory: {baseDir}");
            return 0;
        }

        GC.KeepAlive(mutex);

        try
        {
            var configPath = Path.Combine(baseDir, ConfigFileName);
            if (!File.Exists(configPath))
            {
                return await TryLaunchBackupAsync(baseDir, args);
            }

            var config = await LoadConfigAsync(configPath);
            if (config == null || string.IsNullOrWhiteSpace(config.TargetExecutable))
            {
                LogError("Invalid configuration: TargetExecutable is missing.");
                return 1;
            }

            var workingDir = config.WorkingDirectory ?? Path.GetDirectoryName(config.TargetExecutable);
            if (!ValidatePaths(config.TargetExecutable, workingDir))
            {
                return 1;
            }

            LogLaunchDetails(configPath, config, workingDir);

            var (startInfo, tempExePath) = PrepareProcessStartInfo(config, workingDir!, args);
            var (exitCode, _) = await ExecuteAndMonitorProcessAsync(config, startInfo);

            CleanupTempExecutable(tempExePath);
            LogInfo($"Process completed. Final Exit Code: {exitCode}");
            return exitCode;
        }
        catch (Exception ex)
        {
            LogError($"Critical error in proxy launcher: {ex.Message}");
            return 1;
        }
    }

    private static string GetScopedMutexName(string baseDir)
    {
        var normalizedDir = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDir));
        return $"{ProxyConstants.MutexPrefix}{Convert.ToHexString(hashBytes)[..16]}";
    }

    private static async Task<ProxyConfig?> LoadConfigAsync(string configPath)
    {
        var configJson = await File.ReadAllTextAsync(configPath);
        return JsonSerializer.Deserialize<ProxyConfig>(configJson);
    }

    private static bool ValidatePaths(string targetExecutable, string? workingDir)
    {
        if (!File.Exists(targetExecutable))
        {
            LogError($"Target executable not found: {targetExecutable}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(workingDir) || !Directory.Exists(workingDir))
        {
            LogError($"Working directory not found: {workingDir}");
            return false;
        }

        return true;
    }

    private static void LogLaunchDetails(string configPath, ProxyConfig config, string? workingDir)
    {
        LogInfo($"Proxy Launcher started at {DateTime.UtcNow:O}");
        LogInfo($"Configuration loaded from: {configPath}");
        LogInfo($"Target Executable: {config.TargetExecutable}");
        LogInfo($"Working Directory: {workingDir}");
        LogInfo($"Arguments: {(config.Arguments != null ? string.Join(" ", config.Arguments) : "(none)")}");
    }

    private static (ProcessStartInfo StartInfo, string? TempExePath) PrepareProcessStartInfo(
        ProxyConfig config,
        string workingDir,
        string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = config.TargetExecutable!,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        ConfigureSteamEnvironment(startInfo, config, workingDir);
        startInfo.Arguments = BuildArgumentString(config, args);

        var tempExePath = PrepareTemporaryExecutable(config, workingDir, startInfo);
        return (startInfo, tempExePath);
    }

    private static void ConfigureSteamEnvironment(ProcessStartInfo startInfo, ProxyConfig config, string workingDir)
    {
        var steamContext = IsSteamLaunched();
        if (!steamContext && !string.IsNullOrWhiteSpace(config.SteamAppId))
        {
            LogInfo("Steam context not detected from environment; continuing with injected Steam env instead of exiting.");
        }

        if (!string.IsNullOrWhiteSpace(config.SteamAppId))
        {
            EnsureSteamAppId(config.SteamAppId, workingDir);
            var targetDir = Path.GetDirectoryName(config.TargetExecutable) ?? workingDir;
            if (!string.Equals(targetDir, workingDir, StringComparison.OrdinalIgnoreCase))
            {
                EnsureSteamAppId(config.SteamAppId, targetDir);
            }

            startInfo.Environment["SteamAppId"] = config.SteamAppId;
            startInfo.Environment["SteamGameId"] = config.SteamAppId;
            startInfo.Environment["SteamClientLaunch"] = "1";
            startInfo.Environment["SteamEnv"] = "1";
            startInfo.Environment["SteamOverlayGameId"] = config.SteamAppId;
        }
    }

    private static string BuildArgumentString(ProxyConfig config, string[] args)
    {
        var arguments = new List<string>();
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (config.Arguments != null)
        {
            foreach (var arg in config.Arguments)
            {
                if (!string.IsNullOrWhiteSpace(arg) && dedupe.Add(arg))
                {
                    arguments.Add(arg);
                }
            }
        }

        if (args.Length > 0)
        {
            foreach (var arg in args)
            {
                var cleanArg = arg.Trim('"');
                if (string.Equals(cleanArg, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                {
                    LogInfo($"Filtering out Steam %command% executable arg (matches ProcessPath): {arg}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(arg) && dedupe.Add(arg))
                {
                    arguments.Add(arg);
                }
            }
        }

        return string.Join(" ", arguments);
    }

    private static string? PrepareTemporaryExecutable(ProxyConfig config, string workingDir, ProcessStartInfo startInfo)
    {
        var targetExeDir = Path.GetDirectoryName(config.TargetExecutable) ?? string.Empty;
        if (string.Equals(targetExeDir, workingDir, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var exeName = Path.GetFileNameWithoutExtension(config.TargetExecutable);
        var tempExeName = $"{exeName}_genhub_temp_{Guid.NewGuid():N}.exe";
        var tempExePath = Path.Combine(workingDir, tempExeName);

        LogInfo($"Target exe not in working directory - creating temp copy at: {tempExePath}");
        try
        {
            File.Copy(config.TargetExecutable!, tempExePath, overwrite: true);
            startInfo.FileName = tempExePath;
            LogInfo("Temp copy created successfully");
            return tempExePath;
        }
        catch (Exception ex)
        {
            LogError($"Failed to create temp copy: {ex.Message}");
            return null;
        }
    }

    private static async Task<(int ExitCode, bool SpawnedFound)> ExecuteAndMonitorProcessAsync(
        ProxyConfig config,
        ProcessStartInfo startInfo)
    {
        LogInfo($"Launching: \"{startInfo.FileName}\" {startInfo.Arguments}");
        LogInfo($"Working Directory: {startInfo.WorkingDirectory}");

        var sw = Stopwatch.StartNew();
        var launchStartUtc = DateTime.UtcNow;
        using var process = Process.Start(startInfo);
        if (process == null)
        {
            LogError($"Failed to start target process: {config.TargetExecutable}");
            return (1, false);
        }

        LogInfo($"Process started successfully. PID: {process.Id}");
        await process.WaitForExitAsync();
        sw.Stop();

        var finalExitCode = process.ExitCode;
        LogInfo($"Process exited. Exit Code: {finalExitCode}, Duration: {(int)sw.Elapsed.TotalSeconds}s");

        var spawnedFound = false;
        if (sw.Elapsed.TotalSeconds < 30)
        {
            var baseName = Path.GetFileNameWithoutExtension(config.TargetExecutable);
            var spawned = TryFindSpawnedProcess(baseName, startInfo.WorkingDirectory, launchStartUtc, process.Id);
            if (spawned != null)
            {
                spawnedFound = true;
                LogInfo($"Detected spawned process {spawned.Id} for {baseName}; waiting for it to exit to preserve Steam tracking.");
                try
                {
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
                finally
                {
                    spawned.Dispose();
                }
            }
        }

        return (finalExitCode, spawnedFound);
    }

    private static void CleanupTempExecutable(string? tempExePath)
    {
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
    private static Process? TryFindSpawnedProcess(string? baseName, string? workingDir, DateTime launchStartUtc, int excludedPid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return null;
            }

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

    /// <summary>
    /// Logs an informational message to the proxy log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    private static void LogInfo(string message)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ProxyConstants.LogFileName);
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] INFO: {message}{Environment.NewLine}");
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
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] ERROR: {message}{Environment.NewLine}");
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