using GenHub.Core.Models.Providers;
using GenHub.Core.Services.Tools;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels.Dialogs;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using CoreContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for GenHub build auto-detection and metadata inference in Publisher Studio dialogs.
/// </summary>
public sealed class PublisherStudioGenHubBuildTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "GenHubBuildTests_" + Guid.NewGuid().ToString("N"));
    private readonly GenHubBuildInspector _inspector = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherStudioGenHubBuildTests"/> class.
    /// </summary>
    public PublisherStudioGenHubBuildTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    /// Verifies that staging a GenHub release setup executable auto-selects GenHubBuild content type
    /// and populates version, category, and content ID.
    /// </summary>
    [Fact]
    public void AddContentDialog_WhenGenHubBuildStaged_AutoConfiguresContent()
    {
        var buildPath = WriteTempFile("GenHub-Setup-2.5.0.exe", "fake binary data");
        CatalogContentItem? created = null;

        using var vm = new AddContentDialogViewModel(item => created = item, buildInspector: _inspector);
        vm.PopulateFromPaths([buildPath]);

        Assert.Equal(CoreContentType.GenHubBuild, vm.SelectedContentType);
        Assert.Equal("2.5.0", vm.InitialVersion);
        Assert.Contains("GenHub", vm.ContentName);
        Assert.Contains("genhub", vm.ContentId);

        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(created);
        Assert.Equal(CoreContentType.GenHubBuild, created.ContentType);
        var release = Assert.Single(created.Releases);
        Assert.Equal("2.5.0", release.Version);
        Assert.Equal("Release", release.Category);
        Assert.False(release.IsPrerelease);
    }

    /// <summary>
    /// Verifies that staging a GenHub PR build configures category as Test and sets IsPrerelease.
    /// </summary>
    [Fact]
    public void AddContentDialog_WhenGenHubPrBuildStaged_ConfiguresTestCategoryAndPrerelease()
    {
        var buildPath = WriteTempFile("GenHub-PR-123-1.0.0-dev.1.exe", "fake pr binary");
        CatalogContentItem? created = null;

        using var vm = new AddContentDialogViewModel(item => created = item, buildInspector: _inspector);
        vm.PopulateFromPaths([buildPath]);

        Assert.Equal(CoreContentType.GenHubBuild, vm.SelectedContentType);
        Assert.Equal("1.0.0-dev.1", vm.InitialVersion);

        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(created);
        var release = Assert.Single(created.Releases);
        Assert.Equal("Test", release.Category);
        Assert.True(release.IsPrerelease);
    }

    /// <summary>
    /// Verifies that AddReleaseDialogViewModel auto-detects GenHub builds and configures release version and category.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task AddReleaseDialog_WhenGenHubForkBuildAdded_ConfiguresVersionAsync()
    {
        var buildPath = WriteTempFile("GenHub-Community-Fork-v1.4.0.zip", "fake fork zip");
        var existingContent = new CatalogContentItem
        {
            Id = "genhub-community-fork",
            Name = "Community GenHub Fork",
            ContentType = CoreContentType.GenHubBuild,
        };
        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" },
        };

        ContentRelease? createdRelease = null;
        var mockDialogService = new Mock<IPublisherStudioDialogService>();
        var vm = new AddReleaseDialogViewModel(
            existingContent,
            catalog,
            rel => createdRelease = rel,
            mockDialogService.Object,
            buildInspector: _inspector);

        await vm.AddArtifactsFromPathsAsync([buildPath]);

        Assert.Equal("1.4.0", vm.Version);
        Assert.False(vm.IsPrerelease);
    }

    private string WriteTempFile(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
