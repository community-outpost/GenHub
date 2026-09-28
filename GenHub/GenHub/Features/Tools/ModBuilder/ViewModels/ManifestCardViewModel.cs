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
    [NotifyPropertyChangedFor(nameof(PacksCount))]
    private IReadOnlyList<string> _packNames = [];

    /// <summary>
    /// Gets a user-friendly summary of the linked bundle packs.
    /// </summary>
    public string PacksSummary => PackNames != null && PackNames.Count > 0
        ? string.Join(", ", PackNames)
        : "None";

    /// <summary>
    /// Gets the count of linked bundle packs.
    /// </summary>
    public int PacksCount => PackNames?.Count ?? 0;
}
