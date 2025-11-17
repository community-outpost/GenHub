using System;
using GenHub.Core.Interfaces.Localization;
using GenHub.Core.Models.Localization;
using GenHub.Core.Services.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides dependency injection registration for localization services.
/// </summary>
public static class LocalizationServicesModule
{
    /// <summary>
    /// Adds localization services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Optional localization options. If null, default options will be used.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLocalizationServices(
        this IServiceCollection services,
        LocalizationOptions? options = null)
    {
        // Register localization options as singleton
        var localizationOptions = options ?? new LocalizationOptions();
        services.AddSingleton(localizationOptions);

        // Register language provider as singleton
        services.AddSingleton<ILanguageProvider, LanguageProvider>();

        // Register localization service as singleton
        services.AddSingleton<ILocalizationService, LocalizationService>();

        return services;
    }

    /// <summary>
    /// Adds localization services to the service collection with configuration action.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure localization options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLocalizationServices(
        this IServiceCollection services,
        Action<LocalizationOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions, nameof(configureOptions));

        var options = new LocalizationOptions();
        configureOptions(options);

        return services.AddLocalizationServices(options);
    }
}