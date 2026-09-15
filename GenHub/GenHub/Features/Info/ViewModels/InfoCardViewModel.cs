using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Info;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for an individual information card.
/// </summary>
public partial class InfoCardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private InfoCardType _type;

    [ObservableProperty]
    private bool _isExpandable;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string? _detailedContent;

    [ObservableProperty]
    private List<InfoAction> _actions = [];

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleExpansion()
    {
        if (IsExpandable)
        {
            IsExpanded = !IsExpanded;
        }
    }
}
