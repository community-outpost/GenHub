// File: GenHub/Common/Views/MainView.axaml.cs
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Common.ViewModels;

namespace GenHub.Common.Views;

/// <summary>
/// Code-behind for MainView.
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainView"/> class.
    /// </summary>
    public MainView()
    {
        InitializeComponent();

        // Resolve VM from DI
        DataContext = AppLocator.GetServiceOrDefault<MainViewModel>();

        // Invoke async init
        Loaded += async (_, __) =>
        {
            if (DataContext is MainViewModel vm)
                await vm.InitializeAsync();
        };
    }

    private void InitializeComponent() =>
        AvaloniaXamlLoader.Load(this);
}