using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.RmlEditor;
using GenHub.Features.Tools.RmlEditor;
using GenHub.Features.Tools.RmlEditor.Services;
using GenHub.Features.Tools.RmlEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for the RML editor.
/// </summary>
public static class RmlEditorModule
{
    /// <summary>
    /// Registers RML editor services.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRmlEditor(this IServiceCollection services)
    {
        services.AddSingleton<IRmlDocumentService, RmlDocumentService>();
        services.AddSingleton<IRcssDocumentService, RcssDocumentService>();
        services.AddSingleton<RmlPreviewBuilder>();
        services.AddSingleton<RmlImageResolver>();
        services.AddTransient<RmlEditorViewModel>();
        services.AddSingleton<IToolPlugin, RmlEditorToolPlugin>();

        return services;
    }
}
