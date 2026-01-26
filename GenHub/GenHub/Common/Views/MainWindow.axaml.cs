using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System;
using System.Linq;

namespace GenHub.Common.Views;

/// <summary>
/// Main application window for GenHub.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class and registers drag-and-drop event handlers for file drop and drag-over.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    /// <summary>
    /// Provides drag-over feedback that permits drops only when the drag data contains files.
    /// </summary>
    /// <param name="sender">The source of the drag event.</param>
    /// <param name="e">Drag event data; its DragEffects is set to Link when files are present, otherwise set to None.</param>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    /// <summary>
    /// Handles files dropped onto the window by processing each dropped JSON file and, when a `catalogUrl` property is found, invoking the application's subscription handler.
    /// </summary>
    /// <remarks>
    /// For each dropped file with a ".json" extension, the method reads and parses the file, extracts the `catalogUrl` string if present and non-empty, and calls <c>HandleSubscribeCommandAsync(url)</c> on the current <c>App</c> instance. Non-JSON files and files that fail to be read or parsed are silently ignored. The method processes files sequentially.
    /// </remarks>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles()?.ToList();
        if (files == null || files.Count == 0) return;

        foreach (var file in files)
        {
            var filePath = file.Path.LocalPath;
            if (System.IO.Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var json = await System.IO.File.ReadAllTextAsync(filePath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("catalogUrl", out var urlProp))
                    {
                        var url = urlProp.GetString();
                        if (!string.IsNullOrEmpty(url))
                        {
                            if (Avalonia.Application.Current is App app)
                            {
                                await app.HandleSubscribeCommandAsync(url);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to parse dropped file: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Handles pointer pressed events on the title bar for dragging.
    /// </summary>
    /// <param name="sender">The sender object.</param>
    /// <param name="e">The pointer event arguments.</param>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, new Avalonia.Interactivity.RoutedEventArgs());
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    /// <summary>
    /// Handles the minimize button click.
    /// </summary>
    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Handles the maximize/restore button click.
    /// </summary>
    private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>
    /// Handles the close button click.
    /// </summary>
    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}