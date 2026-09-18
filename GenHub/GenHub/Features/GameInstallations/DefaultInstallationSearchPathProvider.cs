using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;

namespace GenHub.Features.GameInstallations;

/// <summary>
/// Default fallback provider for installation search paths when no platform-specific provider is registered.
/// </summary>
public class DefaultInstallationSearchPathProvider : IInstallationSearchPathProvider
{
    /// <inheritdoc/>
    public virtual IReadOnlyList<string> GetSearchPaths(GameInstallationType installationType)
    {
        return Array.Empty<string>();
    }
}
