using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GenHub.Core.Models.Tools.TextureEditor;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace GenHub.Common.Controls;

/// <summary>
/// Shared picker control for browsing and selecting SAGE MappedImage entries.
/// Reused by the Texture Editor today and by future WND and INI editors,
/// so mapped image selection behavior stays in one place.
/// </summary>
public partial class MappedImagePickerControl : UserControl
{
    /// <summary>
    /// Defines the <see cref="ItemsSource"/> property.
    /// </summary>
    public static readonly StyledProperty<IEnumerable<MappedImageDefinition>?> ItemsSourceProperty =
        AvaloniaProperty.Register<MappedImagePickerControl, IEnumerable<MappedImageDefinition>?>(nameof(ItemsSource));

    /// <summary>
    /// Defines the <see cref="SelectedImage"/> property.
    /// </summary>
    public static readonly StyledProperty<MappedImageDefinition?> SelectedImageProperty =
        AvaloniaProperty.Register<MappedImagePickerControl, MappedImageDefinition?>(
            nameof(SelectedImage),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="ThumbnailProvider"/> property.
    /// </summary>
    public static readonly StyledProperty<Func<MappedImageDefinition, IImage?>?> ThumbnailProviderProperty =
        AvaloniaProperty.Register<MappedImagePickerControl, Func<MappedImageDefinition, IImage?>?>(nameof(ThumbnailProvider));

    /// <summary>
    /// Defines the <see cref="FilteredItems"/> property.
    /// </summary>
    public static readonly DirectProperty<MappedImagePickerControl, ObservableCollection<MappedImagePickerItem>> FilteredItemsProperty =
        AvaloniaProperty.RegisterDirect<MappedImagePickerControl, ObservableCollection<MappedImagePickerItem>>(
            nameof(FilteredItems),
            control => control.FilteredItems);

    private readonly ObservableCollection<MappedImagePickerItem> _filteredItems = [];
    private INotifyCollectionChanged? _trackedSource;
    private bool _syncingSelection;

    /// <summary>
    /// Initializes a new instance of the <see cref="MappedImagePickerControl"/> class.
    /// </summary>
    public MappedImagePickerControl()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => RefreshFilter();
        ImagesList.SelectionChanged += OnListSelectionChanged;
        ImagesList.DoubleTapped += (_, _) => RaiseEditRequested();
        EditButton.Click += (_, _) => RaiseEditRequested();
    }

    /// <summary>
    /// Gets or sets the available mapped image entries.
    /// </summary>
    public IEnumerable<MappedImageDefinition>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected mapped image entry.
    /// </summary>
    public MappedImageDefinition? SelectedImage
    {
        get => GetValue(SelectedImageProperty);
        set => SetValue(SelectedImageProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional thumbnail provider for entries.
    /// Returned images are owned by the provider, which is responsible for their lifetime.
    /// </summary>
    public Func<MappedImageDefinition, IImage?>? ThumbnailProvider
    {
        get => GetValue(ThumbnailProviderProperty);
        set => SetValue(ThumbnailProviderProperty, value);
    }

    /// <summary>
    /// Gets the filtered picker items matching the search text.
    /// </summary>
    public ObservableCollection<MappedImagePickerItem> FilteredItems => _filteredItems;

    /// <summary>
    /// Raised when the user requests editing the selected image in the Texture Editor.
    /// </summary>
    public event EventHandler<MappedImageDefinition>? EditRequested;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            TrackItemsSource(change.GetOldValue<IEnumerable<MappedImageDefinition>?>(), change.GetNewValue<IEnumerable<MappedImageDefinition>?>());
            RefreshFilter();
        }
        else if (change.Property == ThumbnailProviderProperty)
        {
            RefreshFilter();
        }
        else if (change.Property == SelectedImageProperty)
        {
            SyncListSelection();
        }
    }

    private static bool MatchesSearch(MappedImageDefinition image, string search)
    {
        if (search.Length == 0)
        {
            return true;
        }

        return image.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            image.TextureFileName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseEditRequested()
    {
        if (SelectedImage is not null)
        {
            EditRequested?.Invoke(this, SelectedImage);
        }
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        try
        {
            _syncingSelection = true;
            SelectedImage = ImagesList.SelectedItem as MappedImagePickerItem is { } item ? item.Definition : null;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void SyncListSelection()
    {
        if (_syncingSelection)
        {
            return;
        }

        try
        {
            _syncingSelection = true;
            ImagesList.SelectedItem = SelectedImage is null
                ? null
                : _filteredItems.FirstOrDefault(item => string.Equals(item.Definition.Name, SelectedImage.Name, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void TrackItemsSource(IEnumerable<MappedImageDefinition>? oldSource, IEnumerable<MappedImageDefinition>? newSource)
    {
        if (_trackedSource is not null)
        {
            _trackedSource.CollectionChanged -= OnItemsSourceCollectionChanged;
            _trackedSource = null;
        }

        if (oldSource is INotifyCollectionChanged oldObservable && !ReferenceEquals(oldSource, newSource))
        {
            oldObservable.CollectionChanged -= OnItemsSourceCollectionChanged;
        }

        if (newSource is INotifyCollectionChanged newObservable)
        {
            _trackedSource = newObservable;
            newObservable.CollectionChanged += OnItemsSourceCollectionChanged;
        }
    }

    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshFilter();

    private void RefreshFilter()
    {
        string search = SearchBox.Text?.Trim() ?? string.Empty;
        var provider = ThumbnailProvider;
        var items = ItemsSource ?? [];
        var matches = items
            .Where(image => MatchesSearch(image, search))
            .OrderBy(image => image.Name, StringComparer.OrdinalIgnoreCase)
            .Select(image => new MappedImagePickerItem(image, provider?.Invoke(image)))
            .ToList();

        try
        {
            _syncingSelection = true;
            _filteredItems.Clear();
            foreach (var item in matches)
            {
                _filteredItems.Add(item);
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        if (SelectedImage is not null && _filteredItems.All(item => !string.Equals(item.Definition.Name, SelectedImage.Name, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedImage = null;
        }

        SyncListSelection();
    }
}
