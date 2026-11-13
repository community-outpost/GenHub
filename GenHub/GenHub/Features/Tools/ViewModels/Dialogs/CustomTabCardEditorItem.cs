using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Providers;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// An editable card on a publisher-defined custom tab in Publisher Studio.
/// </summary>
public partial class CustomTabCardEditorItem : ObservableObject
{
    private readonly string _accentColor;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string? _label;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _imageUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomTabCardEditorItem"/> class.
    /// </summary>
    /// <param name="source">The card to edit, or null for a new card.</param>
    public CustomTabCardEditorItem(CatalogTabCardDefinition? source = null)
    {
        var card = source ?? new CatalogTabCardDefinition();
        _accentColor = card.AccentColor;
        Title = card.Title ?? string.Empty;
        Label = card.Label;
        Description = card.Description ?? string.Empty;
        ImageUrl = card.ImageUrl;
    }

    /// <summary>
    /// Builds the catalog card definition from the edited fields.
    /// </summary>
    /// <returns>A new catalog card definition.</returns>
    public CatalogTabCardDefinition ToDefinition()
    {
        return new CatalogTabCardDefinition
        {
            Title = Title.Trim(),
            Label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(),
            Description = Description.Trim(),
            ImageUrl = string.IsNullOrWhiteSpace(ImageUrl) ? null : ImageUrl.Trim(),
            AccentColor = _accentColor,
        };
    }
}
