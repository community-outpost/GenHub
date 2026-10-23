using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using GenHub.Common.Markup;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Content;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows.Input;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Appends one top-level tab per publisher-defined custom tab after the built-in tabs of a <see cref="TabControl"/>.
/// </summary>
/// <remarks>
/// The Downloads detail view and the Publisher Studio item preview both render custom tabs through this host,
/// so authors see the same tabs players see. Studio sets <see cref="EditCommandProperty"/> and
/// <see cref="DeleteCommandProperty"/> to add edit affordances to each tab.
/// </remarks>
public static class CustomTabHost
{
    /// <summary>
    /// Identifies the Tabs attached property, the custom tabs to render after the built-in tabs.
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<CustomTabDefinition>?> TabsProperty =
        AvaloniaProperty.RegisterAttached<TabControl, IReadOnlyList<CustomTabDefinition>?>("Tabs", typeof(CustomTabHost));

    /// <summary>
    /// Identifies the BuiltInCount attached property, the number of tabs declared before the custom tabs.
    /// </summary>
    public static readonly AttachedProperty<int> BuiltInCountProperty =
        AvaloniaProperty.RegisterAttached<TabControl, int>("BuiltInCount", typeof(CustomTabHost));

    /// <summary>
    /// Identifies the EditCommand attached property, invoked with a tab when its edit button is clicked.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> EditCommandProperty =
        AvaloniaProperty.RegisterAttached<TabControl, ICommand?>("EditCommand", typeof(CustomTabHost));

    /// <summary>
    /// Identifies the DeleteCommand attached property, invoked with a tab when its delete button is clicked.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> DeleteCommandProperty =
        AvaloniaProperty.RegisterAttached<TabControl, ICommand?>("DeleteCommand", typeof(CustomTabHost));

    private const string EditTabKey = "Tools.PublisherStudio.Content.CustomTabs.EditTab";
    private const string DeleteTabKey = "Tools.PublisherStudio.Content.CustomTabs.DeleteTab";

    private static readonly AttachedProperty<INotifyCollectionChanged?> SubscribedTabsProperty =
        AvaloniaProperty.RegisterAttached<TabControl, INotifyCollectionChanged?>("SubscribedTabs", typeof(CustomTabHost));

    private static readonly AttachedProperty<NotifyCollectionChangedEventHandler?> HandlerProperty =
        AvaloniaProperty.RegisterAttached<TabControl, NotifyCollectionChangedEventHandler?>("Handler", typeof(CustomTabHost));

    static CustomTabHost()
    {
        TabsProperty.Changed.AddClassHandler<TabControl>((tabs, args) =>
        {
            tabs.AttachedToVisualTree -= OnAttachedToVisualTree;
            tabs.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            tabs.AttachedToVisualTree += OnAttachedToVisualTree;
            tabs.DetachedFromVisualTree += OnDetachedFromVisualTree;

            if (tabs.GetVisualRoot() != null)
            {
                Subscribe(tabs);
            }

            Refresh(tabs);
        });
        BuiltInCountProperty.Changed.AddClassHandler<TabControl>((tabs, _) => Refresh(tabs));
        EditCommandProperty.Changed.AddClassHandler<TabControl>((tabs, _) => Refresh(tabs, force: true));
        DeleteCommandProperty.Changed.AddClassHandler<TabControl>((tabs, _) => Refresh(tabs, force: true));
    }

    /// <summary>Gets the custom tabs rendered by a tab control.</summary>
    /// <param name="element">The tab control.</param>
    /// <returns>The custom tabs.</returns>
    public static IReadOnlyList<CustomTabDefinition>? GetTabs(TabControl element) => element.GetValue(TabsProperty);

    /// <summary>Sets the custom tabs rendered by a tab control.</summary>
    /// <param name="element">The tab control.</param>
    /// <param name="value">The custom tabs.</param>
    public static void SetTabs(TabControl element, IReadOnlyList<CustomTabDefinition>? value) => element.SetValue(TabsProperty, value);

    /// <summary>Gets the number of built-in tabs of a tab control.</summary>
    /// <param name="element">The tab control.</param>
    /// <returns>The built-in tab count.</returns>
    public static int GetBuiltInCount(TabControl element) => element.GetValue(BuiltInCountProperty);

    /// <summary>Sets the number of built-in tabs of a tab control.</summary>
    /// <param name="element">The tab control.</param>
    /// <param name="value">The built-in tab count.</param>
    public static void SetBuiltInCount(TabControl element, int value) => element.SetValue(BuiltInCountProperty, value);

    /// <summary>Gets the edit command of a tab control.</summary>
    /// <param name="element">The tab control.</param>
    /// <returns>The edit command.</returns>
    public static ICommand? GetEditCommand(TabControl element) => element.GetValue(EditCommandProperty);

    /// <summary>Sets the edit command of a tab control.</summary>
    /// <param name="element">The tab control.</param>
    /// <param name="value">The edit command.</param>
    public static void SetEditCommand(TabControl element, ICommand? value) => element.SetValue(EditCommandProperty, value);

    /// <summary>Gets the delete command of a tab control.</summary>
    /// <param name="element">The tab control.</param>
    /// <returns>The delete command.</returns>
    public static ICommand? GetDeleteCommand(TabControl element) => element.GetValue(DeleteCommandProperty);

    /// <summary>Sets the delete command of a tab control.</summary>
    /// <param name="element">The tab control.</param>
    /// <param name="value">The delete command.</param>
    public static void SetDeleteCommand(TabControl element, ICommand? value) => element.SetValue(DeleteCommandProperty, value);

    /// <summary>
    /// Replaces the generated tabs after the built-in tabs, unless they already show the given tabs in order.
    /// </summary>
    /// <param name="tabControl">The tab control.</param>
    /// <param name="builtInCount">The number of built-in tabs.</param>
    /// <param name="tabs">The custom tabs to show.</param>
    /// <param name="createItem">Builds the tab item for a custom tab.</param>
    /// <param name="force">Rebuilds even when the generated tabs already match.</param>
    public static void Sync(
        TabControl tabControl,
        int builtInCount,
        IReadOnlyList<CustomTabDefinition> tabs,
        Func<CustomTabDefinition, TabItem> createItem,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(createItem);

        var items = tabControl.Items;
        if (builtInCount <= 0 || items.Count < builtInCount)
        {
            return;
        }

        if (!force && GeneratedTabsMatch(items, builtInCount, tabs))
        {
            return;
        }

        while (items.Count > builtInCount)
        {
            items.RemoveAt(items.Count - 1);
        }

        foreach (var tab in tabs)
        {
            var item = createItem(tab);
            item.Tag = tab;
            items.Add(item);
        }

        if (tabControl.SelectedIndex < 0 || tabControl.SelectedIndex >= items.Count)
        {
            tabControl.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Creates the scrolling content of a custom tab.
    /// </summary>
    /// <param name="tab">The custom tab.</param>
    /// <param name="imageCommand">The command run when a card image is clicked, or null.</param>
    /// <param name="editCommand">The command run by the edit buttons inside the tab, or null to hide them.</param>
    /// <returns>The tab content.</returns>
    public static Control CreateContent(CustomTabDefinition tab, ICommand? imageCommand, ICommand? editCommand)
    {
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new CustomTabContentView
            {
                DataContext = tab,
                ImageCommand = imageCommand,
                EditCommand = editCommand,
            },
        };
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TabControl tabControl)
        {
            Subscribe(tabControl);
            Refresh(tabControl);
        }
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TabControl tabControl)
        {
            Unsubscribe(tabControl);
        }
    }

    private static void Subscribe(TabControl tabControl)
    {
        Unsubscribe(tabControl);
        if (GetTabs(tabControl) is INotifyCollectionChanged observable)
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => Refresh(tabControl);
            observable.CollectionChanged += handler;
            tabControl.SetValue(SubscribedTabsProperty, observable);
            tabControl.SetValue(HandlerProperty, handler);
        }
    }

    private static void Unsubscribe(TabControl tabControl)
    {
        if (tabControl.GetValue(SubscribedTabsProperty) is { } observable && tabControl.GetValue(HandlerProperty) is { } handler)
        {
            observable.CollectionChanged -= handler;
        }

        tabControl.ClearValue(SubscribedTabsProperty);
        tabControl.ClearValue(HandlerProperty);
    }

    private static void Refresh(TabControl tabControl, bool force = false)
    {
        var editCommand = GetEditCommand(tabControl);
        var deleteCommand = GetDeleteCommand(tabControl);
        Sync(
            tabControl,
            GetBuiltInCount(tabControl),
            GetTabs(tabControl) ?? [],
            tab => new TabItem
            {
                Header = CreateHeader(tab, editCommand, deleteCommand),
                Content = CreateContent(tab, null, editCommand),
            },
            force);
    }

    private static object CreateHeader(CustomTabDefinition tab, ICommand? editCommand, ICommand? deleteCommand)
    {
        if (editCommand == null && deleteCommand == null)
        {
            return tab.Header;
        }

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(new TextBlock { Text = tab.Header, VerticalAlignment = VerticalAlignment.Center });
        if (editCommand != null)
        {
            header.Children.Add(CreateHeaderButton("EditIcon", "AccentBrush", editCommand, tab, EditTabKey));
        }

        if (deleteCommand != null)
        {
            header.Children.Add(CreateHeaderButton("DeleteIcon", "StatusErrorBrush", deleteCommand, tab, DeleteTabKey));
        }

        return header;
    }

    private static Button CreateHeaderButton(string iconKey, string brushKey, ICommand command, CustomTabDefinition tab, string tipKey)
    {
        var icon = new PathIcon { Width = 12, Height = 12 };
        if (Application.Current?.TryGetResource(iconKey, theme: null, out var geometry) == true && geometry is Geometry data)
        {
            icon.Data = data;
        }

        icon.Bind(PathIcon.ForegroundProperty, icon.GetResourceObservable(brushKey));
        var button = new Button
        {
            Content = icon,
            Command = command,
            CommandParameter = tab,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (Application.Current?.TryGetResource(LocalizationConstants.ResourceServiceKey, theme: null, out var resource) == true &&
            resource is ILocalizationService localization)
        {
            button.Bind(ToolTip.TipProperty, new LocalizeExtension(tipKey).CreateBinding(localization));
        }

        return button;
    }

    private static bool GeneratedTabsMatch(ItemCollection items, int builtInCount, IReadOnlyList<CustomTabDefinition> tabs)
    {
        if (items.Count - builtInCount != tabs.Count)
        {
            return false;
        }

        for (var i = 0; i < tabs.Count; i++)
        {
            if (items[builtInCount + i] is not TabItem { Tag: var tag } || !ReferenceEquals(tag, tabs[i]))
            {
                return false;
            }
        }

        return true;
    }
}
