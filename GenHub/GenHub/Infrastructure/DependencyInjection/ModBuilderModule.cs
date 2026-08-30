using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for ModBuilder.
/// </summary>
public static class ModBuilderModule
{
    /// <summary>
    /// Registers ModBuilder services.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddModBuilder(this IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<IBuildEngineService, BuildEngineService>();
        services.AddSingleton<IProjectConfigService, ProjectConfigService>();
        services.AddSingleton<IConfigurationLoaderService, ConfigurationLoaderService>();
        services.AddSingleton<IFileConversionService, FileConversionService>();
        services.AddSingleton<IImageConversionService, ImageConversionService>();
        services.AddSingleton<IStringTableConversionService, StringTableConversionService>();
        services.AddSingleton<ITextProcessingService, TextProcessingService>();
        services.AddSingleton<IArchiveService, ArchiveService>();
        services.AddSingleton<IBuildCacheService, BuildCacheService>();
        services.AddSingleton<IExternalToolService, ExternalToolService>();
        services.AddSingleton<IFileHashRegistryService, FileHashRegistryService>();
        services.AddSingleton<IMd5HashProvider, Md5HashProvider>();
        services.AddSingleton<IProjectStructureGenerator, ProjectStructureGenerator>();

        // ViewModels
        services.AddTransient<ModBuilderViewModel>();
        services.AddTransient<FileManagerViewModel>();

        // Tool Plugin
        services.AddSingleton<IToolPlugin, ModBuilderToolPlugin>();

        return services;
    }
}
