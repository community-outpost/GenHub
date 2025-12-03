using System;
using Avalonia.Controls;
using Avalonia.Threading;
using GenHub.Features.GitHub.ViewModels;
using System.Collections.Specialized;

namespace GenHub.Features.GitHub.Views;

/// <summary>
/// Interaction logic for InstallationView.xaml.
/// </summary>
public partial class InstallationView : UserControl
{
    private ListBox? _installationLogListBox;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationView"/> class.
    /// </summary>
    public InstallationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is InstallationViewModel viewModel)
        {
            // Subscribe to collection changes for auto-scroll
            viewModel.InstallationLog.CollectionChanged += OnInstallationLogChanged;
        }
    }

    private void OnInstallationLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // Find the ListBox if not already cached
            if (_installationLogListBox == null)
            {
                _installationLogListBox = this.FindControl<ListBox>("InstallationLogListBox");
            }

            // Scroll to the last item
            if (_installationLogListBox != null && _installationLogListBox.Items != null)
            {
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        var lastIndex = _installationLogListBox.ItemCount - 1;
                        if (lastIndex >= 0)
                        {
                            _installationLogListBox.ScrollIntoView(lastIndex);
                        }
                    },
                    DispatcherPriority.Background);
            }
        }
    }
}
