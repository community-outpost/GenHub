using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.Views;
using GenHub.Features.Info.ViewModels;
using GenHub.Infrastructure.Converters;
using Material.Icons;
using Material.Icons.Avalonia;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Views;

/// <summary>
/// Headless tests for the sort menu on the Content page.
/// </summary>
public class ContentSortMenuTests
{
    /// <summary>
    /// Verifies the sort menu marks the current order, runs the sort command, and offers Type only under all content.
    /// </summary>
    [AvaloniaFact]
    public void SortMenu_MarksCurrentOrder_AndHidesTypeForGroups()
    {
        var viewModel = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ContentTab();
        var view = new GameProfileContentEditorView { DataContext = viewModel };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            var button = view.FindControl<Button>("ContentSortButton")!;
            Assert.Equal(MaterialIconKind.Sort, Assert.IsType<MaterialIcon>(button.Content).Kind);
            var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            var items = flyout.Items.OfType<MenuItem>().ToList();

            Assert.Equal(4, items.Count);
            Assert.All(items, item => Assert.Same(viewModel.SetProfileContentSortCommand, item.Command));
            Assert.Equal(
                [ProfileContentSortOrder.Name, ProfileContentSortOrder.ReleaseDate, ProfileContentSortOrder.EnabledFirst, ProfileContentSortOrder.Type],
                items.Select(item => (ProfileContentSortOrder)item.CommandParameter!));
            Assert.Equal([true, false, false, false], CheckMarks(items));
            Assert.True(items[3].IsVisible);
            Assert.Equal(SortLabel(ProfileContentSortOrder.Name), AutomationProperties.GetHelpText(button));

            items[1].Command!.Execute(items[1].CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ProfileContentSortOrder.ReleaseDate, viewModel.SelectedProfileContentSort);
            Assert.Equal([false, true, false, false], CheckMarks(items));
            Assert.Equal(SortLabel(ProfileContentSortOrder.ReleaseDate), AutomationProperties.GetHelpText(button));

            viewModel.SelectedProfileContentFilter = new ProfileContentFilterOption(ProfileContentGroup.Maps);
            Dispatcher.UIThread.RunJobs();

            Assert.False(items[3].IsVisible);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    private static string? SortLabel(ProfileContentSortOrder order) =>
        LocalizedProfileContentSortOrderConverter.Instance.Convert(order, typeof(string), null, CultureInfo.InvariantCulture) as string;

    private static List<bool> CheckMarks(IEnumerable<MenuItem> items) =>
        items.Select(item => item.ToggleType == MenuItemToggleType.Radio && item.IsChecked).ToList();
}
