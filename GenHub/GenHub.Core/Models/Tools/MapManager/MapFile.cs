using Avalonia.Media.Imaging;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GenHub.Core.Models.Tools.MapManager;

/// <summary>
/// Represents a map file with its metadata and associated assets.
/// </summary>
public class MapFile : INotifyPropertyChanged
{
    private Bitmap? _thumbnailBitmap;

    /// <summary>
    /// Event for property change notifications.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the file name of the map.
    /// </summary>
    public required string FileName { get; set; }

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
    public bool IsDirectory { get; set; }

    /// <summary>
    /// Gets or sets the list of asset file paths associated with this map (.tga, .ini, .str, .txt).
    /// </summary>
    public List<string> AssetFiles { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the map directory is expanded in the UI.
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// Gets or sets the display name for this map (parsed from file or directory).
    /// </summary>
    public string? DisplayName { get; set; }

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
}