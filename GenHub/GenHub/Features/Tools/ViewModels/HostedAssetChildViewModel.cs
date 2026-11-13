using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// A child entry shown when a hosted asset row is expanded, such as a content
/// item inside a catalog or a catalog reference inside a definition.
/// </summary>
public partial class HostedAssetChildViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    private string _copyUrl = string.Empty;
}
