using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.IniEditor;
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
        services.AddTransient<IniEditorViewModel>();
        services.AddSingleton<IToolPlugin, IniEditorToolPlugin>();

        return services;
    }
}
