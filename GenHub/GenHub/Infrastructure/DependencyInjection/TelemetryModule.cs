using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Utilities;
using GenHub.Features.Telemetry.Services;
using GenHub.Features.Telemetry.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;

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
        services.AddHttpClient("TelemetryAnalytics", c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient("TelemetrySentry", c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<ITelemetrySanitizer, TelemetrySanitizer>();

        // Register default pluggable sinks
        services.AddSingleton<ITelemetrySink, LoggingTelemetrySink>();

        services.AddSingleton<AnalyticsTelemetrySink>(sp =>
            new AnalyticsTelemetrySink(
                sp.GetRequiredService<ILogger<AnalyticsTelemetrySink>>(),
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("TelemetryAnalytics")));
        services.AddSingleton<ITelemetrySink>(sp => sp.GetRequiredService<AnalyticsTelemetrySink>());

        services.AddSingleton<SentryTelemetrySink>(sp =>
            new SentryTelemetrySink(
                sp.GetRequiredService<ILogger<SentryTelemetrySink>>(),
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("TelemetrySentry")));
        services.AddSingleton<ITelemetrySink>(sp => sp.GetRequiredService<SentryTelemetrySink>());

        // Register core TelemetryService
        services.AddSingleton<ITelemetryService, TelemetryService>();

        return services;
    }
}
