using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// View model representing a manifest card displayed on the ModBuilder dashboard sidebar.
/// </summary>
public partial class ManifestCardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _publisher = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private GameType? _targetGame;

    [ObservableProperty]
    private ContentType? _contentType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PacksSummary))]
    private IReadOnlyList<string> _packNames = [];

    /// <summary>
    /// Gets a user-friendly summary of the linked bundle packs.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound as an instance property in XAML and reads source-generated instance state.")]
    public string PacksSummary => PackNames is { Count: > 0 }
        ? string.Join(", ", PackNames)
        : "-";
}
