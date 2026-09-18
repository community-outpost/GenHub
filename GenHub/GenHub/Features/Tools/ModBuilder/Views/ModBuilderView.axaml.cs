using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GenHub.Features.Tools.ModBuilder.Views;

/// <summary>
/// View for ModBuilder tool.
/// </summary>
public partial class ModBuilderView : UserControl
{
    private readonly ScrollViewer? _scroller;
    private readonly TextBox? _textBox;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModBuilderView"/> class.
    /// </summary>
    public ModBuilderView()
    {
        InitializeComponent();

        _scroller = this.FindControl<ScrollViewer>("BuildOutputScroller");
        _textBox = this.FindControl<TextBox>("BuildOutputTextBox");

        if (_textBox != null)
        {
            _textBox.PropertyChanged += OnTextBoxPropertyChanged;
        }
    }

    private void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_textBox != null)
                {
                    _textBox.CaretIndex = _textBox.Text?.Length ?? 0;
                }

                if (_scroller != null)
                {
                    _scroller.Offset = new Vector(_scroller.Offset.X, double.MaxValue);
                }
            }, DispatcherPriority.Background);
        }
    }
}
