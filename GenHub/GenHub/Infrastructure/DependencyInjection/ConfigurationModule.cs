using System;
using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for configuration services.
/// </summary>
public static class ConfigurationModule
{
    /// <summary>
    /// Registers configuration services with the service collection.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConfigurationModule(this IServiceCollection services)
    {
        services.AddSingleton<IAppConfiguration, AppConfiguration>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IConfigurationProviderService, ConfigurationProviderService>();

        return services;
    }
}
