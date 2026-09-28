using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using GenHub.Common.Controls;
using GenHub.Core.Models.Tools.TextureEditor;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Common.Controls;

/// <summary>
/// Headless tests for <see cref="MappedImagePickerControl"/> selection behavior.
/// </summary>
public sealed class MappedImagePickerControlTests
{
    /// <summary>
    /// Verifies that rebuilding the filtered list preserves a still-matching selection.
    /// </summary>
    [AvaloniaFact]
    public void RefreshFilter_ProviderRefresh_PreservesMatchingSelection()
    {
        RegisterPickerResources();
        var picker = new MappedImagePickerControl();
        var images = new List<MappedImageDefinition>
        {
            new("Alpha", "a.tga", 64, 64, 0, 0, 64, 64),
            new("Beta", "b.tga", 64, 64, 0, 0, 64, 64),
        };
        picker.ItemsSource = images;
        picker.SelectedImage = images[1];

        picker.ThumbnailProvider = _ => null;

        Assert.Same(images[1], picker.SelectedImage);
    }

    /// <summary>
    /// Verifies that a selection disappearing from the source list is cleared.
    /// </summary>
    [AvaloniaFact]
    public void RefreshFilter_SelectedImageRemoved_ClearsSelection()
    {
        RegisterPickerResources();
        var picker = new MappedImagePickerControl();
        var images = new List<MappedImageDefinition>
        {
            new("Alpha", "a.tga", 64, 64, 0, 0, 64, 64),
            new("Beta", "b.tga", 64, 64, 0, 0, 64, 64),
        };
        picker.ItemsSource = images;
        picker.SelectedImage = images[1];

        picker.ItemsSource = new List<MappedImageDefinition> { images[0] };

        Assert.Null(picker.SelectedImage);
    }

    private static void RegisterPickerResources()
    {
        var resources = Avalonia.Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        string[] keys = ["ToolIcon.Magnify", "ToolIcon.ImageOutline", "ToolIcon.ChevronRight", "ToolIcon.PencilOutline"];
        foreach (var key in keys)
        {
            if (!resources.ContainsKey(key))
            {
                resources[key] = new StreamGeometry();
            }
        }
    }
}
