using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;

namespace GenHub.Features.Tools.Views;

/// <summary>
/// Dialog for sharing an upload via plain download link or GenHub protocol link.
/// </summary>
public partial class ShareLinksDialog : GenHubWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShareLinksDialog"/> class.
    /// </summary>
    public ShareLinksDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
