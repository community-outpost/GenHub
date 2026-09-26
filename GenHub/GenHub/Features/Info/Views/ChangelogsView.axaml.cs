using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Info.Views;

/// <summary>
/// Interaction logic for ChangelogsView.axaml.
/// </summary>
public partial class ChangelogsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChangelogsView"/> class.
    /// </summary>
    public ChangelogsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the container control for a specific changelog item.
    /// </summary>
    /// <param name="item">The changelog item.</param>
    /// <returns>The container control if found; otherwise, null.</returns>
    public Control? ContainerFromItem(object item)
    {
        return ReleasesItemsControl?.ContainerFromItem(item);
    }
}
