using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// One variant axis (e.g. resolution) exposed as a labeled ComboBox on content cards.
/// Selecting an option sets the parent card's <see cref="ContentGridItemViewModel.SelectedVariant"/>.
/// Multi-axis lists are rendering infrastructure only — no cross-product filtering.
/// </summary>
public partial class VariantAxisGroup : ObservableObject
{
    /// <summary>
    /// Gets or sets the raw axis key (e.g. "resolution", "game-type", "default").
    /// </summary>
    public string AxisKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display label for the axis (e.g. "Resolution").
    /// Hidden in the UI when the card has only one axis.
    /// </summary>
    public string AxisLabel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether axis labels should be shown (true when the card has multiple axes).
    /// </summary>
    public bool ShowAxisLabel { get; set; }

    /// <summary>
    /// Gets the options for this axis.
    /// </summary>
    public ObservableCollection<InstallableVariant> Options { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether selection change events should be suppressed
    /// (used while syncing from the parent SelectedVariant).
    /// </summary>
    public bool SuppressSelectionEvents { get; set; }

    /// <summary>
    /// Gets or sets the currently selected option within this axis.
    /// </summary>
    [ObservableProperty]
    private InstallableVariant? _selectedOption;

    /// <summary>
    /// Raised when the user picks a different option in this axis's ComboBox.
    /// </summary>
    public event System.Action<InstallableVariant?>? SelectionCommitted;

    partial void OnSelectedOptionChanged(InstallableVariant? value)
    {
        if (SuppressSelectionEvents)
        {
            return;
        }

        SelectionCommitted?.Invoke(value);
    }
}
