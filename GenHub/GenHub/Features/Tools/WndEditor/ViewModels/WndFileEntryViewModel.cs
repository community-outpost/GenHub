namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// A window definition file listed in the explorer.
/// </summary>
/// <param name="FileName">The file name.</param>
/// <param name="FullPath">The full file path.</param>
/// <param name="IsCurrent">Whether this file is open in the editor.</param>
public sealed record WndFileEntryViewModel(string FileName, string FullPath, bool IsCurrent);
