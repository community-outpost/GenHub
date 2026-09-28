using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Features.Tools.TextureEditor;
using GenHub.Features.Tools.TextureEditor.Services;
using GenHub.Features.Tools.TextureEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for Texture Editor and shared SAGE texture services.
/// </summary>
public static class TextureEditorModule
{
    /// <summary>
    /// Registers Texture Editor services, viewmodels, and tool plugin.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTextureEditor(this IServiceCollection services)
    {
        // Shared SAGE texture services (also consumed by GenHotkeys and future editors)
        services.AddSingleton<ISageMappedImageParser, SageMappedImageParser>();
        services.AddSingleton<IMappedImageRegistry, MappedImageRegistry>();
        services.AddSingleton<ISageTextureCodec, SageTextureCodec>();
        services.AddSingleton<IAtlasPackingService, AtlasPackingService>();

        // Texture Editor services
        services.AddSingleton<TextureBitmapService>();
        services.AddSingleton<ITextureImageLoader, AvaloniaTextureImageLoader>();

        // ViewModel
        services.AddTransient<TextureEditorViewModel>();

        // Tool Plugin
        services.AddSingleton<IToolPlugin, TextureEditorToolPlugin>();

        return services;
    }
}
