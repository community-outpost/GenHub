using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.SingleInstance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Windows.Infrastructure.SingleInstance;

/// <summary>
/// Manages single-instance application behavior with inter-process communication support.
/// Allows secondary instances to send commands (like profile launch requests) to the primary instance.
/// </summary>
public sealed class SingleInstanceManager : ISingleInstanceCommandReceiver, IDisposable
{
    private const string MutexName = "Global\\GenHub";
    private const string PipeName = "GenHub_SingleInstance_Pipe";
    private const int PipeConnectionTimeoutMs = 3000;

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
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipeClient.Connect(timeout: PipeConnectionTimeoutMs);

            using var writer = new StreamWriter(pipeClient);
            writer.WriteLine(command);
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

                        _logger.LogDebug("Pipe server waiting for connection...");
                        await _pipeServer.WaitForConnectionAsync(_pipeServerCts.Token);

                        using var reader = new StreamReader(_pipeServer);
                        var command = await reader.ReadLineAsync(_pipeServerCts.Token);

                        if (!string.IsNullOrEmpty(command))
                        {
                            _logger.LogInformation("Received command from secondary instance: {Command}", command);
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
                        _logger.LogWarning(ex, "Error in pipe server loop");
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
