using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.GameInstallations;
using GenHub.Features.Workspace;
using GenHub.Windows.Features.ActionSets;
using GenHub.Windows.Features.ActionSets.Fixes;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using GenHub.Windows.Features.ActionSets.UI;
using GenHub.Windows.Features.GitHub.Services;
using GenHub.Windows.Features.Shortcuts;
using GenHub.Windows.Features.Workspace;
using GenHub.Windows.GameInstallations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Windows.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering Windows-specific services.
/// </summary>
public static class WindowsServicesModule
{
    /// <summary>
    /// Registers Windows-specific services in the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWindowsServices(this IServiceCollection services)
    {
        // Add HttpClient for patches that download content
        services.AddHttpClient();

        // Register Windows-specific services
        services.AddSingleton<IGameInstallationDetector, WindowsInstallationDetector>();
        services.AddSingleton<IGitHubTokenStorage, WindowsGitHubTokenStorage>();
        services.AddSingleton<IShortcutService, WindowsShortcutService>();

        // Register WindowsFileOperationsService with factory to avoid circular dependency
        services.AddScoped<IFileOperationsService>(serviceProvider =>
        {
            var baseService = serviceProvider.GetRequiredService<FileOperationsService>();
            var casService = serviceProvider.GetRequiredService<ICasService>();
            var logger = serviceProvider.GetRequiredService<ILogger<WindowsFileOperationsService>>();
            return new WindowsFileOperationsService(baseService, casService, logger);
        });

        // Register ActionSet Infrastructure
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IActionSetOrchestrator, ActionSetOrchestrator>();

        // Register ActionSets
        services.AddSingleton<IActionSet, BrowserEngineFix>();
        services.AddSingleton<IActionSet, DbgHelpFix>();
        services.AddSingleton<IActionSet, EAAppRegistryFix>();
        services.AddSingleton<IActionSet, MyDocumentsPathCompatibility>();
        services.AddSingleton<IActionSet, VCRedist2008Fix>();
        services.AddSingleton<IActionSet, VCRedist2010Fix>();
        services.AddSingleton<IActionSet, RemoveReadOnlyFix>();
        services.AddSingleton<IActionSet, AppCompatConfigurationsFix>();
        services.AddSingleton<IActionSet, DirectXRuntimeFix>();
        services.AddSingleton<IActionSet, Patch104Fix>();
        services.AddSingleton<IActionSet, Patch108Fix>();
        services.AddSingleton<IActionSet, OptionsINIFix>();
        services.AddSingleton<IActionSet, VanillaExecutableFix>();
        services.AddSingleton<IActionSet, ZeroHourExecutableFix>();
        services.AddSingleton<IActionSet, OneDriveFix>();
        services.AddSingleton<IActionSet, EdgeScrollerFix>();
        services.AddSingleton<IActionSet, TheFirstDecadeRegistryFix>();
        services.AddSingleton<IActionSet, CncOnlineLauncherFix>();
        services.AddSingleton<IActionSet, MalwarebytesFix>();
        services.AddSingleton<IActionSet, D3D8XDLLCheck>();
        services.AddSingleton<IActionSet, NahimicFix>();
        services.AddSingleton<IActionSet, DisableOriginInGame>();
        services.AddSingleton<IActionSet, GenArial>();
        services.AddSingleton<IActionSet, HDIconsFix>();
        services.AddSingleton<IActionSet, WindowsMediaFeaturePack>();
        services.AddSingleton<IActionSet, GameRangerRunAsAdmin>();
        services.AddSingleton<IActionSet, ExpandedLANLobbyMenu>();
        services.AddSingleton<IActionSet, ProxyLauncher>();
        services.AddSingleton<IActionSet, StartMenuFix>();
        services.AddSingleton<IActionSet, SerialKeyFix>();
        services.AddSingleton<IActionSet, IntelGfxDriverCompatibility>();

        // Network Optimization Fixes
        services.AddSingleton<IActionSet, NetworkPrivateProfileFix>();
        services.AddSingleton<IActionSet, PreferIPv4Fix>();
        services.AddSingleton<IActionSet, FirewallExceptionFix>();

        // NOTE: GenPatcherContentActionSetProvider removed - content stubs were non-functional.
        // Content from GenPatcherContentRegistry is available in the Downloads UI.

        // Register GenPatcher Tool
        services.AddSingleton<IToolPlugin, GenPatcherTool>();
        services.AddTransient<GenPatcherViewModel>();

        return services;
    }
}
