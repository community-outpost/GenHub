using System.Collections.Generic;
using GenHub.Features.GameProfiles.ViewModels.Wizard;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels.Wizard;

/// <summary>
/// Unit tests for <see cref="SetupWizardViewModel"/>.
/// </summary>
public class SetupWizardViewModelTests
{
    /// <summary>
    /// Verifies that the constructor initializes labels and items accurately.
    /// </summary>
    [Fact]
    public void Constructor_InitializesLabelsAndItemsCorrectly()
    {
        var items = new List<SetupWizardItemViewModel>
        {
            new() { Title = "Item 1", IsSelected = true, IsMandatory = false },
            new() { Title = "Item 2", IsSelected = true, IsMandatory = false },
            new() { Title = "Item 3", IsSelected = false, IsMandatory = false },
        };

        var vm = new SetupWizardViewModel(items);

        Assert.Equal(3, vm.Items.Count);
        Assert.Equal("Setup Detected Content", vm.Title);
        Assert.Equal("Skip", vm.CancelLabel);
        Assert.Equal("Continue (2)", vm.ConfirmLabel);
        Assert.False(vm.Confirmed);
    }

    /// <summary>
    /// Verifies that ToggleSelectionCommand toggles item selection for non-mandatory items.
    /// </summary>
    [Fact]
    public void ToggleSelectionCommand_WhenItemNonMandatory_TogglesSelectionAndUpdatesLabel()
    {
        var item1 = new SetupWizardItemViewModel { Title = "Item 1", IsSelected = true, IsMandatory = false };
        var item2 = new SetupWizardItemViewModel { Title = "Item 2", IsSelected = false, IsMandatory = false };
        var vm = new SetupWizardViewModel([item1, item2]);

        Assert.Equal("Continue (1)", vm.ConfirmLabel);

        vm.ToggleSelectionCommand.Execute(item1);

        Assert.False(item1.IsSelected);
        Assert.Equal("Continue", vm.ConfirmLabel);

        vm.ToggleSelectionCommand.Execute(item2);

        Assert.True(item2.IsSelected);
        Assert.Equal("Continue (1)", vm.ConfirmLabel);
    }

    /// <summary>
    /// Verifies that ToggleSelectionCommand ignores mandatory items.
    /// </summary>
    [Fact]
    public void ToggleSelectionCommand_WhenItemMandatory_DoesNotToggleSelection()
    {
        var mandatoryItem = new SetupWizardItemViewModel { Title = "Mandatory Item", IsSelected = true, IsMandatory = true };
        var vm = new SetupWizardViewModel([mandatoryItem]);

        Assert.Equal("Continue (1)", vm.ConfirmLabel);

        vm.ToggleSelectionCommand.Execute(mandatoryItem);

        Assert.True(mandatoryItem.IsSelected);
        Assert.Equal("Continue (1)", vm.ConfirmLabel);
    }

    /// <summary>
    /// Verifies that ToggleSelectionCommand does nothing when item is null.
    /// </summary>
    [Fact]
    public void ToggleSelectionCommand_WhenItemNull_DoesNothing()
    {
        var item = new SetupWizardItemViewModel { Title = "Item 1", IsSelected = true, IsMandatory = false };
        var vm = new SetupWizardViewModel([item]);

        vm.ToggleSelectionCommand.Execute(null);

        Assert.True(item.IsSelected);
        Assert.Equal("Continue (1)", vm.ConfirmLabel);
    }

    /// <summary>
    /// Verifies that ConfirmCommand sets Confirmed to true and signals close.
    /// </summary>
    [Fact]
    public void ConfirmCommand_SetsConfirmedAndFiresCloseRequested()
    {
        var item = new SetupWizardItemViewModel { Title = "Item 1", IsSelected = true };
        var vm = new SetupWizardViewModel([item]);
        var closeFired = false;
        vm.CloseRequested += (_, _) => closeFired = true;

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.Confirmed);
        Assert.True(closeFired);
    }

    /// <summary>
    /// Verifies that CancelCommand sets Confirmed to false and signals close.
    /// </summary>
    [Fact]
    public void CancelCommand_SetsConfirmedFalseAndFiresCloseRequested()
    {
        var item = new SetupWizardItemViewModel { Title = "Item 1", IsSelected = true };
        var vm = new SetupWizardViewModel([item]);
        var closeFired = false;
        vm.CloseRequested += (_, _) => closeFired = true;

        vm.CancelCommand.Execute(null);

        Assert.False(vm.Confirmed);
        Assert.True(closeFired);
    }
}
