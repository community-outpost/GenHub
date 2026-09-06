using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.Info.ViewModels;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for <see cref="ScanWizardDemoViewModel"/> and <see cref="ScanWizardDemoItemViewModel"/>.
/// </summary>
public class ScanWizardDemoViewModelTests
{
    private readonly Mock<INotificationService> _notificationServiceMock = new();

    /// <summary>
    /// Tests that the factory method creates a view model initialized with detected game items.
    /// </summary>
    [Fact]
    public void CreateDemoScanWizard_InitializesWithDetectedItems()
    {
        var vm = CreateVm();

        vm.Should().NotBeNull();
        vm.Items.Should().HaveCount(2);
        vm.HasSelectedItems.Should().BeTrue();
        vm.ConfirmLabel.Should().Be("Import Selected (2)");
    }

    /// <summary>
    /// Tests that selecting and deselecting items dynamically updates the confirm label and selection state.
    /// </summary>
    [Fact]
    public void SelectionChanged_UpdatesConfirmLabelAndHasSelectedItems()
    {
        var vm = CreateVm();

        // Deselect first item
        vm.Items[0].IsSelected = false;
        vm.ConfirmLabel.Should().Be("Import Selected (1)");
        vm.HasSelectedItems.Should().BeTrue();

        // Deselect second item
        vm.Items[1].IsSelected = false;
        vm.ConfirmLabel.Should().Be("Import Selected");
        vm.HasSelectedItems.Should().BeFalse();

        // Re-select first item
        vm.Items[0].IsSelected = true;
        vm.ConfirmLabel.Should().Be("Import Selected (1)");
        vm.HasSelectedItems.Should().BeTrue();
    }

    /// <summary>
    /// Tests that RescanCommand executes and repopulates all items as selected.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RescanCommand_ExecutesAndRepopulatesItems()
    {
        var vm = CreateVm();
        vm.Items[0].IsSelected = false;

        await vm.RescanCommand.ExecuteAsync(null);

        vm.Items.Should().HaveCount(2);
        vm.Items.Should().OnlyContain(item => item.IsSelected);
        vm.ConfirmLabel.Should().Be("Import Selected (2)");
        vm.HasSelectedItems.Should().BeTrue();
        _notificationServiceMock.Verify(
            n => n.Show(It.Is<NotificationMessage>(m => m.Type == NotificationType.Success && m.Title == "Scan Wizard")),
            Times.Once);
    }

    /// <summary>
    /// Tests that ImportCommand shows success notification when items are selected.
    /// </summary>
    [Fact]
    public void ImportCommand_WithSelectedItems_DispatchesSuccessNotification()
    {
        var vm = CreateVm();

        vm.ImportCommand.Execute(null);

        _notificationServiceMock.Verify(
            n => n.Show(It.Is<NotificationMessage>(m => m.Type == NotificationType.Success && m.Message.Contains("2 game installations"))),
            Times.Once);
    }

    /// <summary>
    /// Tests that ImportCommand does not dispatch notification when no items are selected.
    /// </summary>
    [Fact]
    public void ImportCommand_WithNoSelectedItems_DoesNotDispatchNotification()
    {
        var vm = CreateVm();
        vm.Items[0].IsSelected = false;
        vm.Items[1].IsSelected = false;

        vm.ImportCommand.Execute(null);

        _notificationServiceMock.Verify(
            n => n.Show(It.IsAny<NotificationMessage>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that CancelCommand shows info notification.
    /// </summary>
    [Fact]
    public void CancelCommand_DispatchesInfoNotification()
    {
        var vm = CreateVm();

        vm.CancelCommand.Execute(null);

        _notificationServiceMock.Verify(
            n => n.Show(It.Is<NotificationMessage>(m => m.Type == NotificationType.Info && m.Title == "Scan Wizard")),
            Times.Once);
    }

    private ScanWizardDemoViewModel CreateVm() =>
        DemoViewModelFactory.CreateDemoScanWizard(_notificationServiceMock.Object);
}
