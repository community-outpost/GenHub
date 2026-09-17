using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.SingleInstance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Windows.Infrastructure.SingleInstance;

/// <summary>
/// Manages single-instance application behavior with inter-process communication support.
/// Allows secondary instances to send commands (like profile launch requests) to the primary instance.
/// </summary>
public sealed class SingleInstanceManager : ISingleInstanceCommandReceiver, IDisposable
{
    private const int PipeConnectionTimeoutMs = 3000;

    private static readonly string MutexName = GenerateMutexName();
    private static readonly string PipeName = GeneratePipeName();

    private readonly ILogger<SingleInstanceManager> _logger;
    private readonly Mutex _mutex;
    private readonly bool _isFirstInstance;
    private readonly CancellationTokenSource _pipeServerCts;

    private NamedPipeServerStream? _pipeServer;
    private Task? _pipeListenerTask;

    /// <summary>
    /// Occurs when a command is received from another instance.
    /// </summary>
    public event EventHandler<string>? CommandReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstanceManager"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public SingleInstanceManager(ILogger<SingleInstanceManager> logger)
    {
        _logger = logger ?? NullLogger<SingleInstanceManager>.Instance;
        _pipeServerCts = new CancellationTokenSource();
        _mutex = new Mutex(true, MutexName, out _isFirstInstance);

        if (_isFirstInstance)
        {
            _logger.LogDebug("This is the primary instance - starting pipe server");
            StartPipeServer();
        }
        else
        {
            _logger.LogDebug("Another instance is already running");
        }
    }

    /// <summary>
    /// Gets a value indicating whether this is the first (primary) instance.
    /// </summary>
    public bool IsFirstInstance => _isFirstInstance;

    /// <summary>
    /// Sends a command to the running primary instance.
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <returns>True if the command was sent successfully; otherwise, false.</returns>
    public static bool SendCommandToPrimaryInstance(string command)
    {
        try
        {
            var sanitizedCommand = CommandLineParser.SanitizePayload(command).Trim();
            if (!IsValidIpcCommand(sanitizedCommand))
            {
                return false;
            }

            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipeClient.Connect(timeout: PipeConnectionTimeoutMs);

            using var writer = new StreamWriter(pipeClient);
            writer.WriteLine(sanitizedCommand);
            writer.Flush();

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Focuses the primary instance's main window.
    /// </summary>
    public static void FocusPrimaryInstance()
    {
        var currentProcess = Process.GetCurrentProcess();
        var process = Process.GetProcessesByName(currentProcess.ProcessName)
            .FirstOrDefault(p => p.Id != currentProcess.Id);

        if (process != null && process.MainWindowHandle != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(process.MainWindowHandle);
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

        _mutex.Dispose();
        _pipeServerCts.Dispose();
    }

    private static string GeneratePipeName()
    {
        var rawUser = Environment.UserName ?? "default";
        var userBytes = Encoding.UTF8.GetBytes(rawUser);
        var hash = Convert.ToHexString(SHA256.HashData(userBytes))[..8].ToLowerInvariant();
        return $"{CommandLineConstants.SingleInstancePipePrefix}{hash}_{CommandLineConstants.SingleInstancePipeSuffix}";
    }

    private static string GenerateMutexName()
    {
        var rawUser = Environment.UserName ?? "default";
        var userBytes = Encoding.UTF8.GetBytes(rawUser);
        var hash = Convert.ToHexString(SHA256.HashData(userBytes))[..8].ToLowerInvariant();
        return $"Local\\GenHub_{hash}";
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
            async () =>
            {
                while (!_pipeServerCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessPipeConnectionAsync(_pipeServerCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error in pipe server loop");
                    }
                }
            },
            _pipeServerCts.Token);
    }

    private async Task ProcessPipeConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            _pipeServer = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            _logger.LogDebug("Pipe server waiting for connection...");
            await _pipeServer.WaitForConnectionAsync(cancellationToken);

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
                    _logger.LogWarning("Rejecting unknown or malformed IPC command from secondary Windows instance.");
                }
            }

            _pipeServer.Disconnect();
        }
        finally
        {
            _pipeServer?.Dispose();
            _pipeServer = null;
        }
    }
}
