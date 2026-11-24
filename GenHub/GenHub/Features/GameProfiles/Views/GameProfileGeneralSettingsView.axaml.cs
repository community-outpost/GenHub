using Avalonia.Controls;
using GenHub.Features.GameProfiles.ViewModels;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// General and Appearance pages of the profile settings window.
/// </summary>
public partial class GameProfileGeneralSettingsView : UserControl
{
    private readonly PageScrollReset _scrollReset;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileGeneralSettingsView"/> class.
    /// </summary>
    public GameProfileGeneralSettingsView()
    {
        InitializeComponent();
        _scrollReset = new PageScrollReset(GeneralSettingsScrollViewer, nameof(GameProfileSettingsViewModel.SelectedPage));
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _scrollReset.Track(DataContext);
    }
}
