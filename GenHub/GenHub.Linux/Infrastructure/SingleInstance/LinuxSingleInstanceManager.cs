using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.SingleInstance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Linux.Infrastructure.SingleInstance;

/// <summary>
/// Manages single-instance application behavior on Linux with inter-process communication support.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSingleInstanceManager : ISingleInstanceCommandReceiver, IDisposable
{
    private const string PipeName = "GenHub_SingleInstance_Pipe";
    private const int PipeConnectionTimeoutMs = 3000;

    private readonly ILogger<LinuxSingleInstanceManager> _logger;
    private readonly FileStream _lockFile;
    private readonly CancellationTokenSource _pipeServerCts;

    private NamedPipeServerStream? _pipeServer;
    private Task? _pipeListenerTask;

    /// <summary>
    /// Occurs when a command is received from another instance.
    /// </summary>
    public event EventHandler<string>? CommandReceived;

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
        var lockFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".genhub", "lock");
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
            var profileId = CommandLineParser.ExtractProfileId(args);
            var subscriptionUrl = CommandLineParser.ExtractSubscriptionUrl(args);
            var profileShareUri = CommandLineParser.ExtractProfileShareUri(args);

            if (string.IsNullOrEmpty(profileId) && string.IsNullOrEmpty(subscriptionUrl) && string.IsNullOrEmpty(profileShareUri))
            {
                return true;
            }

            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipeClient.Connect(timeout: PipeConnectionTimeoutMs);

            using var writer = new StreamWriter(pipeClient);

            if (!string.IsNullOrEmpty(profileId))
            {
                logger.LogInformation("Forwarding launch-profile command to primary instance: {ProfileId}", profileId);
                writer.WriteLine($"{IpcCommands.LaunchProfilePrefix}{profileId}");
            }
            else if (!string.IsNullOrEmpty(subscriptionUrl))
            {
                logger.LogInformation("Forwarding subscribe command to primary instance: {Url}", subscriptionUrl);
                writer.WriteLine($"{IpcCommands.SubscribePrefix}{subscriptionUrl}");
            }
            else if (!string.IsNullOrEmpty(profileShareUri))
            {
                logger.LogInformation("Forwarding import-profile command to primary instance");
                writer.WriteLine($"{IpcCommands.ImportProfilePrefix}{profileShareUri}");
            }

            writer.Flush();
            return true;
        }
        catch (Exception ex)
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

        _lockFile.Dispose();
        _pipeServerCts.Dispose();
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
                        _pipeServer = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        _logger.LogDebug("Linux pipe server waiting for connection...");
                        await _pipeServer.WaitForConnectionAsync(_pipeServerCts.Token);

                        using var reader = new StreamReader(_pipeServer);
                        var command = await reader.ReadLineAsync(_pipeServerCts.Token);

                        if (!string.IsNullOrEmpty(command))
                        {
                            _logger.LogInformation("Received command from secondary Linux instance: {Command}", command);
                            CommandReceived?.Invoke(this, command);
                        }

                        _pipeServer.Disconnect();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error in Linux pipe server loop");
                    }
                    finally
                    {
                        _pipeServer?.Dispose();
                        _pipeServer = null;
                    }
                }
            },
            _pipeServerCts.Token);
    }
}
