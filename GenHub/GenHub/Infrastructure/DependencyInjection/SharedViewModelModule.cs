using Microsoft.Extensions.DependencyInjection;
using GenHub.ViewModels;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class SharedViewModelModule
    {
        public static IServiceCollection AddSharedViewModelModule(this IServiceCollection services)
        {
            services.AddSingleton<MainViewModel>();
            return services;
        }
    }
}
