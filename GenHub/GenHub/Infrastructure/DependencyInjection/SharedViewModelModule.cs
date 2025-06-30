using GenHub.ViewModels;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Provides extension methods to register shared ViewModels in the dependency injection container.
    /// </summary>
    public static class SharedViewModelModule
    {
        /// <summary>
        /// Registers shared ViewModels as singletons in the service collection.
        /// </summary>
        /// <param name="services">The service collection to add the ViewModels to.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddSharedViewModelModule(this IServiceCollection services)
        {
            services.AddSingleton<MainViewModel>();
            return services;
        }
    }
}
