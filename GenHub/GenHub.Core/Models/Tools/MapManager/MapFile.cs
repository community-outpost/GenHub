using Avalonia.Media.Imaging;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GenHub.Core.Models.Tools.MapManager;

/// <summary>
/// Represents a map file with its metadata and associated assets.
/// </summary>
public class MapFile : INotifyPropertyChanged
{
    private Bitmap? _thumbnailBitmap;
    private int? _playerCount;
    private string _fileName = string.Empty;
    private bool _isDirectory;
    private List<string> _assetFiles = [];
    private IReadOnlyList<string>? _mapTypeParts;

    /// <summary>
    /// Event for property change notifications.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the file name of the map.
    /// </summary>
    public required string FileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            InvalidateMapTypeParts();
        }
    }

    /// <summary>
    /// Gets or sets the full path to the map file.
    /// </summary>
    public required string FullPath { get; set; }

    /// <summary>
    /// Gets or sets the size of the map file in bytes (includes all assets if directory-based).
    /// </summary>
    public required long SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the game type (Generals or Zero Hour).
    /// </summary>
    public required GameType GameType { get; set; }

    /// <summary>
    /// Gets or sets the last modified timestamp.
    /// </summary>
    public required DateTime LastModified { get; set; }

    /// <summary>
    /// Gets or sets the directory name containing this map (null for root-level maps).
    /// </summary>
    public string? DirectoryName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this map is stored in a directory with assets.
    /// All maps should be directory-based after migration.
    /// </summary>
    public bool IsDirectory
    {
        get => _isDirectory;
        set
        {
            _isDirectory = value;
            InvalidateMapTypeParts();
        }
    }

    /// <summary>
    /// Gets or sets the list of asset file paths associated with this map (.tga, .ini, .str, .txt).
    /// Exposed read-only so in-place mutations cannot silently stale the cached type parts.
    /// </summary>
    public IReadOnlyList<string> AssetFiles
    {
        get => _assetFiles;
        set
        {
            _assetFiles = value?.ToList() ?? [];
            InvalidateMapTypeParts();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the map directory is expanded in the UI.
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// Gets or sets the display name for this map (parsed from file or directory).
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the number of players supported by this map.
    /// </summary>
    public int? PlayerCount
    {
        get => _playerCount;
        set
        {
            if (_playerCount != value)
            {
                _playerCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedPlayerCount));
            }
        }
    }

    /// <summary>
    /// Gets the formatted display text for the number of players.
    /// </summary>
    public string FormattedPlayerCount => PlayerCount is > 0 ? PlayerCount.Value.ToString() : "-";

    /// <summary>
    /// Gets the invariant asset-type parts classifying this map (e.g. Map, Ini, Tga, or Archive).
    /// This is the single classification source shared by display and sorting.
    /// The value is computed once and cached; assigning <see cref="FileName"/>,
    /// <see cref="IsDirectory"/>, or <see cref="AssetFiles"/> invalidates the cache.
    /// </summary>
    public IReadOnlyList<string> MapTypeParts => _mapTypeParts ??= ComputeMapTypeParts();

    /// <summary>
    /// Gets the sort key matching the displayed map type classification.
    /// </summary>
    public string MapTypeSortKey => string.Join(" + ", MapTypeParts);

    /// <summary>
    /// Gets or sets the path to the thumbnail image file (.tga).
    /// </summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Gets or sets the cached thumbnail bitmap for UI display.
    /// </summary>
    public Bitmap? ThumbnailBitmap
    {
        get => _thumbnailBitmap;
        set
        {
            if (_thumbnailBitmap != value)
            {
                _thumbnailBitmap = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Notifies listeners that a property value has changed.
    /// </summary>
    /// <param name="propertyName">Name of the property.</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private IReadOnlyList<string> ComputeMapTypeParts()
    {
        if (!IsDirectory && FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ["Archive"];
        }

        var parts = new List<string> { "Map" };
        if (AssetFiles.Any(f => f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add("Ini");
        }

        if (AssetFiles.Any(f => f.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add("Tga");
        }

        if (AssetFiles.Any(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add("Txt");
        }

        return parts;
    }

    private void InvalidateMapTypeParts()
    {
        _mapTypeParts = null;
    }
}
