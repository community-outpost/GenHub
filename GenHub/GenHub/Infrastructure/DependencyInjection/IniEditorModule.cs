using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.IniEditor;
using GenHub.Core.Interfaces.Tools.ModelViewer;
using GenHub.Core.Services.Tools.ModelViewer;
using GenHub.Features.Tools.IniEditor;
using GenHub.Features.Tools.IniEditor.Services;
using GenHub.Features.Tools.IniEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for the INI editor.
/// </summary>
public static class IniEditorModule
{
    /// <summary>
    /// Registers INI editor services.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIniEditor(this IServiceCollection services)
    {
        services.AddSingleton<IIniDocumentService, IniDocumentService>();
        services.AddSingleton<IIniSchemaService, IniSchemaService>();
        services.AddSingleton<IIniReferenceService, IniReferenceService>();
        services.AddSingleton<IW3dParser, W3dParser>();
        services.AddSingleton<IW3dModelResolver, W3dModelResolver>();
        services.AddTransient<IniEditorViewModel>();
        services.AddSingleton<IToolPlugin, IniEditorToolPlugin>();

        return services;
    }
}
