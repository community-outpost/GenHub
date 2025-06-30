using System;
using GenHub.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Provides extension methods to configure application services.
    /// </summary>
    public static class ApplicationServicesModule
    {
        /// <summary>
        /// Configures application-level services and registers them with the dependency injection container.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection ConfigureApplicationServices(
            this IServiceCollection services)
        {
            services.AddLoggingModule();
            services.AddScoped<IGameDetector, DummyGameDetector>();
            return services;
        }
    }
}
