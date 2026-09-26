using Avalonia.Controls;

namespace GenHub.Features.Info.Views;

/// <summary>
/// Interaction logic for GeneralsOnlineChangelogView.axaml.
/// </summary>
public partial class GeneralsOnlineChangelogView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineChangelogView"/> class.
    /// </summary>
    public GeneralsOnlineChangelogView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the container control for a specific patch note item.
    /// </summary>
    /// <param name="item">The patch note item.</param>
    /// <returns>The container control if found; otherwise, null.</returns>
    public Control? ContainerFromItem(object item)
    {
        return PatchNotesItemsControl?.ContainerFromItem(item);
    }
}
