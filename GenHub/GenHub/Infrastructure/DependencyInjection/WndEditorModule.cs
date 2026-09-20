using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor.Services;
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

        return services;
    }
}
