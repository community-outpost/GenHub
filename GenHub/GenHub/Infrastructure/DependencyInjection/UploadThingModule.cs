using System;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Services;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for UploadThing services.
/// </summary>
public static class UploadThingModule
{
    /// <summary>
    /// Registers UploadThing services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddUploadThingServices(this IServiceCollection services)
    {
        services.AddHttpClient<IUploadThingService, UploadThingService>(static client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.DefaultUserAgent);
        });

        services.TryAddSingleton<IUploadHistoryService, UploadHistoryService>();

        return services;
    }
}
