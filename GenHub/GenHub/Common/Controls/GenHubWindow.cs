using Avalonia.Controls;
using GenHub.Common.Helpers;
using System;

namespace GenHub.Common.Controls;

/// <summary>
/// Base window for GenHub dialogs. Applies platform window decorations on construction
/// and equips borderless resizable windows with resize grips on Linux when opened,
/// so dialogs do not repeat the chrome boilerplate.
/// </summary>
public class GenHubWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GenHubWindow"/> class.
    /// </summary>
    public GenHubWindow()
    {
        WindowChromeHelper.ApplyPlatformDecorations(this);
    }

    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowChromeHelper.EnsureResizeGrips(this);
    }
}
