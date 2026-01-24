namespace GenHub.Windows.Features.ActionSets;

using System;
using Avalonia.Controls;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Windows.Features.ActionSets.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Tool plugin for GenPatcher functionality.
/// </summary>
/// <param name="logger">The logger instance.</param>
public class GenPatcherTool(ILogger<GenPatcherTool> logger) : IToolPlugin
{
    private IServiceProvider? _serviceProvider;

    /// <inheritdoc/>
    public ToolMetadata Metadata => new()
    {
        Id = "GenPatcher",
        Name = "GenPatcher",
        Author = "Legionnaire (Ported)",
        Version = "1.0.0",
        Description = "Apply essential fixes and patches to Command & Conquer Generals and Zero Hour.",
        Tags = ["Fixes", "Patching", "System"],
    };

    /// <inheritdoc/>
    public Control CreateControl()
    {
        var view = new GenPatcherToolView();

        // If we have the service provider, resolve the VM
        if (_serviceProvider != null)
        {
            var vm = _serviceProvider.GetRequiredService<GenPatcherViewModel>();
            if (vm != null)
            {
                view.DataContext = vm;
            }
        }

        return view;
    }

    /// <inheritdoc/>
    public void OnActivated(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        logger.LogInformation("GenPatcher Tool Activated");
    }

    /// <inheritdoc/>
    public void OnDeactivated()
    {
        logger.LogInformation("GenPatcher Tool Deactivated");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Cleanup if needed
    }
}
