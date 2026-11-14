// <copyright file="Program.cs" company="Community Outpost">
// Copyright (c) Community Outpost. All rights reserved.
// </copyright>

using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

namespace GenHub.UI.Showcase;

/// <summary>Starts the isolated developer component gallery.</summary>
internal static class Program
{
    /// <summary>Starts the gallery or saves a headless preview.</summary>
    /// <param name="args">Optional --screenshot and output file path.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        // Developer-only gallery: no application services, games or network clients.
        var builder = AppBuilder.Configure<GalleryApp>().UsePlatformDetect().WithInterFont();
        if (args.Length == 2 && args[0] == "--screenshot")
        {
            builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            var window = GalleryApp.CreateWindow();
            window.Show();
            try
            {
                for (var i = 0; i < 5; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }

                using var frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("The gallery did not render.");
                frame.Save(args[1]);
            }
            finally
            {
                window.Close();
            }
        }
        else
        {
            builder.StartWithClassicDesktopLifetime(args);
        }
    }
}
