using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Views;

/// <summary>
/// Main application window for GenHub.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // register drag-and-drop handlers to allow subscribing to publisher catalogs by dropping json catalog files directly onto the window
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    /// <summary>
    /// handles drag over events to allow dropping catalog files onto the main window.
    /// </summary>
    /// <param name="sender">the event sender.</param>
    /// <param name="e">the drag event arguments.</param>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // accept file drag operations so users can drop publisher catalog files directly onto the window
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Link : DragDropEffects.None;
    }

    /// <summary>
    /// handles drop events to process catalog json files dropped onto the main window.
    /// </summary>
    /// <param name="sender">the event sender.</param>
    /// <param name="e">the drag event arguments.</param>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        // process dropped files to allow subscribing to publisher catalogs via file drop as an alternative to protocol links
        var files = e.Data.GetFiles()?.ToList();
        if (files == null || files.Count == 0) return;

        foreach (var file in files)
        {
            var filePath = file.Path.LocalPath;
            if (System.IO.Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // inspect json content for catalogurl property to initiate subscription workflow
                    var json = await System.IO.File.ReadAllTextAsync(filePath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("catalogUrl", out var urlProp))
                    {
                        var url = urlProp.GetString();
                        if (!string.IsNullOrEmpty(url))
                        {
                            if (Avalonia.Application.Current is App app)
                            {
                                // route catalog url to main app subscription handler (same flow as genhub://subscribe?url=...)
                                await app.HandleSubscribeCommandAsync(url);
                            }
                        }
                        else if (Avalonia.Application.Current is App app)
                        {
                            var logger = app.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILogger<MainWindow>>();
                            logger?.LogWarning("Dropped catalog JSON has an empty 'catalogUrl' property: {FilePath}", filePath);
                        }
                    }
                    else if (Avalonia.Application.Current is App app)
                    {
                        var logger = app.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILogger<MainWindow>>();
                        logger?.LogWarning("Dropped JSON file does not contain a 'catalogUrl' property: {FilePath}", filePath);
                    }
                }
                catch (Exception ex)
                {
                    if (Avalonia.Application.Current is App app)
                    {
                        var logger = app.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILogger<MainWindow>>();
                        logger?.LogError(ex, "Failed to process dropped JSON file: {FilePath}", filePath);
                    }
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
            if (e.ClickCount == 2 && CanResize)
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
