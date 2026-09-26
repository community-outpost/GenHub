using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// Placeholder bundle pack for the interactive ModBuilder demo.
/// </summary>
public partial class ModBuilderDemoPack : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _description;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModBuilderDemoPack"/> class.
    /// </summary>
    /// <param name="name">The pack name.</param>
    /// <param name="description">The pack description.</param>
    public ModBuilderDemoPack(string name, string description)
    {
        _name = name;
        _description = description;
        Items = new ObservableCollection<ModBuilderDemoFile>();
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TotalSizeKb));
    }

    /// <summary>
    /// Gets the files bundled in this pack.
    /// </summary>
    public ObservableCollection<ModBuilderDemoFile> Items { get; }

    /// <summary>
    /// Gets the total size of bundled files in kilobytes.
    /// </summary>
    public int TotalSizeKb => Items.Sum(i => i.SizeKb);
}
