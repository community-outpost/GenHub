using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// Placeholder project file for the interactive ModBuilder demo.
/// </summary>
public partial class ModBuilderDemoFile : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _targetPath;

    [ObservableProperty]
    private string _conversion;

    [ObservableProperty]
    private int _sizeKb;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModBuilderDemoFile"/> class.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="targetPath">The relative install target path.</param>
    /// <param name="conversion">The build conversion applied to the file.</param>
    /// <param name="sizeKb">The file size in kilobytes.</param>
    public ModBuilderDemoFile(string name, string targetPath, string conversion, int sizeKb)
    {
        _name = name;
        _targetPath = targetPath;
        _conversion = conversion;
        _sizeKb = sizeKb;
    }

    /// <summary>
    /// Gets the display label combining name and size.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses generated observable properties Name and SizeKb in partial view model")]
    public string DisplayLabel => $"{Name} ({SizeKb} KB)";
}
