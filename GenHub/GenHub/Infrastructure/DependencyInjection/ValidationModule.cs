using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services;
using GenHub.Features.Validation;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering validation-related services.
/// </summary>
public static class ValidationModule
{
    /// <summary>
    /// Registers validation services for dependency injection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddValidationServices(this IServiceCollection services)
    {
        // Register core content validator first (used by all other validators)
        services.AddTransient<IContentValidator, ContentValidator>();

        // Register domain-specific validators that use ContentValidator internally
        services.AddTransient<IGameInstallationValidator, GameInstallationValidator>();
        services.AddTransient<IGameClientValidator, GameClientValidator>();

        // Register generic validator interface implementations
        services.AddTransient<IValidator<ContentManifest>>(provider =>
            provider.GetRequiredService<IContentValidator>() as ContentValidator ??
            throw new InvalidOperationException("ContentValidator must implement IValidator<ContentManifest>"));
        services.AddTransient<IValidator<GameInstallation>, GameInstallationValidator>();
        services.AddTransient<IValidator<GameClient>, GameClientValidator>();

        return services;
    }
}
