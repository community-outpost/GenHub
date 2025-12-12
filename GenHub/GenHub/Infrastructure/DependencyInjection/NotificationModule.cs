using GenHub.Core.Interfaces.Notifications;
using GenHub.Features.Notifications.Services;
using GenHub.Features.Notifications.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for notification services.
/// </summary>
public static class NotificationModule
{
    /// <summary>
    /// Adds notification services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddNotificationModule(this IServiceCollection services)
    {
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<NotificationManagerViewModel>();

        return services;
    }
}