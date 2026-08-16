using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using GenHub.Features.Tools.ModBuilder.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder;

/// <summary>
/// Tool plugin for ModBuilder.
/// </summary>
public sealed class ModBuilderToolPlugin : IToolPlugin
{
    private Control? _rootControl;
    private IServiceProvider? _serviceProvider;

    /// <inheritdoc />
    public ToolMetadata Metadata => new()
    {
        Id = ToolConstants.ModBuilder.Id,
        Name = ToolConstants.ModBuilder.Name,
        Version = ToolConstants.ModBuilder.Version,
        Author = ToolConstants.ModBuilder.Author,
        Description = ToolConstants.ModBuilder.Description,
        IconPath = ToolConstants.ModBuilder.IconPath,
        IsBundled = ToolConstants.ModBuilder.IsBundled,
        Tags = [.. ToolConstants.ModBuilder.Tags],
    };

    /// <inheritdoc />
    public Control CreateControl()
    {
        if (_rootControl != null)
        {
            return _rootControl;
        }

        if (_serviceProvider == null)
        {
            return new TextBlock { Text = "Error loading ModBuilder" };
        }

        // Get ViewModel from DI
        var viewModel = _serviceProvider.GetRequiredService<ModBuilderViewModel>();

        // Initialize the ViewModel
        _ = Task.Run(async () =>
        {
            try
            {
                await viewModel.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetService<ILogger<ModBuilderToolPlugin>>();
                logger?.LogError(ex, "Failed to initialize ModBuilder ViewModel");
            }
        });

        // Create container panel for view switching
        var container = new Panel();

        // Create both views with same ViewModel
        var dashboardView = new ProjectDashboardView { DataContext = viewModel };
        var modBuilderView = new ModBuilderView { DataContext = viewModel };

        // Bind dashboard visibility to !IsProjectLoaded
        dashboardView.Bind(
            Control.IsVisibleProperty,
            new Binding(nameof(ModBuilderViewModel.IsProjectLoaded))
            {
                Converter = new FuncValueConverter<bool, bool>(isLoaded => !isLoaded)
            });

        // Bind modbuilder visibility to IsProjectLoaded
        modBuilderView.Bind(
            Control.IsVisibleProperty,
            new Binding(nameof(ModBuilderViewModel.IsProjectLoaded)));

        // Add both views to container
        container.Children.Add(dashboardView);
        container.Children.Add(modBuilderView);

        _rootControl = container;
        return container;
    }

    /// <inheritdoc />
    public void OnActivated(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public void OnDeactivated()
    {
        // View and ViewModel state is preserved for now.
        // Could call a reset or save method on ViewModel if needed.
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _rootControl = null;
        _serviceProvider = null;
    }
}
