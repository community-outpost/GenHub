using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service for resolving content dependencies.
/// </summary>
public class DependencyResolver(
    IContentManifestPool manifestPool,
    ILogger<DependencyResolver> logger) : IDependencyResolver
{
    private readonly IContentManifestPool _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
    private readonly ILogger<DependencyResolver> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<HashSet<string>> ResolveDependenciesAsync(IEnumerable<string> contentIds, CancellationToken cancellationToken = default)
    {
        var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toProcess = new Queue<string>(contentIds);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingContentIds = new List<string>();

        while (toProcess.Count > 0)
        {
            var contentId = toProcess.Dequeue();
            if (!visited.Add(contentId))
                continue;

            resolvedIds.Add(contentId);

            try
            {
                var manifestResult = await _manifestPool.GetManifestAsync(ManifestId.Create(contentId), cancellationToken);
                if (manifestResult.Success && manifestResult.Data != null)
                {
                    var manifest = manifestResult.Data;
                    if (manifest.Dependencies != null)
                    {
                        var relevantDeps = manifest.Dependencies.Where(d => d.InstallBehavior == DependencyInstallBehavior.RequireExisting || d.InstallBehavior == DependencyInstallBehavior.AutoInstall);
                        foreach (var dep in relevantDeps)
                        {
                            // Skip default/placeholder IDs - these are generic type-based constraints validated separately
                            if (dep.Id.ToString() == ManifestConstants.DefaultContentDependencyId)
                            {
                                _logger.LogDebug("Skipping generic dependency {DependencyName} (type-based constraint, not specific manifest)", dep.Name);
                                continue;
                            }

                            // Skip type-based dependencies (StrictPublisher = false means any matching type will satisfy)
                            // These use semantic IDs like "1.104.any.gameinstallation.zerohour" and are validated separately
                            if (!dep.StrictPublisher)
                            {
                                _logger.LogDebug("Skipping type-based dependency {DependencyName} (StrictPublisher=false, validated by type matching)", dep.Name);
                                continue;
                            }

                            // TODO: AutoInstall dependencies are resolved here but not automatically installed.
                            // Future PR should implement IAutoInstallService to acquire missing AutoInstall content.
                            if (!resolvedIds.Contains(dep.Id))
                            {
                                toProcess.Enqueue(dep.Id);
                            }
                        }
                    }
                }
                else
                {
                    // Manifest not found - log and collect missing IDs
                    missingContentIds.Add(contentId);
                    _logger.LogWarning("Manifest not found for content ID: {ContentId}", contentId);
                }
            }
            catch (ArgumentException ex)
            {
                // Invalid ID - log and collect as missing
                missingContentIds.Add(contentId);
                _logger.LogWarning(ex, "Invalid manifest ID during dependency resolution: {ContentId}", contentId);
            }
        }

        if (missingContentIds.Count > 0)
        {
            throw new InvalidOperationException($"Missing or invalid content IDs: {string.Join(", ", missingContentIds)}");
        }

        return resolvedIds;
    }

    /// <inheritdoc/>
    public async Task<DependencyResolutionResult> ResolveDependenciesWithManifestsAsync(IEnumerable<string> contentIds, CancellationToken cancellationToken = default)
    {
        var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedManifests = new List<ContentManifest>();
        var missingContentIds = new List<string>();
        var warnings = new List<string>();
        var toProcess = new Queue<string>(contentIds);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processingStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Track currently processing path for circular detection

        while (toProcess.Count > 0)
        {
            var contentId = toProcess.Dequeue();

            // Circular dependency detection
            if (processingStack.Contains(contentId))
            {
                var circularWarning = $"Circular dependency detected: '{contentId}' is already in the resolution path";
                warnings.Add(circularWarning);
                _logger.LogWarning("Circular dependency detected: {ContentId} is already in the resolution path", contentId);
                continue;
            }

            if (!visited.Add(contentId))
                continue;

            processingStack.Add(contentId);
            resolvedIds.Add(contentId);

            try
            {
                var manifestResult = await _manifestPool.GetManifestAsync(ManifestId.Create(contentId), cancellationToken);
                if (manifestResult.Success && manifestResult.Data != null)
                {
                    var manifest = manifestResult.Data;
                    resolvedManifests.Add(manifest);

                    if (manifest.Dependencies != null)
                    {
                        var relevantDeps = manifest.Dependencies.Where(d => d.InstallBehavior == DependencyInstallBehavior.RequireExisting || d.InstallBehavior == DependencyInstallBehavior.AutoInstall);
                        foreach (var dep in relevantDeps)
                        {
                            // Skip default/placeholder IDs - these are generic type-based constraints validated separately
                            if (dep.Id.ToString() == ManifestConstants.DefaultContentDependencyId)
                            {
                                _logger.LogDebug("Skipping generic dependency {DependencyName} (type-based constraint, not specific manifest)", dep.Name);
                                continue;
                            }

                            // Skip type-based dependencies (StrictPublisher = false means any matching type will satisfy)
                            // These use semantic IDs like "1.104.any.gameinstallation.zerohour" and are validated separately
                            if (!dep.StrictPublisher)
                            {
                                _logger.LogDebug("Skipping type-based dependency {DependencyName} (StrictPublisher=false, validated by type matching)", dep.Name);
                                continue;
                            }

                            if (!resolvedIds.Contains(dep.Id))
                            {
                                toProcess.Enqueue(dep.Id);
                            }
                        }
                    }
                }
                else
                {
                    // Manifest not found
                    missingContentIds.Add(contentId);
                    _logger.LogWarning("Manifest not found for content ID: {ContentId}", contentId);
                }
            }
            catch (ArgumentException ex)
            {
                // Invalid ID
                missingContentIds.Add(contentId);
                _logger.LogWarning(ex, "Invalid manifest ID during dependency resolution: {ContentId}", contentId);
            }
            finally
            {
                processingStack.Remove(contentId);
            }
        }

        if (missingContentIds.Count > 0)
        {
            return DependencyResolutionResult.CreateFailure($"Missing or invalid content IDs: {string.Join(", ", missingContentIds)}");
        }

        if (warnings.Count > 0)
        {
            return DependencyResolutionResult.CreateSuccessWithWarnings([..resolvedIds], resolvedManifests, missingContentIds, warnings);
        }

        return DependencyResolutionResult.CreateSuccess([..resolvedIds], resolvedManifests, missingContentIds);
    }
}