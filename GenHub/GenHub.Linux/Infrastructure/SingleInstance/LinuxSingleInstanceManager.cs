using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.SingleInstance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Linux.Infrastructure.SingleInstance;

/// <summary>
/// Manages single-instance application behavior on Linux with inter-process communication support.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxSingleInstanceManager : ISingleInstanceCommandReceiver, IDisposable
{
    private const int PipeConnectionTimeoutMs = 3000;
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    private static readonly string PipeName = GeneratePipeName();

    private readonly ILogger<LinuxSingleInstanceManager> _logger;
    private readonly FileStream _lockFile;
    private readonly CancellationTokenSource _pipeServerCts;

    private NamedPipeServerStream? _pipeServer;
    private Task? _pipeListenerTask;

    /// <summary>
    /// Occurs when a command is received from another instance.
    /// </summary>
    public event EventHandler<string>? CommandReceived;

    [StructLayout(LayoutKind.Sequential)]
    private struct UCred
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

    [LibraryImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static partial int getsockopt(int sockfd, int level, int optname, out UCred optval, ref int optlen);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint geteuid();

    private LinuxSingleInstanceManager(FileStream lockFile, ILogger<LinuxSingleInstanceManager> logger)
    {
        _lockFile = lockFile;
        _logger = logger ?? NullLogger<LinuxSingleInstanceManager>.Instance;
        _pipeServerCts = new CancellationTokenSource();

        _logger.LogDebug("This is the primary instance on Linux - starting pipe server");
        StartPipeServer();
    }

    /// <summary>
    /// Attempts to acquire the primary instance lock. If successful, returns the manager; otherwise null.
    /// </summary>
    /// <param name="logger">Logger for single instance diagnostics.</param>
    /// <returns>The single instance manager if primary; otherwise null.</returns>
    public static LinuxSingleInstanceManager? TryCreatePrimary(ILogger<LinuxSingleInstanceManager> logger)
    {
        var dataRoot = StorageMigrationService.GetDefaultDataRoot();
        var lockFilePath = Path.Combine(dataRoot, "lock");
        var lockDir = Path.GetDirectoryName(lockFilePath);
        if (!string.IsNullOrEmpty(lockDir))
        {
            Directory.CreateDirectory(lockDir);
        }

        try
        {
            var lockFile = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new LinuxSingleInstanceManager(lockFile, logger);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends command line arguments to the running primary instance.
    /// </summary>
    /// <param name="args">Command line arguments to forward.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>True if forwarded or instance detected; otherwise false.</returns>
    public static bool SendCommandToPrimaryInstance(string[] args, ILogger logger)
    {
        try
        {
            var profileShareUri = CommandLineParser.ExtractProfileShareUri(args);
            var subscriptionUrl = CommandLineParser.ExtractSubscriptionUrl(args);
            var profileId = CommandLineParser.ExtractProfileId(args);

            string commandToSend;
            if (!string.IsNullOrEmpty(profileShareUri))
            {
                logger.LogInformation("Forwarding import-profile command to primary instance");
                commandToSend = $"{IpcCommands.ImportProfilePrefix}{profileShareUri}";
            }
            else if (!string.IsNullOrEmpty(subscriptionUrl))
            {
                logger.LogInformation("Forwarding subscribe command to primary instance");
                commandToSend = $"{IpcCommands.SubscribePrefix}{subscriptionUrl}";
            }
            else if (!string.IsNullOrEmpty(profileId))
            {
                logger.LogInformation("Forwarding launch-profile command to primary instance: {ProfileId}", profileId);
                commandToSend = $"{IpcCommands.LaunchProfilePrefix}{profileId}";
            }
            else
            {
                logger.LogInformation("Forwarding activate command to primary instance");
                commandToSend = IpcCommands.ActivateCommand;
            }

            commandToSend = CommandLineParser.SanitizePayload(commandToSend).Trim();
            if (!IsValidIpcCommand(commandToSend))
            {
                logger.LogWarning("Refusing to forward invalid IPC command.");
                return false;
            }

            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipeClient.Connect(timeout: PipeConnectionTimeoutMs);

            using var writer = new StreamWriter(pipeClient);
            writer.WriteLine(commandToSend);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or SocketException)
        {
            logger.LogDebug(ex, "Could not forward command to primary instance via pipe");
            return false;
        }
    }

    /// <summary>
    /// Releases resources used by the manager.
    /// </summary>
    public void Dispose()
    {
        _pipeServerCts.Cancel();

        try
        {
            _pipeServer?.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }

        try
        {
            _pipeListenerTask?.Wait(TimeSpan.FromSeconds(1), CancellationToken.None);
        }
        catch
        {
            // Ignore task cancellation / disposal errors
        }

        _lockFile.Dispose();
        _pipeServerCts.Dispose();
    }

    private static string GeneratePipeName()
    {
        var rawUser = Environment.UserName ?? "default";
        var userBytes = Encoding.UTF8.GetBytes(rawUser);
        var hash = Convert.ToHexString(SHA256.HashData(userBytes))[..8].ToLowerInvariant();
        return $"{CommandLineConstants.SingleInstancePipePrefix}{hash}_{CommandLineConstants.SingleInstancePipeSuffix}";
    }

    private static bool IsValidIpcCommand(string command)
    {
        if (string.Equals(command, IpcCommands.ActivateCommand, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (command.StartsWith(IpcCommands.LaunchProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = command[IpcCommands.LaunchProfilePrefix.Length..].Trim();
            return !string.IsNullOrEmpty(id) && !id.Contains('/') && !id.Contains('\\') && !id.Contains("..");
        }

        if (command.StartsWith(IpcCommands.SubscribePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var url = command[IpcCommands.SubscribePrefix.Length..].Trim();
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        if (command.StartsWith(IpcCommands.ImportProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var target = command[IpcCommands.ImportProfilePrefix.Length..].Trim();
            if (target.StartsWith(CommandLineConstants.ProfileImportUriPrefix, StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(CommandLineConstants.ProfileViewUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (target.EndsWith(ProfileSharingConstants.ProfileFileExtension, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(target))
            {
                return true;
            }

            return false;
        }

        return false;
    }

    private static void LogReceivedCommand(ILogger logger, string command)
    {
        if (command.StartsWith(IpcCommands.ImportProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Received IPC command: {Prefix}...", IpcCommands.ImportProfilePrefix);
        }
        else if (command.StartsWith(IpcCommands.SubscribePrefix, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Received IPC command: {Prefix}...", IpcCommands.SubscribePrefix);
        }
        else if (command.StartsWith(IpcCommands.LaunchProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = command[IpcCommands.LaunchProfilePrefix.Length..];
            logger.LogInformation("Received IPC command: {Prefix}{ProfileId}", IpcCommands.LaunchProfilePrefix, id);
        }
        else
        {
            logger.LogInformation("Received IPC command: {Command}", command);
        }
    }

    private void StartPipeServer()
    {
        _pipeListenerTask = Task.Run(
            () => RunPipeServerLoopAsync(_pipeServerCts.Token),
            _pipeServerCts.Token);
    }

    private async Task RunPipeServerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await AcceptAndProcessConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in Linux pipe server loop");
                try
                {
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task AcceptAndProcessConnectionAsync(CancellationToken cancellationToken)
    {
        _pipeServer = new NamedPipeServerStream(
            PipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        try
        {
            _logger.LogDebug("Linux pipe server waiting for connection...");
            await _pipeServer.WaitForConnectionAsync(cancellationToken);

            if (!IsPeerAuthorized(_pipeServer))
            {
                _logger.LogWarning("Rejecting unauthorized pipe connection from different Linux user.");
                _pipeServer.Disconnect();
                return;
            }

            using var reader = new StreamReader(_pipeServer);
            var rawCommand = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(rawCommand))
            {
                var command = CommandLineParser.SanitizePayload(rawCommand).Trim();
                if (IsValidIpcCommand(command))
                {
                    LogReceivedCommand(_logger, command);
                    CommandReceived?.Invoke(this, command);
                }
                else
                {
                    _logger.LogWarning("Rejecting unknown or malformed IPC command from secondary Linux instance.");
                }
            }

            _pipeServer.Disconnect();
        }
        finally
        {
            if (_pipeServer != null)
            {
                await _pipeServer.DisposeAsync().ConfigureAwait(false);
                _pipeServer = null;
            }
        }
    }

    [SuppressMessage("Reliability", "S3869:SafeHandle.DangerousGetHandle should not be called", Justification = "Interoping with libc getsockopt requires extracting the native file descriptor from SafePipeHandle.")]
    private bool IsPeerAuthorized(NamedPipeServerStream pipeServer)
    {
        try
        {
            bool success = false;
            pipeServer.SafePipeHandle.DangerousAddRef(ref success);
            try
            {
                var fd = pipeServer.SafePipeHandle.DangerousGetHandle().ToInt32();
                int len = Marshal.SizeOf<UCred>();
                int res = getsockopt(fd, SolSocket, SoPeerCred, out var cred, ref len);
                if (res != 0)
                {
                    _logger.LogWarning("getsockopt SO_PEERCRED failed with error {ErrorCode}", Marshal.GetLastWin32Error());
                    return false;
                }

                uint myUid = geteuid();
                return cred.Uid == myUid;
            }
            finally
            {
                if (success)
                {
                    pipeServer.SafePipeHandle.DangerousRelease();
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or DllNotFoundException or EntryPointNotFoundException)
        {
            _logger.LogWarning(ex, "Failed to verify peer credentials on Linux pipe connection");
            return false;
        }
    }
}
