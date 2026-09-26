using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.ViewModels.Dialogs;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests verifying dialog metadata resetting and catalog filename support.
/// </summary>
public class PublisherStudioDialogAndHostingFixTests
{
    /// <summary>
    /// Verifies that AddArtifactDialogViewModel clears stale local file size and SHA256 when toggled to URL mode.
    /// </summary>
    [Fact]
    public void AddArtifactDialogViewModel_WhenToggledToUrlMode_ClearsStaleFileMetadata()
    {
        var vm = new AddArtifactDialogViewModel(artifact => { });

        vm.UseLocalFile = true;
        vm.LocalFilePath = "/path/to/artifact.zip";
        vm.FileSize = 2048;
        vm.FileSizeDisplay = "2 KB";
        vm.Sha256Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Toggle to URL mode
        vm.UseLocalFile = false;

        Assert.Null(vm.LocalFilePath);
        Assert.Equal(0, vm.FileSize);
        Assert.Empty(vm.FileSizeDisplay);
        Assert.Empty(vm.Sha256Hash);
    }

    /// <summary>
    /// Verifies that AddContentDialogViewModel clears stale local file size and SHA256 when toggled to direct URL mode.
    /// </summary>
    [Fact]
    public void AddContentDialogViewModel_WhenToggledToDirectUrl_ClearsStaleFileMetadata()
    {
        var vm = new AddContentDialogViewModel(_ => { });

        vm.UseDirectUrl = false;
        vm.LocalFilePath = "/path/to/content.zip";
        vm.FileSize = 4096;
        vm.FileSizeDisplay = "4 KB";
        vm.Sha256Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Switch to Direct URL mode
        vm.UseDirectUrl = true;

        Assert.Null(vm.LocalFilePath);
        Assert.Equal(0, vm.FileSize);
        Assert.Empty(vm.FileSizeDisplay);
        Assert.Null(vm.Sha256Hash);
    }
}
