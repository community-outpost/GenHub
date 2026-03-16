using GenHub.Core.Interfaces.Info;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Infrastructure module for the Info feature.
/// </summary>
public static class InfoModule
{
    /// <summary>
    /// Registers the Info feature services and ViewModels.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IFaqService, FaqService>();
        services.AddSingleton<IGeneralsOnlinePatchNotesService, GeneralsOnlinePatchNotesService>();
        services.AddSingleton<IInfoContentProvider, DefaultInfoContentProvider>();

        // Register the container ViewModel
        services.AddTransient<InfoViewModel>();

        // Register individual info sections
        services.AddTransient<IInfoSectionViewModel, FaqSectionViewModel>();
        services.AddTransient<IInfoSectionViewModel, GenHubInfoSectionViewModel>();

        // Register view models
        services.AddTransient<ChangelogsViewModel>();
        services.AddTransient<GeneralsOnlineChangelogViewModel>();
    }
}
