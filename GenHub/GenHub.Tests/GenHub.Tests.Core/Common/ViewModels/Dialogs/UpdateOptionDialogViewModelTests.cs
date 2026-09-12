using GenHub.Common.ViewModels.Dialogs;
using GenHub.Core.Models.Enums;
using Xunit;

namespace GenHub.Tests.Core.Common.ViewModels.Dialogs;

/// <summary>
/// Unit tests for <see cref="UpdateOptionDialogViewModel"/>.
/// </summary>
public class UpdateOptionDialogViewModelTests
{
    /// <summary>
    /// Verifies that the default constructor initializes with ReplaceCurrent and DeleteOldVersions set to true.
    /// </summary>
    [Fact]
    public void Constructor_DefaultsToReplaceCurrentWithDeleteOldVersionsTrue()
    {
        var vm = new UpdateOptionDialogViewModel();

        Assert.Equal(UpdateStrategy.ReplaceCurrent, vm.Strategy);
        Assert.True(vm.IsReplaceCurrentVersion);
        Assert.False(vm.IsCreateNewProfile);
        Assert.True(vm.DeleteOldVersions);
        Assert.True(vm.CanDeleteOldVersions);
    }

    /// <summary>
    /// Verifies that selecting CreateNewProfile disables and unchecks DeleteOldVersions.
    /// </summary>
    [Fact]
    public void SetIsCreateNewProfile_DisablesAndUnchecksDeleteOldVersions()
    {
        var vm = new UpdateOptionDialogViewModel
        {
            IsCreateNewProfile = true,
        };

        Assert.Equal(UpdateStrategy.CreateNewProfile, vm.Strategy);
        Assert.False(vm.IsReplaceCurrentVersion);
        Assert.True(vm.IsCreateNewProfile);
        Assert.False(vm.DeleteOldVersions);
        Assert.False(vm.CanDeleteOldVersions);
    }

    /// <summary>
    /// Verifies that switching back to ReplaceCurrentVersion re-enables and checks DeleteOldVersions.
    /// </summary>
    [Fact]
    public void SetIsReplaceCurrentVersion_EnablesAndChecksDeleteOldVersions()
    {
        var vm = new UpdateOptionDialogViewModel
        {
            IsCreateNewProfile = true,
        };

        vm.IsReplaceCurrentVersion = true;

        Assert.Equal(UpdateStrategy.ReplaceCurrent, vm.Strategy);
        Assert.True(vm.IsReplaceCurrentVersion);
        Assert.False(vm.IsCreateNewProfile);
        Assert.True(vm.DeleteOldVersions);
        Assert.True(vm.CanDeleteOldVersions);
    }

    /// <summary>
    /// Verifies that executing UpdateCommand with ReplaceCurrent preserves DeleteOldVersions.
    /// </summary>
    [Fact]
    public void UpdateCommand_WithReplaceCurrent_PopulatesResultWithDeleteOldVersions()
    {
        var vm = new UpdateOptionDialogViewModel
        {
            IsReplaceCurrentVersion = true,
            DeleteOldVersions = true,
            IsDoNotAskAgain = true,
        };
        var closed = false;
        vm.CloseAction = r => closed = true;

        vm.UpdateCommand.Execute(null);

        Assert.True(closed);
        Assert.NotNull(vm.Result);
        Assert.Equal("Update", vm.Result.Action);
        Assert.Equal(UpdateStrategy.ReplaceCurrent, vm.Result.Strategy);
        Assert.True(vm.Result.DeleteOldVersions);
        Assert.True(vm.Result.IsDoNotAskAgain);
    }

    /// <summary>
    /// Verifies that executing UpdateCommand with CreateNewProfile forces DeleteOldVersions to false.
    /// </summary>
    [Fact]
    public void UpdateCommand_WithCreateNewProfile_ForcesDeleteOldVersionsFalse()
    {
        var vm = new UpdateOptionDialogViewModel
        {
            IsCreateNewProfile = true,
        };
        var closed = false;
        vm.CloseAction = r => closed = true;

        vm.UpdateCommand.Execute(null);

        Assert.True(closed);
        Assert.NotNull(vm.Result);
        Assert.Equal("Update", vm.Result.Action);
        Assert.Equal(UpdateStrategy.CreateNewProfile, vm.Result.Strategy);
        Assert.False(vm.Result.DeleteOldVersions);
    }

    /// <summary>
    /// Verifies that executing SkipCommand populates result with Skip action and DeleteOldVersions as false.
    /// </summary>
    [Fact]
    public void SkipCommand_PopulatesResultWithSkipActionAndDeleteOldVersionsFalse()
    {
        var vm = new UpdateOptionDialogViewModel
        {
            IsDoNotAskAgain = true,
        };
        var closed = false;
        vm.CloseAction = r => closed = true;

        vm.SkipCommand.Execute(null);

        Assert.True(closed);
        Assert.NotNull(vm.Result);
        Assert.Equal("Skip", vm.Result.Action);
        Assert.False(vm.Result.DeleteOldVersions);
        Assert.True(vm.Result.IsDoNotAskAgain);
    }
}
