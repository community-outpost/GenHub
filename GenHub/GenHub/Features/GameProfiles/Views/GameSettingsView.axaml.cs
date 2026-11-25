using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using GenHub.Features.GameProfiles.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for game configuration settings, listing every option on one page grouped by section, with a search box that filters the rows.
/// </summary>
public partial class GameSettingsView : UserControl
{
    private const string SearchHiddenClass = "search-hidden";
    private const string RowClass = "setting-row";
    private const string SectionHeaderClass = "section-header";
    private const string SectionHeaderRowClass = "section-header-row";
    private const string GroupLabelClass = "group-label";
    private const string RowLabelClass = "row-label";

    private static readonly HashSet<string> RefilterProperties =
    [
        nameof(GameSettingsViewModel.IsTheSuperHackersVisible),
        nameof(GameSettingsViewModel.IsGeneralsOnlineVisible),
        nameof(GameSettingsViewModel.IsCustomCameraVisible),
        nameof(GameSettingsViewModel.IsCustomLodSelected),
        nameof(GameSettingsViewModel.IsZeroHour),
    ];

    private INotifyPropertyChanged? _source;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSettingsView"/> class.
    /// </summary>
    public GameSettingsView()
    {
        InitializeComponent();
        SettingsSearchBox.AddHandler(KeyDownEvent, OnSearchBoxKeyDown, RoutingStrategies.Tunnel);
        ClearSearchButton.Click += OnClearSearchClick;
    }

    /// <summary>
    /// Shows only the rows whose visible label or tooltip contains the query, keeping rows hidden that the profile's game client does not use.
    /// </summary>
    /// <param name="query">The search text; empty shows every row.</param>
    internal void ApplySearch(string? query)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        var rows = CollectRows(SettingsRows);

        var groups = rows.SelectMany(row => new[] { row.Section, row.SubGroup }).OfType<SearchGroup>().Distinct().ToList();
        foreach (var group in groups)
        {
            group.Matches = trimmed.Length > 0 && (Contains(group.Text, trimmed) || Contains(group.TipText, trimmed));
        }

        var anyShown = false;
        foreach (var row in rows)
        {
            var show = trimmed.Length == 0 ||
                       row.Section.Matches ||
                       row.SubGroup?.Matches == true ||
                       Contains(row.Label, trimmed) ||
                       Contains(row.TipText, trimmed);
            row.Element.Classes.Set(SearchHiddenClass, !show);
            row.IsShown = show && IsAllowedByClient(row.Element);
            anyShown |= row.IsShown;
        }

        foreach (var group in groups.Where(group => group.Element != null))
        {
            var show = trimmed.Length == 0 || rows.Any(row => row.IsShown && (row.Section == group || row.SubGroup == group));
            group.Element!.Classes.Set(SearchHiddenClass, !show);
        }

        NoSearchResults.Classes.Set(SearchHiddenClass, trimmed.Length == 0 || anyShown);
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Track(VisualRoot == null ? null : DataContext);
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Track(DataContext);
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Track(null);
    }

    private static bool Contains(string text, string query) =>
        text.Length > 0 &&
        CultureInfo.CurrentUICulture.CompareInfo.IndexOf(text.Trim(), query, CompareOptions.IgnoreCase) >= 0;

    private static string GetHeaderText(Control control) => control switch
    {
        TextBlock textBlock => textBlock.Text ?? string.Empty,
        Panel panel => panel.Children.OfType<TextBlock>().FirstOrDefault(text => text.Classes.Contains(SectionHeaderClass))?.Text ?? string.Empty,
        _ => string.Empty,
    };

    private static List<SearchRow> CollectRows(Panel container)
    {
        var rows = new List<SearchRow>();
        var section = new SearchGroup(null, string.Empty, string.Empty);
        Collect(container, ref section, null, rows);
        return rows;
    }

    private static void Collect(Panel container, ref SearchGroup section, SearchGroup? subGroup, List<SearchRow> rows)
    {
        foreach (var child in container.Children)
        {
            if (child is Border border && border.Classes.Contains(RowClass))
            {
                var label = border.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(text => text.Classes.Contains(RowLabelClass));
                rows.Add(new SearchRow(border, section, subGroup, label?.Text ?? string.Empty, ToolTip.GetTip(border) as string ?? string.Empty));
            }
            else if ((child is TextBlock && child.Classes.Contains(SectionHeaderClass)) || child.Classes.Contains(SectionHeaderRowClass))
            {
                section = new SearchGroup(child, GetHeaderText(child), string.Empty);
                subGroup = null;
            }
            else if (child is TextBlock && child.Classes.Contains(GroupLabelClass))
            {
                subGroup = new SearchGroup(child, GetHeaderText(child), ToolTip.GetTip(child) as string ?? string.Empty);
            }
            else if (child is Panel panel)
            {
                Collect(panel, ref section, subGroup, rows);
            }
        }
    }

    private bool IsAllowedByClient(Control row)
    {
        for (var parent = row.Parent as Control; parent != null && parent != SettingsRows; parent = parent.Parent as Control)
        {
            if (!parent.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    private void Track(object? dataContext)
    {
        if (_source != null)
        {
            _source.PropertyChanged -= OnSourcePropertyChanged;
        }

        _source = dataContext as INotifyPropertyChanged;
        if (_source != null)
        {
            _source.PropertyChanged += OnSourcePropertyChanged;
        }

        ApplySearch((dataContext as GameSettingsViewModel)?.SearchQuery);
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not GameSettingsViewModel settings)
        {
            return;
        }

        if (e.PropertyName == nameof(GameSettingsViewModel.SearchQuery))
        {
            SettingsScrollViewer.Offset = default;
            ApplySearch(settings.SearchQuery);
        }
        else if (e.PropertyName != null && RefilterProperties.Contains(e.PropertyName))
        {
            // Let the client visibility bindings update before reading them.
            Dispatcher.UIThread.Post(() => ApplySearch(settings.SearchQuery));
        }
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is GameSettingsViewModel { SearchQuery.Length: > 0 } settings)
        {
            settings.SearchQuery = string.Empty;
            e.Handled = true;
        }
    }

    private void OnClearSearchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GameSettingsViewModel settings)
        {
            settings.SearchQuery = string.Empty;
            SettingsSearchBox.Focus();
        }
    }

    private sealed class SearchGroup(Control? element, string text, string tipText)
    {
        public Control? Element { get; } = element;

        public string Text { get; } = text;

        public string TipText { get; } = tipText;

        public bool Matches { get; set; }
    }

    private sealed class SearchRow(Border element, SearchGroup section, SearchGroup? subGroup, string label, string tipText)
    {
        public Border Element { get; } = element;

        public SearchGroup Section { get; } = section;

        public SearchGroup? SubGroup { get; } = subGroup;

        public string Label { get; } = label;

        public string TipText { get; } = tipText;

        public bool IsShown { get; set; }
    }
}
