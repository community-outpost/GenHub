using Avalonia.Controls;
using GenHub.Features.GameProfiles.ViewModels;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for game configuration settings, showing the section chosen in the profile settings sidebar.
/// </summary>
public partial class GameSettingsView : UserControl
{
    private readonly PageScrollReset _scrollReset;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSettingsView"/> class.
    /// </summary>
    public GameSettingsView()
    {
        InitializeComponent();
        _scrollReset = new PageScrollReset(SettingsScrollViewer, nameof(GameSettingsViewModel.SelectedCategory));
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _scrollReset.Track(DataContext);
    }
}
