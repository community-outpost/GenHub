using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System.IO;
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
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Test artifact data");
            var vm = new AddArtifactDialogViewModel(artifact => { });

            vm.UseLocalFile = true;
            vm.LocalFilePath = tempFile;

            Assert.True(vm.FileSize > 0);
            Assert.False(string.IsNullOrEmpty(vm.Sha256Hash));

            // Toggle to URL mode
            vm.UseLocalFile = false;

            Assert.Null(vm.LocalFilePath);
            Assert.Equal(0, vm.FileSize);
            Assert.Empty(vm.FileSizeDisplay);
            Assert.Empty(vm.Sha256Hash);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that AddContentDialogViewModel clears stale local file size and SHA256 when toggled to direct URL mode.
    /// </summary>
    [Fact]
    public void AddContentDialogViewModel_WhenToggledToDirectUrl_ClearsStaleFileMetadata()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Test content data");
            var vm = new AddContentDialogViewModel();

            vm.UseDirectUrl = false;
            vm.LocalFilePath = tempFile;

            Assert.True(vm.FileSize > 0);
            Assert.False(string.IsNullOrEmpty(vm.Sha256Hash));

            // Switch to Direct URL mode
            vm.UseDirectUrl = true;

            Assert.Null(vm.LocalFilePath);
            Assert.Equal(0, vm.FileSize);
            Assert.Empty(vm.FileSizeDisplay);
            Assert.Null(vm.Sha256Hash);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
