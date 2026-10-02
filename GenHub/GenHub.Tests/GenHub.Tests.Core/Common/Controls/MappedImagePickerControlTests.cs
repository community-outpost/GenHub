using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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

    /// <summary>
    /// Verifies that double-tapping a row requests editing the selected image.
    /// </summary>
    [AvaloniaFact]
    public void DoubleTapped_Row_RaisesEditRequested()
    {
        RegisterPickerResources();
        var picker = new MappedImagePickerControl();
        var images = new List<MappedImageDefinition>
        {
            new("Alpha", "a.tga", 64, 64, 0, 0, 64, 64),
        };
        picker.ItemsSource = images;
        picker.SelectedImage = images[0];
        MappedImageDefinition? requested = null;
        picker.EditRequested += (_, definition) => requested = definition;

        var window = new Window { Width = 400, Height = 400, Content = picker };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var list = picker.FindControl<ListBox>("ImagesList");
            Assert.NotNull(list);
            var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));

            // Raising on the realized row bubbles to the list with the row as
            // the source, mirroring a real gesture.
            row.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, null!));

            Assert.Same(images[0], requested);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that double-tapping empty list space does not activate the stale selection.
    /// </summary>
    [AvaloniaFact]
    public void DoubleTapped_EmptySpace_DoesNotRaiseEditRequested()
    {
        RegisterPickerResources();
        var picker = new MappedImagePickerControl();
        var images = new List<MappedImageDefinition>
        {
            new("Alpha", "a.tga", 64, 64, 0, 0, 64, 64),
        };
        picker.ItemsSource = images;
        picker.SelectedImage = images[0];
        MappedImageDefinition? requested = null;
        picker.EditRequested += (_, definition) => requested = definition;

        var window = new Window { Width = 400, Height = 400, Content = picker };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var list = picker.FindControl<ListBox>("ImagesList");
            Assert.NotNull(list);

            // Raising on the list itself carries no row source, mirroring a
            // gesture on the empty space below the last row.
            list.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, null!));

            Assert.Null(requested);
        }
        finally
        {
            window.Close();
        }
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
