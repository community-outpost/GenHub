using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering download-related services.
/// </summary>
public static class DownloadModule
{
    /// <summary>
    /// Registers download services for dependency injection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddDownloadServices(this IServiceCollection services)
    {
        services.AddSingleton<IDownloadUrlValidator, DownloadUrlValidator>();

        // DownloadService is shared by singleton content deliverers and manifest factories.
        // It requires an infinite HttpClient.Timeout for streaming large files, while managing
        // connection and per-read inactivity timeouts via its internal CTS.
        services.AddSingleton<DownloadService>(serviceProvider =>
        {
            var configProvider = serviceProvider.GetRequiredService<IConfigurationProviderService>();
            var handler = CreateDownloadHttpHandler();
            var downloadClient = new HttpClient(handler, disposeHandler: true);
            ConfigureDownloadClient(downloadClient, configProvider);

            return new DownloadService(
                serviceProvider.GetService<ILogger<DownloadService>>() ?? NullLogger<DownloadService>.Instance,
                downloadClient,
                serviceProvider.GetRequiredService<IFileHashProvider>(),
                serviceProvider.GetRequiredService<IDownloadUrlValidator>(),
                serviceProvider.GetService<ITelemetryService>());
        });
        services.AddSingleton<IDownloadService>(serviceProvider => serviceProvider.GetRequiredService<DownloadService>());

        // Note: IContentStateService is registered as Singleton in ContentPipelineModule.AddSharedComponents
        // to ensure a single instance with consistent state change events.

        // Register default HttpClient for general DI consumers with a finite timeout.
        services.AddSingleton<HttpClient>(serviceProvider =>
        {
            var configProvider = serviceProvider.GetRequiredService<IConfigurationProviderService>();
            var handler = CreateDownloadHttpHandler();
            var client = new HttpClient(handler, disposeHandler: true);
            ConfigureNamedClient(client, configProvider);
            return client;
        });

        // Named client for tool import downloads with SSRF protection and manual redirect validation
        services.AddHttpClient(
            ToolConstants.ToolImportHttpClientName,
            (serviceProvider, client) =>
            {
                var configProvider = serviceProvider.GetRequiredService<IConfigurationProviderService>();
                ConfigureNamedClient(client, configProvider);
            })
            .ConfigurePrimaryHttpMessageHandler(() => ImageCacheService.CreateSsrfSafeSocketsHttpHandler());

        // Named client for on-demand Playwright driver downloads (config-driven timeout).
        services.AddHttpClient(
            ModDBConstants.PlaywrightDriverHttpClientName,
            (serviceProvider, client) =>
            {
                var configProvider = serviceProvider.GetRequiredService<IConfigurationProviderService>();
                ConfigureNamedClient(client, configProvider);
            });

        return services;
    }

    private static SocketsHttpHandler CreateDownloadHttpHandler() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(DownloadDefaults.HttpConnectTimeoutSeconds),
        PooledConnectionLifetime = TimeSpan.FromMinutes(DownloadDefaults.HttpPooledConnectionLifetimeMinutes),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(DownloadDefaults.HttpPooledConnectionIdleTimeoutSeconds),
        EnableMultipleHttp2Connections = true,
        MaxConnectionsPerServer = DownloadDefaults.HttpMaxConnectionsPerServer,
        ConnectCallback = async (context, cancellationToken) =>
        {
            if (Uri.CheckHostName(context.DnsEndPoint.Host) == UriHostNameType.Unknown)
            {
                throw new HttpRequestException($"Invalid host name: '{context.DnsEndPoint.Host}'.");
            }

            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0 || !addresses.All(NetworkSecurityHelper.IsSafeIpAddress))
            {
                throw new HttpRequestException($"Host '{context.DnsEndPoint.Host}' resolved to an unsafe or reserved IP address.");
            }

            var sortedAddresses = addresses
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ToArray();

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(sortedAddresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    private static void ConfigureDownloadClient(HttpClient client, IConfigurationProviderService configProvider)
    {
        var userAgent = configProvider.GetDownloadUserAgent();

        client.DefaultRequestHeaders.Add("User-Agent", userAgent ?? ApiConstants.DefaultUserAgent);

        // Streaming file downloads require infinite HttpClient.Timeout so that large files or
        // slow connections are not prematurely aborted after a fixed duration. Connection
        // and per-read inactivity timeouts are managed via SocketsHttpHandler.ConnectTimeout
        // and CancellationTokenSource inside DownloadService.
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    private static void ConfigureNamedClient(HttpClient client, IConfigurationProviderService configProvider)
    {
        var userAgent = configProvider.GetDownloadUserAgent();
        var timeoutSeconds = configProvider.GetDownloadTimeoutSeconds();

        client.DefaultRequestHeaders.Add("User-Agent", userAgent ?? ApiConstants.DefaultUserAgent);
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : DownloadDefaults.TimeoutSeconds);
    }
}
