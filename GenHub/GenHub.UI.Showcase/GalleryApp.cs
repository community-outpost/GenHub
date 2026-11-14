// <copyright file="GalleryApp.cs" company="Community Outpost">
// Copyright (c) Community Outpost. All rights reserved.
// </copyright>

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using GenHub.Core.Constants;
using GenHub.Common.Services;

namespace GenHub.UI.Showcase;

/// <summary>Displays shared controls without starting application services.</summary>
internal sealed class GalleryApp : Application
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        this.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        this.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://GenHub/"))
        {
            Source = new Uri("avares://GenHub/Assets/Styles/ThemeResources.axaml"),
        });
        this.Styles.Add(new FluentTheme());
        this.Styles.Add(new StyleInclude(new Uri("avares://GenHub/"))
        {
            Source = new Uri("avares://GenHub/Assets/Styles/ButtonStyles.axaml"),
        });
    }

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Creates an isolated component reference window.</summary>
    /// <returns>The component reference window.</returns>
    internal static Window CreateWindow()
    {
        var application = Current ?? throw new InvalidOperationException("The gallery application is not initialized.");
        var body = new StackPanel { Spacing = 20, Margin = new Thickness(28) };
        body.Children.Add(Label("GenHub components", 26));
        body.Children.Add(Label("Developer reference · shared styles, real Fluent controls", 13));
        var themes = new ComboBox
        {
            ItemsSource = ThemeConstants.AllThemes.Select(t => t.Id).ToArray(),
            SelectedIndex = 0,
            Width = 220,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(themes, "Preview accent palette");
        themes.SelectionChanged += (_, _) =>
        {
            var theme = ThemeConstants.AllThemes.FirstOrDefault(t => t.Id == themes.SelectedItem as string)
                ?? ThemeConstants.DefaultTheme;
            var actionColors = ActionButtonPalette.Create(Color.Parse(theme.PrimaryHex));
            application.Resources["ActionPrimaryRestBrush"] = new SolidColorBrush(actionColors.Rest);
            application.Resources["ActionPrimaryHoverBrush"] = new SolidColorBrush(actionColors.Hover);
            application.Resources["ActionPrimaryPressedBrush"] = new SolidColorBrush(actionColors.Pressed);
            application.Resources["AccentBrush"] = new SolidColorBrush(Color.Parse(theme.PrimaryHex));
            application.Resources["AccentLightBrush"] = new SolidColorBrush(Color.Parse(theme.LightHex));
        };
        body.Children.Add(themes);
        body.Children.Add(Label("Actions · hover, press, Tab and Enter to inspect states", 16));
        foreach (var compact in new[] { false, true })
        {
            var row = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            foreach (var (role, text) in new[] { ("action-primary", "Install"), ("action-secondary", "Cancel"), ("action-danger", "Delete") })
            {
                row.Children.Add(Action(role, text, compact));
                var disabled = Action(role, text, compact);
                disabled.IsEnabled = false;
                row.Children.Add(disabled);
            }

            body.Children.Add(Label(compact ? "Compact · toolbars" : "Default · forms and dialogs", 13));
            body.Children.Add(row);
        }

        var icons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        foreach (var key in new[] { "AddIcon", "EditIcon", "RefreshIcon", "CloseIcon" })
        {
            var button = Action("icon-action", string.Empty, false);
            button.Content = new PathIcon { Data = (Geometry)application.FindResource(key)! };
            AutomationProperties.SetName(button, key);
            ToolTip.SetTip(button, key);
            icons.Children.Add(button);
        }

        body.Children.Add(Label("Utility icons · 16px glyph / 32px minimum target", 16));
        body.Children.Add(icons);
        body.Children.Add(Label("Localization and narrow layouts · resize the window", 16));
        foreach (var text in new[] { "Установить необходимые компоненты", "تثبيت العناصر المطلوبة" })
        {
            var button = Action("action-primary", string.Empty, false);
            button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            button.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            button.FlowDirection = text[0] > 0x600 && text[0] < 0x700 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            body.Children.Add(button);
        }

        return new Window
        {
            Title = "GenHub UI component showcase (developer tool)",
            Width = 880,
            Height = 780,
            MinWidth = 360,
            Background = (IBrush)application.FindResource("SurfaceBackgroundBrush")!,
            Foreground = (IBrush)application.FindResource("TextPrimary")!,
            Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        };
    }

    private static TextBlock Label(string text, double size) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };

    private static Button Action(string role, string text, bool compact)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 10, 6) };
        button.Classes.Add(role);
        if (compact)
        {
            button.Classes.Add("action-compact");
        }

        return button;
    }
}
