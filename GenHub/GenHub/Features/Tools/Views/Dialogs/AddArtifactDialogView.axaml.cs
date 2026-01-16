using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GenHub.Features.Tools.ViewModels.Dialogs;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// View for adding a new artifact.
/// </summary>
public partial class AddArtifactDialogView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddArtifactDialogView"/> class.
    /// </summary>
    public AddArtifactDialogView()
    {
        InitializeComponent();
    }

    private async void SelectFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Artifact File",
            AllowMultiple = false,
        });

        if (files.Count >= 1 && DataContext is AddArtifactDialogViewModel vm)
        {
            var file = files[0];

            // Try to get local path
            if (file.Path.IsAbsoluteUri && file.Path.Scheme == Uri.UriSchemeFile)
            {
                vm.SelectLocalFileCommand.Execute(file.Path.LocalPath);
            }
            else
            {
                // Fallback or error?
                // For now assuming local file system
            }
        }
    }
}
