using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Infrastructure.SingleInstance;
using GenHub.Core.Interfaces.SingleInstance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.IO.Pipes;
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
    private readonly SingleInstanceCommandDispatcher _commandDispatcher;

    private NamedPipeServerStream? _pipeServer;
    private Task? _pipeListenerTask;

    /// <summary>
    /// Occurs when a command is received from another instance.
    /// </summary>
    public event EventHandler<string>? CommandReceived
    {
        add => _commandDispatcher.CommandReceived += value;
        remove => _commandDispatcher.CommandReceived -= value;
    }

    private LinuxSingleInstanceManager(FileStream lockFile, ILogger<LinuxSingleInstanceManager> logger)
    {
        _lockFile = lockFile;
        _logger = logger ?? NullLogger<LinuxSingleInstanceManager>.Instance;
        _commandDispatcher = new SingleInstanceCommandDispatcher(this, _logger);
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
            if (!string.IsNullOrEmpty(profileShareUri) &&
                profileShareUri.EndsWith(ProfileSharingConstants.ProfileFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    profileShareUri = Path.GetFullPath(profileShareUri);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
                {
                    logger.LogDebug(ex, "Failed to resolve absolute path for profile file: {Path}", profileShareUri);
                }
            }

            var subscriptionUrl = CommandLineParser.ExtractSubscriptionUrl(args);
            var profileId = CommandLineParser.ExtractProfileId(args);

            string commandToSend;
            if (!string.IsNullOrEmpty(profileShareUri))
            {
                logger.LogInformation("Forwarding import-profile command to primary instance");
                commandToSend = $"{IpcCommands.ImportProfilePrefix}{profileShareUri}";
            }
            else if (BuildToolShareCommand(args) is string toolCommand)
            {
                logger.LogInformation("Forwarding tool import command to primary instance");
                commandToSend = toolCommand;
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
            if (!SingleInstanceCommandDispatcher.IsValidIpcCommand(commandToSend))
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to cleanly dispose pipe server during shutdown");
        }

        try
        {
            _pipeListenerTask?.Wait(TimeSpan.FromSeconds(1), CancellationToken.None);
        }
        catch (AggregateException)
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

    private static string? BuildToolShareCommand(string[] args)
    {
        var toolShareUri = CommandLineParser.ExtractToolShareUri(args);
        return ToolShareLink.BuildIpcCommand(toolShareUri);
    }

    [LibraryImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static partial int getsockopt(SafePipeHandle sockfd, int level, int optname, out UCred optval, ref int optlen);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint geteuid();

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
            _commandDispatcher.TryProcessRawPayload(rawCommand, "Linux");

            _pipeServer.Disconnect();
        }
        finally
        {
            if (_pipeServer != null)
            {
                await _pipeServer.DisposeAsync();
                _pipeServer = null;
            }
        }
    }

    private bool IsPeerAuthorized(NamedPipeServerStream pipeServer)
    {
        try
        {
            var safeHandle = pipeServer.SafePipeHandle;
            var ucred = default(UCred);
            int len = Marshal.SizeOf<UCred>();

            int result = getsockopt(safeHandle, SolSocket, SoPeerCred, out ucred, ref len);
            if (result != 0)
            {
                _logger.LogWarning("getsockopt SO_PEERCRED failed with error {Errno}", Marshal.GetLastPInvokeError());
                return false;
            }

            uint currentEuid = geteuid();
            bool authorized = ucred.Uid == currentEuid;
            if (!authorized)
            {
                _logger.LogWarning("Unauthorized peer UID {PeerUid} (expected {CurrentUid})", ucred.Uid, currentEuid);
            }

            return authorized;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify peer credentials on Linux pipe");
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UCred
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }
}
