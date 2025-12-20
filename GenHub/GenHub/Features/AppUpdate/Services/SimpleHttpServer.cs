using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Simple HTTP server to serve local .nupkg files to Velopack.
/// </summary>
internal sealed class SimpleHttpServer : IDisposable
{
    /// <summary>
    /// Length of the secret token in URL path (RFC 4648 Base16).
    /// </summary>
    private const int SecretTokenLength = 32;

    private readonly HttpListener _listener;
    private readonly string _nupkgPath;
    private readonly string _releasesPath;
    private readonly ILogger _logger;
    private readonly string _secretToken;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private int _started = 0; // 0 = not started, 1 = started (for thread-safe check)

    /// <summary>
    /// Gets the port the server is listening on.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the secret token used in the URL path for security (prevents hijacking by other local processes).
    /// </summary>
    public string SecretToken => _secretToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleHttpServer"/> class.
    /// </summary>
    /// <param name="nupkgPath">The path to the .nupkg file to serve.</param>
    /// <param name="releasesPath">The path to the releases.json file (or releases.win.json).</param>
    /// <param name="port">The port to listen on.</param>
    /// <param name="logger">The logger instance.</param>
    public SimpleHttpServer(string nupkgPath, string releasesPath, int port, ILogger logger)
    {
        _nupkgPath = nupkgPath ?? throw new ArgumentNullException(nameof(nupkgPath));
        _releasesPath = releasesPath ?? throw new ArgumentNullException(nameof(releasesPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Port = port;

        // Generate a random secret token to prevent other local processes from hijacking the server
        _secretToken = Guid.NewGuid().ToString("N").Substring(0, SecretTokenLength);

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/{_secretToken}/");
    }

    /// <summary>
    /// Starts the HTTP server using thread-safe Interlocked pattern.
    /// </summary>
    public void Start()
    {
        // Use Interlocked to atomically check and set _started flag
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return; // Already running
        }

        _cts = new CancellationTokenSource();
        _listener.Start();
        _serverTask = Task.Run(() => HandleRequestsAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("HTTP server started on port {Port} with secret token", Port);
    }

    /// <summary>
    /// Stops the HTTP server.
    /// </summary>
    public void Stop()
    {
        if (_cts == null || _serverTask == null)
        {
            return;
        }

        _cts.Cancel();
        _listener.Stop();

        try
        {
            _serverTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping HTTP server");
        }

        _logger.LogInformation("HTTP server stopped");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _listener.Close();
    }

    private async Task HandleRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Use BeginGetContext + timeout to allow cancellation
                var contextTask = _listener.GetContextAsync();
                var completedTask = await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken)).ConfigureAwait(false);

                if (completedTask != contextTask)
                {
                    // Timeout occurred or cancellation requested, check status
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    continue; // Loop back to check cancellation
                }

                var context = await contextTask.ConfigureAwait(false);
                _ = Task.Run(() => ProcessRequestAsync(context), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when _listener is stopped during cancellation
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling HTTP request");
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            _logger.LogDebug("HTTP request: {Method} {Url}", request.HttpMethod, request.Url);

            // Serve releases.win.json or releases.json (Velopack format)
            if (request.Url?.AbsolutePath.Contains("releases", StringComparison.OrdinalIgnoreCase) == true &&
                request.Url.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(_releasesPath))
                {
                    _logger.LogDebug("Serving releases JSON from {Path}", _releasesPath);
                    var content = await File.ReadAllBytesAsync(_releasesPath);
                    response.ContentType = "application/json";
                    response.ContentLength64 = content.Length;
                    await response.OutputStream.WriteAsync(content);
                }
                else
                {
                    _logger.LogWarning("Releases JSON file not found at {Path}", _releasesPath);
                    response.StatusCode = 404;
                }
            }

            // Serve .nupkg file
            else if (request.Url?.AbsolutePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (File.Exists(_nupkgPath))
                {
                    _logger.LogDebug("Serving nupkg from {Path}", _nupkgPath);
                    response.ContentType = "application/octet-stream";
                    response.ContentLength64 = new FileInfo(_nupkgPath).Length;

                    using var fileStream = File.OpenRead(_nupkgPath);
                    await fileStream.CopyToAsync(response.OutputStream);
                }
                else
                {
                    _logger.LogWarning("Nupkg file not found at {Path}", _nupkgPath);
                    response.StatusCode = 404;
                }
            }
            else
            {
                _logger.LogWarning("Unknown request path: {Path}", request.Url?.AbsolutePath);
                response.StatusCode = 404;
            }

            response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing HTTP request");
        }
    }
}