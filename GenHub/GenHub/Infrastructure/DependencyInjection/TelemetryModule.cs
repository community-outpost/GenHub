using System.Net.Http;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Utilities;
using GenHub.Features.Telemetry.Services;
using GenHub.Features.Telemetry.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering telemetry services, sanitizers, and sinks.
/// </summary>
public static class TelemetryModule
{
    /// <summary>
    /// Registers telemetry services and sinks in the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddTelemetryServices(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<ITelemetrySanitizer, TelemetrySanitizer>();

        // Register default pluggable sinks
        services.AddSingleton<ITelemetrySink, LoggingTelemetrySink>();

        services.AddSingleton<AnalyticsTelemetrySink>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<AnalyticsTelemetrySink>>();
            var factory = sp.GetService<IHttpClientFactory>();
            var client = factory != null ? factory.CreateClient("TelemetryAnalytics") : sp.GetService<HttpClient>();
            return new AnalyticsTelemetrySink(logger, client);
        });
        services.AddSingleton<ITelemetrySink>(sp => sp.GetRequiredService<AnalyticsTelemetrySink>());

        services.AddSingleton<SentryTelemetrySink>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SentryTelemetrySink>>();
            var factory = sp.GetService<IHttpClientFactory>();
            var client = factory != null ? factory.CreateClient("TelemetrySentry") : sp.GetService<HttpClient>();
            return new SentryTelemetrySink(logger, client);
        });
        services.AddSingleton<ITelemetrySink>(sp => sp.GetRequiredService<SentryTelemetrySink>());

        // Register core TelemetryService
        services.AddSingleton<ITelemetryService, TelemetryService>();

        return services;
    }
}
