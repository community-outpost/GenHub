using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor.Services;
using GenHub.Features.Tools.WndEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for the WND editor.
/// </summary>
public static class WndEditorModule
{
    /// <summary>
    /// Registers WND editor services.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWndEditor(this IServiceCollection services)
    {
        services.AddSingleton<IWndDocumentService, WndDocumentService>();
        services.AddSingleton<IChallengeMedalService, ChallengeMedalService>();
        services.AddSingleton<IWndImageAssetService, WndImageAssetService>();
        services.AddSingleton<IWndTextureImportService, WndTextureImportService>();
        services.AddSingleton<IWndStringTableService, WndStringTableService>();
        services.AddSingleton<IWndEditorAssetService, WndEditorAssetService>();
        services.AddTransient<WndEditorViewModel>();
        services.AddSingleton<IToolPlugin, WndEditorToolPlugin>();

        return services;
    }
}
