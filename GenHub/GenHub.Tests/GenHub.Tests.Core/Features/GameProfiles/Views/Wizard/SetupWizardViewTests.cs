using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Features.GameProfiles.ViewModels.Wizard;
using GenHub.Features.GameProfiles.Views.Wizard;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Views.Wizard;

/// <summary>
/// Headless tests for <see cref="SetupWizardView"/>.
/// </summary>
public class SetupWizardViewTests
{
    /// <summary>
    /// Verifies that Use installed game keeps readable primary colors at rest and under the pointer.
    /// </summary>
    [AvaloniaFact]
    public void UseInstalledGameButton_UsesPrimaryColorsAtRestAndOnHover()
    {
        var viewModel = new SetupWizardViewModel([], foundGames: ["Zero Hour"]);
        var window = new SetupWizardView { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var button = Assert.Single(
                window.GetVisualDescendants().OfType<Button>(),
                candidate => ReferenceEquals(candidate.Command, viewModel.UseInstalledGameCommand));
            var presenter = Assert.Single(
                button.GetVisualDescendants().OfType<ContentPresenter>(),
                child => child.Name == "PART_ContentPresenter");

            AssertBrush(window, "PrimaryButtonBackground", presenter.Background, "background at rest");
            AssertBrush(window, "PrimaryButtonForeground", presenter.Foreground, "foreground at rest");

            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.True(center.HasValue);
            window.MouseMove(center.Value, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(button.IsPointerOver);
            AssertBrush(window, "AccentDarkBrush", presenter.Background, "background on hover");
            AssertBrush(window, "PrimaryButtonForeground", presenter.Foreground, "foreground on hover");
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertBrush(Control scope, string resourceKey, IBrush? actual, string state)
    {
        Assert.True(scope.TryFindResource(resourceKey, scope.ActualThemeVariant, out var resource));
        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(resource).Color;
        var actualColor = (actual as ISolidColorBrush)?.Color;
        Assert.True(actualColor == expected, $"Use installed game {state} is {actualColor?.ToString() ?? "not a solid brush"}, expected {resourceKey} {expected}.");
    }
}
