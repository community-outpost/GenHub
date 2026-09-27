using Avalonia.Controls;
using GenHub.Common.Helpers;
using System;

namespace GenHub.Common.Controls;

/// <summary>
/// Base window for GenHub dialogs. Applies platform window decorations and equips
/// borderless resizable windows with resize grips on Linux when opened, so dialogs
/// do not repeat the chrome boilerplate. The decoration is applied on open rather
/// than on construction because XAML property assignment runs after the base
/// constructor and would otherwise overwrite the platform value.
/// </summary>
public class GenHubWindow : Window
{
    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowChromeHelper.ApplyPlatformDecorations(this);
        WindowChromeHelper.EnsureResizeGrips(this);
    }
}
