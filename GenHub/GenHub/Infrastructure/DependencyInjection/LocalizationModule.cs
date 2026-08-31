using System;
using System.Globalization;
using System.Resources;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for application localization services.
/// </summary>
public static class LocalizationModule
{
    /// <summary>
    /// Registers the shared resource-based localization services.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLocalizationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var resourceAssembly = typeof(LocalizationModule).Assembly;
        var assemblyName = resourceAssembly.GetName().Name
            ?? throw new InvalidOperationException("The GenHub assembly name could not be resolved.");
        var localizationResources = new LocalizationResources(
            new ResourceManager(LocalizationConstants.StringResourceBaseName, resourceAssembly),
            $"{assemblyName}{LocalizationConstants.SatelliteAssemblySuffix}",
            AppContext.BaseDirectory,
            CultureInfo.GetCultureInfo(LocalizationConstants.DefaultCultureName));

        services.AddSingleton(localizationResources);
        services.AddSingleton<ILocalizationService, LocalizationService>();

        return services;
    }
}
