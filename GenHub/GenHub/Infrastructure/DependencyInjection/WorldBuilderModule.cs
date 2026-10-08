using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using GenHub.Features.Tools.WorldBuilder.Services;
using GenHub.Features.Tools.WorldBuilder.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for WorldBuilder.
/// </summary>
public static class WorldBuilderModule
{
    /// <summary>
    /// Registers WorldBuilder services.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWorldBuilder(this IServiceCollection services)
    {
        services.AddSingleton<IGameAssetFileSystem, GameAssetFileSystem>();
        services.AddSingleton<SageIniParser>();
        services.AddSingleton<ISageIniDatabase, SageIniDatabase>();
        services.AddSingleton<IStringTableService, StringTableService>();
        services.AddSingleton<IThingTemplateCatalog, ThingTemplateCatalog>();
        services.AddSingleton<ITerrainTypeCatalog, TerrainTypeCatalog>();
        services.AddSingleton<IRoadCatalog, RoadCatalog>();
        services.AddSingleton<IScriptTemplateCatalog, ScriptTemplateCatalog>();
        services.AddSingleton<ITextureCache, TextureCache>();
        services.AddSingleton<IW3DAssetLoader, W3DAssetLoader>();
        services.AddSingleton<IWorldBuilderContentService, WorldBuilderContentService>();
        services.AddSingleton<WbModelRenderService>();
        services.AddSingleton<WbRoadService>();
        services.AddSingleton<WbBridgeService>();
        services.AddTransient<WbTerrainRenderService>();
        services.AddSingleton<IMapCompressionService, MapCompressionService>();
        services.AddSingleton<IWorldBuilderMapService, WorldBuilderMapService>();
        services.AddSingleton<IMapValidationService, MapValidationService>();
        services.AddSingleton<IMapGenerationService, MapGenerationService>();
        services.AddSingleton<IMapPreviewService, MapPreviewService>();
        services.AddSingleton<ITeamExchangeService, TeamExchangeService>();
        services.AddSingleton<IWorldBuilderProjectService, WorldBuilderProjectService>();
        services.AddSingleton<IWorldBuilderSidecarService, WorldBuilderSidecarService>();
        services.AddTransient<WorldBuilderViewModel>();
        services.AddSingleton<IToolPlugin, WorldBuilderToolPlugin>();

        return services;
    }
}
