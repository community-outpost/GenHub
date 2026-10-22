using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Window for sharing game profiles via genhub:// URI, Discord invite, or .ghprofile export.
/// </summary>
public partial class ShareProfileDialogWindow : GenHubWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShareProfileDialogWindow"/> class.
    /// </summary>
    public ShareProfileDialogWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override bool DisposeDataContextOnClose => true;

    /// <inheritdoc/>
    protected override bool TitleBarDoubleClickMaximizes => false;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
