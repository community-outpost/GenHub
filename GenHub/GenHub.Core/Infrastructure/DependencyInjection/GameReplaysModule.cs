using GenHub.Core.Interfaces.GameReplays;
using GenHub.Core.Services.GameReplays;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for GameReplays services.
/// Registers all GameReplays-related services with appropriate lifetimes.
/// </summary>
public static class GameReplaysModule
{
    /// <summary>
    /// Registers GameReplays services with service collection.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    public static void RegisterGameReplaysServices(this IServiceCollection services)
    {
        // Register HTTP client as transient (creates new instance each time)
        services.AddTransient<IGameReplaysHttpClient, GameReplaysHttpClient>();

        // Register parser as transient (stateless, can be created each time)
        services.AddTransient<IGameReplaysParser, GameReplaysParser>();

        // Register auth service as singleton (maintains OAuth state across app)
        services.AddSingleton<IGameReplaysAuthService, GameReplaysAuthService>();

        // Register comment service as transient
        services.AddTransient<IGameReplaysCommentService, GameReplaysCommentService>();

        // Register main service as scoped (one instance per request/scope)
        services.AddScoped<IGameReplaysService, GameReplaysService>();
    }
}
