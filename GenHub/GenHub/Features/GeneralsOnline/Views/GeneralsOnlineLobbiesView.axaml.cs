using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Features.GeneralsOnline.ViewModels;
using System;
using System.Collections.Specialized;
using System.ComponentModel;

namespace GenHub.Features.GeneralsOnline.Views;

/// <summary>
/// Interaction logic for GeneralsOnlineLobbiesView.axaml.
/// </summary>
public partial class GeneralsOnlineLobbiesView : UserControl
{
    private readonly TextBlock? _motdTextBlock;
    private readonly ListBox? _roomChatListBox;
    private readonly TabControl? _socialTabControl;
    private GeneralsOnlineLobbiesViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineLobbiesView"/> class.
    /// </summary>
    public GeneralsOnlineLobbiesView()
    {
        InitializeComponent();
        _motdTextBlock = this.Find<TextBlock>("MotdTextBlock");
        _roomChatListBox = this.Find<ListBox>("RoomChatListBox");
        _socialTabControl = this.Find<TabControl>("SocialTabControl");
        if (_socialTabControl is not null)
        {
            _socialTabControl.SelectionChanged += OnSocialTabControlSelectionChanged;
        }

        DataContextChanged += OnDataContextChanged;
    }

    private static void AppendRun(InlineCollection inlines, GeneralsOnlineMotdRun run)
    {
        var lines = run.Text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                inlines.Add(new LineBreak());
            }

            var line = lines[index].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var inline = new Run(line);
            if (!string.IsNullOrEmpty(run.ColorHex) && Color.TryParse(run.ColorHex, out var color))
            {
                inline.Foreground = new SolidColorBrush(color);
            }

            inlines.Add(inline);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.RoomChatMessages.CollectionChanged -= OnRoomChatMessagesCollectionChanged;
        }

        _viewModel = DataContext as GeneralsOnlineLobbiesViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.RoomChatMessages.CollectionChanged += OnRoomChatMessagesCollectionChanged;
        }

        RenderMotd();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(GeneralsOnlineLobbiesViewModel.MotdRuns), StringComparison.Ordinal))
        {
            RenderMotd();
        }
    }

    private void OnSocialTabControlSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source == _socialTabControl)
        {
            ScrollChatToEnd();
        }
    }

    private void OnRoomChatMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            ScrollChatToEnd();
        }
    }

    private void ScrollChatToEnd()
    {
        if (_viewModel is not null && _viewModel.RoomChatMessages.Count > 0)
        {
            var lastIndex = _viewModel.RoomChatMessages.Count - 1;
            Dispatcher.UIThread.Post(
                () => _roomChatListBox?.ScrollIntoView(lastIndex),
                DispatcherPriority.Loaded);
        }
    }

    private void RenderMotd()
    {
        var inlines = _motdTextBlock?.Inlines;
        if (inlines is null)
        {
            return;
        }

        inlines.Clear();
        var runs = _viewModel?.MotdRuns;
        if (runs is null)
        {
            return;
        }

        foreach (var run in runs)
        {
            AppendRun(inlines, run);
        }
    }
}
