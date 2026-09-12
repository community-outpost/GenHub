using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Storage;
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
        typeof(IBackgroundUpdateCoordinator),
        typeof(IConfigurationProviderService),
        typeof(IFileOperationsService),
        typeof(IGamePathProvider),
        typeof(IShortcutService),
        typeof(IStorageMigrationService),
        typeof(ISymlinkCapabilityProvider),
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
    /// Known captive dependencies as (singleton, scoped service) pairs, using the full
    /// service names exactly as <c>ValidateScopes</c> reports them.
    /// See community-outpost/GenHub#320.
    /// <para>
    /// A singleton that consumes a scoped service pins that instance for the process
    /// lifetime, which defeats the scoping. Every pair here is a real defect that
    /// predates scope validation being turned on. This list is SHRINK-ONLY: never add
    /// an entry — fix the lifetime instead. When a capture is fixed the ratchet fails
    /// with a "remove me" message until its pair is deleted, so the list can only get
    /// shorter over time. Pairs (rather than singleton names) are the key so that an
    /// already-listed singleton gaining a NEW scoped dependency still fails.
    /// </para>
    /// </summary>
    private static readonly (string Singleton, string Scoped)[] KnownCaptiveDependencies =
    [
        ("GenHub.Core.Interfaces.Content.IContentValidator", "GenHub.Core.Interfaces.Workspace.IFileOperationsService"),
        ("GenHub.Core.Interfaces.GameInstallations.IGameInstallationService", "GenHub.Core.Interfaces.Manifest.IManifestGenerationService"),
    ];

    private static readonly Regex CaptiveDependencyMessage = new(
        "Cannot consume scoped service '(?<scoped>[^']+)' from singleton '(?<singleton>[^']+)'",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

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

        using var testEnvironment = new TemporaryApplicationEnvironment();
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(platformModule);

        // Scope validation runs first as a shrink-only ratchet: every captive
        // dependency the container can detect must either be fixed or be a
        // pre-existing entry in KnownCaptiveDependencies.
        AssertScopeValidationIsShrinkOnly(services);

        // ValidateOnBuild surfaces unresolvable constructor dependencies at build time
        // instead of at first use.
        //
        // ValidateScopes is OFF for THIS provider only because the known captive
        // dependencies in KnownCaptiveDependencies would make the build throw before the
        // resolution assertions below could run. The ratchet above already enforced
        // scope validation against the same registrations; once its allowlist is empty
        // this flag can simply be flipped on and the ratchet deleted.
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

        var configurationProvider = scope.ServiceProvider.GetRequiredService<IConfigurationProviderService>();
        Assert.Equal(testEnvironment.AppDataPath, configurationProvider.GetRootAppDataPath());
        Assert.Equal(testEnvironment.CasPath, configurationProvider.GetCasConfiguration().CasRootPath);

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
            var failureMessage =
                $"Host container failed to construct {type.Name}: {failure?.Message} "
                + "This is a factory-lambda dependency, which ValidateOnBuild cannot detect. "
                + "The application would start and then crash on first use.";

            Assert.True(failure is null, failureMessage);
        }
    }

    /// <summary>
    /// Builds the container with <c>ValidateScopes</c> enabled and diffs the captive
    /// dependency pairs it reports against <see cref="KnownCaptiveDependencies"/>.
    /// <para>
    /// Fails when a (singleton, scoped service) pair is reported that is not on the
    /// list (fix the lifetime — do not extend the list), and also fails when a listed
    /// pair is no longer reported (remove its entry), so the allowlist can only
    /// shrink.
    /// </para>
    /// </summary>
    private static void AssertScopeValidationIsShrinkOnly(IServiceCollection services)
    {
        var reportedCaptives = new List<(string Singleton, string Scoped)>();

        try
        {
            using (services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            }))
            {
                // Validate container builds and scopes without capturing dependencies.
            }
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.InnerExceptions)
            {
                var match = CaptiveDependencyMessage.Match(inner.Message);
                if (match.Success)
                {
                    reportedCaptives.Add((match.Groups["singleton"].Value, match.Groups["scoped"].Value));
                }
                else
                {
                    // Any error that is not a captive dependency is a real build
                    // failure and should fail the test immediately.
                    throw;
                }
            }
        }

        var newCaptives = reportedCaptives
            .Except(KnownCaptiveDependencies)
            .ToList();

        var newCaptivesMessage =
            "New captive dependencies detected in the container: "
            + $"{string.Join(", ", newCaptives.Select(c => $"{c.Singleton} captures {c.Scoped}"))}. "
            + "Fix the service lifetime (singleton must not consume scoped service); "
            + "do NOT add new entries to KnownCaptiveDependencies.";

        Assert.True(newCaptives.Count == 0, newCaptivesMessage);

        var fixedCaptives = KnownCaptiveDependencies
            .Except(reportedCaptives)
            .ToList();

        var fixedCaptivesMessage =
            "Captive dependencies have been fixed: "
            + $"{string.Join(", ", fixedCaptives.Select(c => $"{c.Singleton} captures {c.Scoped}"))}. "
            + "Remove these entries from KnownCaptiveDependencies to shrink the allowlist.";

        Assert.True(fixedCaptives.Count == 0, fixedCaptivesMessage);
    }
}
