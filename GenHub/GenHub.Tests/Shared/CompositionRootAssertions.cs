using System;
using System.Collections.Generic;
using System.Linq;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Settings.ViewModels;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GenHub.Tests.Shared;

/// <summary>
/// Shared composition-root assertions, linked into each platform's test project so
/// every host is held to the same contract.
/// <para>
/// This exists because of a specific failure mode. GenHub resolves several services
/// in ways that succeed even when nothing is registered: an optional constructor
/// parameter falls back to a hardcoded default, and an
/// <see cref="IEnumerable{T}"/> injection resolves to an empty list. Both look like
/// success at startup and fail silently at runtime, and unit tests that inject mocks
/// never exercise the real registration. Three shipped bugs traced to that gap.
/// </para>
/// <para>
/// So <c>ValidateOnBuild</c> alone is not enough here. It catches unresolvable
/// constructor dependencies, but every one of those three bugs was a <em>valid</em>
/// resolution to the wrong thing. The explicit assertions below are the part that
/// catches them.
/// </para>
/// </summary>
public static class CompositionRootAssertions
{
    /// <summary>
    /// Services every host must resolve to something. Add to this list whenever a
    /// service becomes required across all platforms; a host that forgets to register
    /// one then fails here rather than degrading quietly in production.
    /// </summary>
    private static readonly Type[] RequiredSingleServices =
    [
        typeof(IConfigurationProviderService),
        typeof(IFileOperationsService),
        typeof(IShortcutService),
        typeof(IVelopackUpdateManager),
    ];

    /// <summary>
    /// Services injected as a collection, where an empty result is a valid resolution
    /// but a broken application. These are the registrations <c>ValidateOnBuild</c>
    /// cannot protect.
    /// </summary>
    private static readonly Type[] RequiredNonEmptyCollections =
    [
        typeof(IGameInstallationDetector),
    ];

    /// <summary>
    /// Types that must be constructible, not merely registered.
    /// <para>
    /// <c>ValidateOnBuild</c> cannot see inside a factory lambda: a registration like
    /// <c>AddSingleton&lt;T&gt;(sp =&gt; new T(sp.GetRequiredService&lt;TDep&gt;()))</c>
    /// validates clean and then throws the first time it is resolved. That is not
    /// hypothetical — <c>SettingsViewModel</c> is registered exactly that way and
    /// required a Windows-only service, so Linux and macOS built a valid container and
    /// then died constructing MainView.
    /// </para>
    /// <para>
    /// Actually resolving these is the only way to execute those lambdas. Add any type
    /// registered with a factory delegate here.
    /// </para>
    /// </summary>
    private static readonly Type[] RequiredConstructibleTypes =
    [
        typeof(SettingsViewModel),
        typeof(MainViewModel),
    ];

    /// <summary>
    /// Builds a host's real container and asserts it is complete.
    /// </summary>
    /// <param name="platformModule">
    /// The host's platform registration callback, exactly as its <c>Program.Main</c>
    /// passes it to <see cref="AppServices.ConfigureApplicationServices"/>.
    /// </param>
    public static void AssertHostContainerIsComplete(
        Func<IServiceCollection, IServiceCollection> platformModule)
    {
        ArgumentNullException.ThrowIfNull(platformModule);

        var services = new ServiceCollection();
        services.ConfigureApplicationServices(platformModule);

        // ValidateOnBuild surfaces unresolvable constructor dependencies at build time
        // instead of at first use.
        //
        // ValidateScopes stays OFF deliberately. Turning it on currently fails on every
        // host with roughly forty captive-dependency errors: singletons that consume
        // scoped services (IGameInstallationService takes IDownloadService and
        // IManifestGenerationService, IContentValidator takes IFileOperationsService,
        // ILaunchRegistry takes IWorkspaceManager, and so on). Those are real defects —
        // a captured scoped service is pinned for the process lifetime, which defeats
        // the scoping — but they are pre-existing, cross-platform, and far too large to
        // fix here. See TODOS.md "Captive dependency audit". Turn this on once that
        // lands; it is the point of the option.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = false,
        });

        using var scope = provider.CreateScope();

        var missing = RequiredSingleServices
            .Where(t => scope.ServiceProvider.GetService(t) is null)
            .Select(t => t.Name)
            .ToList();

        var missingMessage =
            $"Host container resolved null for: {string.Join(", ", missing)}. "
            + "Register these in the platform module, or remove them from RequiredSingleServices "
            + "if they are genuinely optional on this platform.";

        Assert.True(missing.Count == 0, missingMessage);

        var empty = RequiredNonEmptyCollections
            .Where(t => !((IEnumerable<object>)scope.ServiceProvider
                .GetServices(t)).Any())
            .Select(t => t.Name)
            .ToList();

        var emptyMessage =
            $"Host container resolved an EMPTY collection for: {string.Join(", ", empty)}. "
            + "An empty enumerable is a valid resolution, so this would not fail at startup: "
            + "the application would run and silently do nothing. Register at least one "
            + "implementation per platform, even one that legitimately finds nothing.";

        Assert.True(empty.Count == 0, emptyMessage);

        foreach (var type in RequiredConstructibleTypes)
        {
            var failure = Record.Exception(() => scope.ServiceProvider.GetRequiredService(type));

            Assert.True(
                failure is null,
                $"Host container failed to construct {type.Name}: {failure?.Message} "
                + "This is a factory-lambda dependency, which ValidateOnBuild cannot detect. "
                + "The application would start and then crash on first use.");
        }
    }
}
