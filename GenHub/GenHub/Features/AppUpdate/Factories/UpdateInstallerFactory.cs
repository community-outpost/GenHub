using System;
using System.Runtime.InteropServices;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.AppUpdate.Factories;

/// <summary>
/// Factory for creating platform-specific update installers.
/// </summary>
public class UpdateInstallerFactory(IServiceProvider serviceProvider, ILogger<UpdateInstallerFactory> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<UpdateInstallerFactory> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Creates a platform-specific update installer.
    /// </summary>
    /// <returns>An <see cref="IUpdateInstaller"/> implementation for the current platform.</returns>
    public IUpdateInstaller CreateInstaller()
    {
        _logger.LogInformation("Creating platform-specific update installer for {OSPlatform}", RuntimeInformation.OSDescription);
        var platformInstaller = _serviceProvider.GetService<IPlatformUpdateInstaller>();
        if (platformInstaller == null)
        {
            var errorMessage = $"No platform-specific update installer registered for {RuntimeInformation.OSDescription}";
            _logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        return platformInstaller;
    }
}
