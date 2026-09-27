using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using GenHub.Common.Controls;
using GenHub.Features.GameProfiles.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Window for adding local content to game profiles.
/// </summary>
public partial class AddLocalContentWindow : GenHubWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddLocalContentWindow"/> class.
    /// </summary>
    public AddLocalContentWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        GenHub.Infrastructure.Interop.AdminDragDropFix.Apply(this, OnAdminDrop);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // The hosted view wires the browse delegates and drag/drop handling, while
        // the window only closes itself when the view model requests it.
        if (DataContext is AddLocalContentViewModel vm)
        {
            vm.RequestClose += OnViewModelRequestClose;
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (DataContext is AddLocalContentViewModel vm)
        {
            vm.RequestClose -= OnViewModelRequestClose;
        }
    }

    private void OnViewModelRequestClose(object? sender, bool result)
    {
        Close(result);
    }

    private void OnAdminDrop(string[] files)
    {
        _ = ProcessAdminDropAsync(files);
    }

    private async Task ProcessAdminDropAsync(string[] files)
    {
        if (DataContext is not AddLocalContentViewModel vm)
        {
            return;
        }

        try
        {
            foreach (var file in files)
            {
                await vm.ImportContentAsync(file);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during admin drop import: {ex.Message}");
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
