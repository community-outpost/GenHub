using GenHub.Common.ViewModels.Dialogs;
using GenHub.Core.Models.Enums;
using Xunit;

namespace GenHub.Tests.Core.Common.ViewModels.Dialogs;

public class UpdateOptionDialogViewModelTests
{
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
