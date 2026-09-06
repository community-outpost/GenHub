using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Info.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Layout tests for info section controls.
/// </summary>
public class InfoSectionLayoutTests(ITestOutputHelper output)
{
    /// <summary>
    /// Measures GenHubInfoSectionView when Game Settings is selected.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task GenHubInfoSectionView_WhenGameSettingsSelected_MeasureDesiredWidth()
    {
        var (view, window, vm) = await CreateTestViewAsync();

        var gameSettingsSection = vm.Sections.FirstOrDefault(s => s.Id == "game-settings");
        vm.SelectedSection = gameSettingsSection;

        view.Measure(new Size(550, 700));
        view.Arrange(new Rect(0, 0, 550, 700));

        var scroll = (ScrollViewer)view.Content!;
        var stack = (StackPanel)scroll.Content!;

        output.WriteLine($"View bounds: {view.Bounds.Width}x{view.Bounds.Height}");
        output.WriteLine($"Scroll bounds: {scroll.Bounds.Width}x{scroll.Bounds.Height}");
        output.WriteLine($"Stack desired: {stack.DesiredSize.Width}, bounds: {stack.Bounds.Width}");

        stack.DesiredSize.Width.Should().BeLessThanOrEqualTo(550);
        window.Close();
    }

    /// <summary>
    /// Tests that the Scan Wizard demo view renders and handles selection/interaction correctly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task GenHubInfoSectionView_WhenScanForGamesSelected_ScanWizardInteractive()
    {
        var (view, window, vm) = await CreateTestViewAsync();

        var scanSection = vm.Sections.FirstOrDefault(s => s.Id == "game-detection");
        vm.SelectedSection = scanSection;

        vm.DemoScanWizard.Should().NotBeNull();
        vm.DemoScanWizard!.Items.Should().HaveCount(2);
        vm.DemoScanWizard.HasSelectedItems.Should().BeTrue();
        vm.DemoScanWizard.ConfirmLabel.Should().Be("Import Selected (2)");

        // Unselect an item and verify reactive label update
        vm.DemoScanWizard.Items[0].IsSelected = false;
        vm.DemoScanWizard.ConfirmLabel.Should().Be("Import Selected (1)");

        // Measure layout within constrained window
        view.Measure(new Size(500, 700));
        view.Arrange(new Rect(0, 0, 500, 700));

        var scroll = (ScrollViewer)view.Content!;
        var stack = (StackPanel)scroll.Content!;
        stack.DesiredSize.Width.Should().BeLessThanOrEqualTo(500);

        window.Close();
    }

    /// <summary>
    /// Tests that ScrollViewer has sufficient bottom padding to prevent card overlap with floating version button.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task GenHubInfoSectionView_HasBottomClearancePadding()
    {
        var (view, window, _) = await CreateTestViewAsync();

        var scroll = (ScrollViewer)view.Content!;
        scroll.Padding.Bottom.Should().BeGreaterOrEqualTo(100);

        window.Close();
    }

    private static async Task<(GenHubInfoSectionView View, Window Window, GenHubInfoSectionViewModel ViewModel)> CreateTestViewAsync()
    {
        var patchNotesMock = new Mock<IGeneralsOnlinePatchNotesService>();
        var contentProvider = new DefaultInfoContentProvider(patchNotesMock.Object);
        var notifMock = new Mock<INotificationService>();
        var changelogsVm = new ChangelogsViewModel(new Mock<IGitHubApiClient>().Object, NullLogger<ChangelogsViewModel>.Instance);
        var goChangelogsVm = new GeneralsOnlineChangelogViewModel(patchNotesMock.Object, NullLogger<GeneralsOnlineChangelogViewModel>.Instance);
        var vm = new GenHubInfoSectionViewModel(contentProvider, changelogsVm, goChangelogsVm, notifMock.Object);
        await vm.InitializeAsync();

        var view = new GenHubInfoSectionView
        {
            DataContext = vm,
        };

        var window = new Window
        {
            Content = view,
        };
        window.Show();

        return (view, window, vm);
    }
}
