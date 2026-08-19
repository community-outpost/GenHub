using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Contains tests for <see cref="AddLocalContentViewModel"/>.
/// </summary>
public class AddLocalContentViewModelTests : IDisposable
{
    private readonly Mock<ILocalContentService> _localContentServiceMock;
    private readonly Mock<IContentStorageService> _contentStorageServiceMock;
    private readonly Mock<IGenLauncherNormalizationService> _normalizationServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly List<string> _tempDirectories = [];
    private readonly List<AddLocalContentViewModel> _viewModels = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AddLocalContentViewModelTests"/> class.
    /// </summary>
    public AddLocalContentViewModelTests()
    {
        _localContentServiceMock = new Mock<ILocalContentService>();
        _contentStorageServiceMock = new Mock<IContentStorageService>();
        _normalizationServiceMock = new Mock<IGenLauncherNormalizationService>();
        _dialogServiceMock = new Mock<IDialogService>();

        _localContentServiceMock
            .Setup(x => x.AllowedContentTypes)
            .Returns(AddLocalContentViewModel.AllowedContentTypes);

        _normalizationServiceMock
            .Setup(x => x.DetectGenLauncherFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenLauncherDetectionResult());
    }

    /// <summary>
    /// Cleans up temporary test directories and viewmodels.
    /// </summary>
    public void Dispose()
    {
        foreach (var vm in _viewModels)
        {
            vm.Dispose();
        }

        foreach (var dir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that the ViewModel initializes with proper defaults.
    /// </summary>
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        var vm = CreateViewModel();

        Assert.NotNull(vm);
        Assert.Equal(ContentType.Mod, vm.SelectedContentType);
        Assert.Equal(GameType.ZeroHour, vm.SelectedGameType);
        Assert.Empty(vm.ContentName);
        Assert.Empty(vm.SourcePath);
        Assert.Empty(vm.FileTree);
        Assert.False(vm.IsEditing);
        Assert.False(vm.CanAdd);
        Assert.False(vm.ShowExecutableSelection);
        Assert.Null(vm.SelectedExecutableItem);
        Assert.Equal(0, vm.ExecutableCount);
        Assert.Equal("Add Local Content", vm.DialogTitle);
        Assert.Equal("Add to Library", vm.ActionButtonText);
        Assert.Contains(ContentType.GameClient, AddLocalContentViewModel.AllowedContentTypes);
        Assert.Contains(ContentType.ModdingTool, AddLocalContentViewModel.AllowedContentTypes);
        Assert.Contains(ContentType.Executable, AddLocalContentViewModel.AllowedContentTypes);
    }

    /// <summary>
    /// Verifies that PreviewIdleText changes based on SelectedContentType.
    /// </summary>
    /// <param name="type">The content type under test.</param>
    /// <param name="expectedText">The expected idle description text.</param>
    [Theory]
    [InlineData(ContentType.Mod, "Import mod content (e.g. .big, .zip)")]
    [InlineData(ContentType.GameClient, "Import GameClient")]
    [InlineData(ContentType.Executable, "Import executable")]
    [InlineData(ContentType.ModdingTool, "Import tool executable")]
    [InlineData(ContentType.Patch, "Import patch")]
    [InlineData(ContentType.Addon, "Import addon content")]
    [InlineData(ContentType.Map, "Import map files")]
    [InlineData(ContentType.MapPack, "Import map pack files")]
    [InlineData(ContentType.Mission, "Import mission content")]
    public void PreviewIdleText_ReturnsExpectedDescriptions(ContentType type, string expectedText)
    {
        var vm = CreateViewModel();
        vm.SelectedContentType = type;

        Assert.Equal(expectedText, vm.PreviewIdleText);
    }

    /// <summary>
    /// Verifies that ShowExecutableSelection is true when ExecutableCount > 0 for GameClient, ModdingTool, and Executable.
    /// </summary>
    /// <param name="contentType">The content type under test.</param>
    /// <param name="executableCount">The number of detected executables.</param>
    /// <param name="expectedShow">The expected boolean indicating whether executable selection is shown.</param>
    [Theory]
    [InlineData(ContentType.GameClient, 1, true)]
    [InlineData(ContentType.GameClient, 2, true)]
    [InlineData(ContentType.ModdingTool, 1, true)]
    [InlineData(ContentType.ModdingTool, 2, true)]
    [InlineData(ContentType.Executable, 1, true)]
    [InlineData(ContentType.Executable, 2, true)]
    [InlineData(ContentType.GameClient, 0, false)]
    [InlineData(ContentType.ModdingTool, 0, false)]
    [InlineData(ContentType.Executable, 0, false)]
    [InlineData(ContentType.Mod, 1, false)]
    [InlineData(ContentType.Mod, 2, false)]
    [InlineData(ContentType.Patch, 1, false)]
    [InlineData(ContentType.Map, 1, false)]
    public void ShowExecutableSelection_EvaluatesCorrectly_BasedOnContentTypeAndExecutableCount(
        ContentType contentType,
        int executableCount,
        bool expectedShow)
    {
        var vm = CreateViewModel();
        vm.SelectedContentType = contentType;
        vm.ExecutableCount = executableCount;

        Assert.Equal(expectedShow, vm.ShowExecutableSelection);
    }

    /// <summary>
    /// Verifies that importing a directory with an executable auto-selects the executable for GameClient.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportContentAsync_WithSingleExecutable_ForGameClient_AutoSelectsExecutable()
    {
        var tempDir = CreateTempDirectory();
        var exePath = Path.Combine(tempDir, "generals.exe");
        var dataPath = Path.Combine(tempDir, "data.ini");
        File.WriteAllText(exePath, "fake-exe-content");
        File.WriteAllText(dataPath, "fake-data");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.GameClient;

        await vm.ImportContentAsync(tempDir);

        Assert.Equal(1, vm.ExecutableCount);
        Assert.True(vm.ShowExecutableSelection);
        Assert.NotNull(vm.SelectedExecutableItem);
        Assert.Equal("generals.exe", vm.SelectedExecutableItem!.Name);
        Assert.True(vm.SelectedExecutableItem.IsSelectedExecutable);
    }

    /// <summary>
    /// Verifies that importing a directory with an executable auto-selects the executable for ModdingTool.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportContentAsync_WithSingleExecutable_ForModdingTool_AutoSelectsExecutable()
    {
        var tempDir = CreateTempDirectory();
        var exePath = Path.Combine(tempDir, "FinalBIG.exe");
        var dataPath = Path.Combine(tempDir, "readme.txt");
        File.WriteAllText(exePath, "fake-exe-content");
        File.WriteAllText(dataPath, "read me");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.ModdingTool;

        await vm.ImportContentAsync(tempDir);

        Assert.Equal(1, vm.ExecutableCount);
        Assert.True(vm.ShowExecutableSelection);
        Assert.NotNull(vm.SelectedExecutableItem);
        Assert.Equal("FinalBIG.exe", vm.SelectedExecutableItem!.Name);
        Assert.True(vm.SelectedExecutableItem.IsSelectedExecutable);
    }

    /// <summary>
    /// Verifies that importing a directory with an executable auto-selects the executable for Executable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportContentAsync_WithSingleExecutable_ForExecutable_AutoSelectsExecutable()
    {
        var tempDir = CreateTempDirectory();
        var exePath = Path.Combine(tempDir, "WorldBuilder.exe");
        File.WriteAllText(exePath, "fake-exe-content");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.Executable;

        await vm.ImportContentAsync(tempDir);

        Assert.Equal(1, vm.ExecutableCount);
        Assert.True(vm.ShowExecutableSelection);
        Assert.NotNull(vm.SelectedExecutableItem);
        Assert.Equal("WorldBuilder.exe", vm.SelectedExecutableItem!.Name);
        Assert.True(vm.SelectedExecutableItem.IsSelectedExecutable);
    }

    /// <summary>
    /// Verifies that switching to an executable content type triggers auto-selection if an executable is in the tree.
    /// </summary>
    /// <param name="newType">The executable content type to switch to.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(ContentType.GameClient)]
    [InlineData(ContentType.ModdingTool)]
    [InlineData(ContentType.Executable)]
    public async Task SelectedContentTypeChanged_ToExecutableType_AutoSelectsFirstExecutable(ContentType newType)
    {
        var tempDir = CreateTempDirectory();
        var exePath = Path.Combine(tempDir, "Launcher.exe");
        File.WriteAllText(exePath, "fake-exe-content");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.Mod;

        await vm.ImportContentAsync(tempDir);

        // When imported as Mod, no auto-selection happened
        Assert.Null(vm.SelectedExecutableItem);
        Assert.False(vm.ShowExecutableSelection);

        // Switch to executable type
        vm.SelectedContentType = newType;

        Assert.NotNull(vm.SelectedExecutableItem);
        Assert.Equal("Launcher.exe", vm.SelectedExecutableItem!.Name);
        Assert.True(vm.SelectedExecutableItem.IsSelectedExecutable);
        Assert.True(vm.ShowExecutableSelection);
    }

    /// <summary>
    /// Verifies manual selection of an executable via SelectExecutableCommand.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectExecutableCommand_SwitchesSelectedExecutable()
    {
        var tempDir = CreateTempDirectory();
        var exe1Path = Path.Combine(tempDir, "Primary.exe");
        var exe2Path = Path.Combine(tempDir, "Secondary.exe");
        File.WriteAllText(exe1Path, "fake-exe-1");
        File.WriteAllText(exe2Path, "fake-exe-2");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.ModdingTool;

        await vm.ImportContentAsync(tempDir);

        Assert.Equal(2, vm.ExecutableCount);
        Assert.NotNull(vm.SelectedExecutableItem);

        var initialSelected = vm.SelectedExecutableItem!;
        var otherItem = FindInTree(vm.FileTree, f => f != initialSelected && f.IsExecutable);
        Assert.NotNull(otherItem);
        Assert.False(otherItem!.IsSelectedExecutable);

        // Select the other executable
        vm.SelectExecutableCommand.Execute(otherItem);

        Assert.Equal(otherItem.Name, vm.SelectedExecutableItem.Name);
        Assert.True(otherItem.IsSelectedExecutable);
        Assert.False(initialSelected.IsSelectedExecutable);
    }

    /// <summary>
    /// Verifies that SelectExecutableCommand ignores non-executable files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectExecutableCommand_IgnoresNonExecutableItem()
    {
        var tempDir = CreateTempDirectory();
        var exePath = Path.Combine(tempDir, "Tool.exe");
        var txtPath = Path.Combine(tempDir, "Doc.txt");
        File.WriteAllText(exePath, "fake-exe");
        File.WriteAllText(txtPath, "text");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.Executable;

        await vm.ImportContentAsync(tempDir);

        Assert.Equal("Tool.exe", vm.SelectedExecutableItem?.Name);

        var txtItem = FindInTree(vm.FileTree, f => f.Name == "Doc.txt");
        Assert.NotNull(txtItem);
        Assert.False(txtItem!.IsExecutable);

        vm.SelectExecutableCommand.Execute(txtItem);

        // Should still be Tool.exe
        Assert.Equal("Tool.exe", vm.SelectedExecutableItem?.Name);
        Assert.False(txtItem.IsSelectedExecutable);
    }

    /// <summary>
    /// Verifies that CanAdd validation requires an executable for GameClient, ModdingTool, and Executable.
    /// </summary>
    /// <param name="type">The executable content type under test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(ContentType.GameClient)]
    [InlineData(ContentType.ModdingTool)]
    [InlineData(ContentType.Executable)]
    public async Task Validation_CanAdd_RequiresExecutable_ForExecutableTypes(ContentType type)
    {
        var tempDir = CreateTempDirectory();
        var txtPath = Path.Combine(tempDir, "config.ini");
        File.WriteAllText(txtPath, "config");

        var vm = CreateViewModel();
        vm.SelectedContentType = type;
        vm.ContentName = "Test Tool";

        await vm.ImportContentAsync(tempDir);

        // No executable found, so CanAdd should be false
        Assert.Null(vm.SelectedExecutableItem);
        Assert.False(vm.CanAdd);
    }

    /// <summary>
    /// Verifies that CanAdd is true for non-executable types without an executable.
    /// </summary>
    /// <param name="type">The non-executable content type under test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(ContentType.Mod)]
    [InlineData(ContentType.Patch)]
    [InlineData(ContentType.Addon)]
    [InlineData(ContentType.Map)]
    [InlineData(ContentType.MapPack)]
    [InlineData(ContentType.Mission)]
    public async Task Validation_CanAdd_DoesNotRequireExecutable_ForNonExecutableTypes(ContentType type)
    {
        var tempDir = CreateTempDirectory();
        var txtPath = Path.Combine(tempDir, "mod_data.big");
        File.WriteAllText(txtPath, "big archive data");

        var vm = CreateViewModel();
        vm.SelectedContentType = type;
        vm.ContentName = "Test Mod";

        await vm.ImportContentAsync(tempDir);

        Assert.True(vm.CanAdd);
    }

    /// <summary>
    /// Verifies that CanAdd is true when an executable is present for GameClient, ModdingTool, and Executable.
    /// </summary>
    /// <param name="type">The executable content type under test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(ContentType.GameClient)]
    [InlineData(ContentType.ModdingTool)]
    [InlineData(ContentType.Executable)]
    public async Task Validation_CanAdd_IsTrue_WhenExecutableIsPresent(ContentType type)
    {
        var tempDir = CreateTempDirectory();
        var exePath = Path.Combine(tempDir, "Main.exe");
        File.WriteAllText(exePath, "exe content");

        var vm = CreateViewModel();
        vm.SelectedContentType = type;
        vm.ContentName = "Test Item";

        await vm.ImportContentAsync(tempDir);

        Assert.NotNull(vm.SelectedExecutableItem);
        Assert.True(vm.CanAdd);
    }

    /// <summary>
    /// Verifies that AddContentCommand forwards the relative entry point to ILocalContentService.CreateLocalContentManifestAsync.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AddContentCommand_PassesEntryPoint_ToCreateLocalContentManifestAsync()
    {
        var tempDir = CreateTempDirectory();
        var exePath = Path.Combine(tempDir, "Game.exe");
        File.WriteAllText(exePath, "exe");

        string? capturedEntryPoint = null;
        _localContentServiceMock
            .Setup(x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<GameType>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<string, string, ContentType, GameType, string?, IProgress<ContentStorageProgress>?, CancellationToken, string?>(
                (_, _, _, _, _, _, _, entryPoint) => capturedEntryPoint = entryPoint)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.0.local.gameclient.test"),
                Name = "Test Game Client",
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                EntryPoint = "Game.exe",
            }));

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.GameClient;
        vm.ContentName = "Test Game Client";

        // Import individual file so it lands at the root of staging
        await vm.ImportContentAsync(exePath);

        Assert.True(vm.CanAdd);

        await vm.AddContentCommand.ExecuteAsync(null);

        Assert.Equal("Game.exe", capturedEntryPoint);
        Assert.NotNull(vm.CreatedContentItem);
    }

    /// <summary>
    /// Verifies that AddContentCommand with nested executable passes correct relative path as entryPoint.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AddContentCommand_WithNestedExecutable_PassesRelativePathEntryPoint()
    {
        var tempDir = CreateTempDirectory();
        var subDir = Path.Combine(tempDir, "bin");
        Directory.CreateDirectory(subDir);
        var exePath = Path.Combine(subDir, "tool.exe");
        File.WriteAllText(exePath, "tool exe");

        string? capturedEntryPoint = null;
        _localContentServiceMock
            .Setup(x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<GameType>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<string, string, ContentType, GameType, string?, IProgress<ContentStorageProgress>?, CancellationToken, string?>(
                (_, _, _, _, _, _, _, entryPoint) => capturedEntryPoint = entryPoint)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.0.local.moddingtool.tool"),
                Name = "My Tool",
                ContentType = ContentType.ModdingTool,
                TargetGame = GameType.ZeroHour,
            }));

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.ModdingTool;
        vm.ContentName = "My Tool";

        await vm.ImportContentAsync(tempDir);

        Assert.NotNull(vm.SelectedExecutableItem);

        await vm.AddContentCommand.ExecuteAsync(null);

        var dirName = Path.GetFileName(tempDir);
        Assert.Equal($"{dirName}/bin/tool.exe", capturedEntryPoint);
    }

    /// <summary>
    /// Verifies that LoadFromManifestAsync preserves the manifest EntryPoint when reloading for edit.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadFromManifestAsync_PreservesManifestEntryPoint()
    {
        var manifestId = ManifestId.Create("1.0.local.gameclient.zh");

        var manifest = new ContentManifest
        {
            Id = manifestId,
            Name = "ZH Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            EntryPoint = "special.exe",
            Files =
            [
                new ManifestFile { RelativePath = "special.exe", IsExecutable = true },
                new ManifestFile { RelativePath = "bin/decoy.exe", IsExecutable = true },
            ],
        };

        _contentStorageServiceMock
            .Setup(x => x.RetrieveContentAsync(
                It.IsAny<ManifestId>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<ManifestId, string, CancellationToken>((_, targetPath, _) =>
            {
                Directory.CreateDirectory(targetPath);
                File.WriteAllText(Path.Combine(targetPath, "special.exe"), "exe");
                var targetSub = Path.Combine(targetPath, "bin");
                Directory.CreateDirectory(targetSub);
                File.WriteAllText(Path.Combine(targetSub, "decoy.exe"), "decoy");
            })
            .ReturnsAsync((ManifestId _, string targetPath, CancellationToken _) => OperationResult<string>.CreateSuccess(targetPath));

        string? capturedEntryPoint = null;
        _localContentServiceMock
            .Setup(x => x.UpdateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<GameType>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<string, string, string, ContentType, GameType, string?, IProgress<ContentStorageProgress>?, CancellationToken, string?>(
                (_, _, _, _, _, _, _, _, entryPoint) => capturedEntryPoint = entryPoint)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var item = new GenHub.Features.GameProfiles.ViewModels.ContentDisplayItem
        {
            Id = manifestId.Value,
            ManifestId = manifestId,
            DisplayName = "ZH Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            Manifest = manifest,
        };

        var vm = CreateViewModel();
        await vm.LoadFromManifestAsync(item);

        Assert.NotNull(vm.SelectedExecutableItem);
        Assert.Equal("special.exe", vm.SelectedExecutableItem.Name);

        await vm.AddContentCommand.ExecuteAsync(null);
        Assert.Equal("special.exe", capturedEntryPoint);
    }

    /// <summary>
    /// Verifies that deleting an unrelated item preserves the previously selected executable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteItemAsync_PreservesSelectedExecutable()
    {
        var tempDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(tempDir, "first.exe"), "first");
        File.WriteAllText(Path.Combine(tempDir, "second.exe"), "second");
        File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "readme");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.GameClient;
        vm.ContentName = "Test Client";
        await vm.ImportContentAsync(tempDir);

        var secondExe = FindInTree(vm.FileTree, f => f.Name == "second.exe");
        Assert.NotNull(secondExe);
        vm.SelectExecutableCommand.Execute(secondExe);
        Assert.Equal("second.exe", vm.SelectedExecutableItem?.Name);

        var readme = FindInTree(vm.FileTree, f => f.Name == "readme.txt");
        Assert.NotNull(readme);
        await vm.DeleteItemCommand.ExecuteAsync(readme);

        Assert.NotNull(vm.SelectedExecutableItem);
        Assert.Equal("second.exe", vm.SelectedExecutableItem.Name);
    }

    /// <summary>
    /// Verifies that deleting the currently selected executable falls back to auto-selecting the remaining executable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteItemAsync_WhenSelectedExecutableDeleted_FallsBackToRemainingExecutable()
    {
        var tempDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(tempDir, "first.exe"), "first");
        File.WriteAllText(Path.Combine(tempDir, "second.exe"), "second");
        File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "readme");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.GameClient;
        vm.ContentName = "Test Client";
        await vm.ImportContentAsync(tempDir);

        var secondExe = FindInTree(vm.FileTree, f => f.Name == "second.exe");
        Assert.NotNull(secondExe);
        vm.SelectExecutableCommand.Execute(secondExe);
        Assert.Equal("second.exe", vm.SelectedExecutableItem?.Name);

        await vm.DeleteItemCommand.ExecuteAsync(secondExe);

        Assert.NotNull(vm.SelectedExecutableItem);
        Assert.Equal("first.exe", vm.SelectedExecutableItem.Name);
    }

    /// <summary>
    /// Verifies that switching content type away from executable and back preserves the selected entry point.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ContentTypeChanged_SwitchAwayAndBack_PreservesSelectedExecutable()
    {
        var tempDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(tempDir, "first.exe"), "first");
        File.WriteAllText(Path.Combine(tempDir, "second.exe"), "second");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.GameClient;
        vm.ContentName = "Test Client";
        await vm.ImportContentAsync(tempDir);

        var secondExe = FindInTree(vm.FileTree, f => f.Name == "second.exe");
        Assert.NotNull(secondExe);
        vm.SelectExecutableCommand.Execute(secondExe);
        Assert.Equal("second.exe", vm.SelectedExecutableItem?.Name);

        // Switch to Mod (non-executable type)
        vm.SelectedContentType = ContentType.Mod;
        Assert.Null(vm.SelectedExecutableItem);

        // Switch back to GameClient (executable type)
        vm.SelectedContentType = ContentType.GameClient;
        Assert.NotNull(vm.SelectedExecutableItem);
        Assert.Equal("second.exe", vm.SelectedExecutableItem.Name);
    }

    /// <summary>
    /// Verifies that BuildDirectoryTree prioritizes directories containing executables over non-executable directories.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildDirectoryTree_PrioritizesDirectoriesWithExecutables()
    {
        var tempDir = CreateTempDirectory();

        // Create 25 directories named folder01 to folder25
        for (var i = 1; i <= 25; i++)
        {
            var folder = Path.Combine(tempDir, $"folder{i:D2}");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "data.txt"), "content");
        }

        // Put an executable only in the 25th folder
        var targetFolder = Path.Combine(tempDir, "folder25");
        File.WriteAllText(Path.Combine(targetFolder, "game.exe"), "executable");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.GameClient;
        vm.ContentName = "Test Client";
        await vm.ImportContentAsync(tempDir);

        var folder25 = FindInTree(vm.FileTree, f => f.Name == "folder25");
        Assert.NotNull(folder25);

        var exe = FindInTree(folder25.Children, f => f.Name == "game.exe");
        Assert.NotNull(exe);
        Assert.True(exe.IsExecutable);
    }

    /// <summary>
    /// Verifies that switching from an executable type to a non-executable type clears the selected executable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ContentTypeChanged_FromExecutableToNonExecutable_ClearsSelectedExecutable()
    {
        var tempDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(tempDir, "game.exe"), "game");

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.GameClient;
        vm.ContentName = "Test Client";
        await vm.ImportContentAsync(tempDir);

        Assert.NotNull(vm.SelectedExecutableItem);

        vm.SelectedContentType = ContentType.Mod;

        Assert.Null(vm.SelectedExecutableItem);
    }

    /// <summary>
    /// Verifies that AddContentCommand with non-executable content type passes null as entryPoint.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AddContentCommand_WhenNonExecutableType_PassesNullEntryPoint()
    {
        var tempDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(tempDir, "somefile.txt"), "text");

        string? capturedEntryPoint = "INITIAL";
        _localContentServiceMock
            .Setup(x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<GameType>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<string, string, ContentType, GameType, string?, IProgress<ContentStorageProgress>?, CancellationToken, string?>(
                (_, _, _, _, _, _, _, entryPoint) => capturedEntryPoint = entryPoint)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.0.local.mod.test"),
                Name = "My Mod",
                ContentType = ContentType.Mod,
                TargetGame = GameType.ZeroHour,
            }));

        var vm = CreateViewModel();
        vm.SelectedContentType = ContentType.Mod;
        vm.ContentName = "My Mod";
        await vm.ImportContentAsync(tempDir);

        await vm.AddContentCommand.ExecuteAsync(null);

        Assert.Null(capturedEntryPoint);
    }

    private static FileTreeItem? FindInTree(IEnumerable<FileTreeItem> items, Func<FileTreeItem, bool> predicate)
    {
        foreach (var item in items)
        {
            if (predicate(item)) return item;
            var child = FindInTree(item.Children, predicate);
            if (child != null) return child;
        }

        return null;
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AddLocalContentVmTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private AddLocalContentViewModel CreateViewModel()
    {
        var vm = new AddLocalContentViewModel(
            _localContentServiceMock.Object,
            _contentStorageServiceMock.Object,
            _normalizationServiceMock.Object,
            _dialogServiceMock.Object,
            NullLogger<AddLocalContentViewModel>.Instance);
        _viewModels.Add(vm);
        return vm;
    }
}
