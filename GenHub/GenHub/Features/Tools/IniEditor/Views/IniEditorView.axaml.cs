using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Features.Tools.IniEditor.ViewModels;

namespace GenHub.Features.Tools.IniEditor.Views;

/// <summary>
/// View for the INI editor tool.
/// </summary>
public partial class IniEditorView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IniEditorView"/> class.
    /// </summary>
    public IniEditorView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        var picker = this.Find<MappedImagePickerControl>("TexturePicker");
        if (picker is not null)
        {
            picker.EditRequested += OnPickerEditRequested;
            picker.ImageActivated += OnPickerImageActivated;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnPickerEditRequested(object? sender, MappedImageDefinition definition)
    {
        if (DataContext is IniEditorViewModel)
        {
            IniEditorViewModel.OpenTextureInEditor(definition);
        }
    }

    private void OnPickerImageActivated(object? sender, MappedImageDefinition definition)
    {
        if (DataContext is IniEditorViewModel viewModel)
        {
            viewModel.AttachTextureToSelectedBlock(definition.Name);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not IniEditorViewModel viewModel)
        {
            return;
        }

        var isPrimary = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        var hasShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var hasAlt = (e.KeyModifiers & KeyModifiers.Alt) != 0;

        if (!isPrimary || hasAlt)
        {
            return;
        }

        if (!hasShift && e.Key == Key.Z)
        {
            if (viewModel.UndoCommand.CanExecute(null))
            {
                viewModel.UndoCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if ((!hasShift && e.Key == Key.Y) || (hasShift && e.Key == Key.Z))
        {
            if (viewModel.RedoCommand.CanExecute(null))
            {
                viewModel.RedoCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (!hasShift && e.Key == Key.S)
        {
            if (viewModel.SaveCommand.CanExecute(null))
            {
                viewModel.SaveCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (hasShift && e.Key == Key.S)
        {
            if (viewModel.SaveAsCommand.CanExecute(null))
            {
                viewModel.SaveAsCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
