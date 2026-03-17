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
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

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
                                // Call the internal subscription handler
                                // We use reflection or make it public if needed,
                                // but App already has SingleInstance handle logic.
                                // Actually we can just call the public method if we add it.
                                await app.HandleSubscribeCommandAsync(url);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore invalid files
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
