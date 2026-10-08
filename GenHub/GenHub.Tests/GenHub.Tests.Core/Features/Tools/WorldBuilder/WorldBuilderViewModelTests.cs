using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Messages;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Models.Validation;
using GenHub.Core.Services.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Controls;
using GenHub.Features.Tools.WorldBuilder.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder.ViewModels;

/// <summary>
/// Unit tests for <see cref="WorldBuilderViewModel"/>.
/// </summary>
public sealed class WorldBuilderViewModelTests : IDisposable
{
    private readonly Mock<IWorldBuilderMapService> _mockMapService;
    private readonly Mock<IMapValidationService> _mockValidationService;
    private readonly Mock<IMapGenerationService> _mockGenerationService;
    private readonly Mock<IMapPreviewService> _mockPreviewService;
    private readonly Mock<ITeamExchangeService> _mockTeamService;
    private readonly Mock<IWorldBuilderProjectService> _mockProjectService;
    private readonly Mock<IWorldBuilderSidecarService> _mockSidecarService;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ILocalizationService> _mockLocalizationService;
    private readonly Mock<IDialogService> _mockDialogService;
    private readonly Mock<IWorldBuilderContentService> _mockContentService;
    private readonly WorldBuilderViewModel _viewModel;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldBuilderViewModelTests"/> class.
    /// </summary>
    public WorldBuilderViewModelTests()
    {
        _mockMapService = new Mock<IWorldBuilderMapService>();
        _mockValidationService = new Mock<IMapValidationService>();
        _mockGenerationService = new Mock<IMapGenerationService>();
        _mockPreviewService = new Mock<IMapPreviewService>();
        _mockTeamService = new Mock<ITeamExchangeService>();
        _mockProjectService = new Mock<IWorldBuilderProjectService>();
        _mockSidecarService = new Mock<IWorldBuilderSidecarService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockLocalizationService = new Mock<ILocalizationService>();
        _mockDialogService = new Mock<IDialogService>();
        _mockLocalizationService
            .Setup(s => s.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => args?.Length > 0 ? $"{key}:{string.Join(',', args)}" : key);
        _mockDialogService
            .Setup(s => s.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        _mockPreviewService
            .Setup(s => s.ReadTgaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<MapPreviewData>.CreateFailure("No preview."));
        _mockContentService = new Mock<IWorldBuilderContentService>();
        _mockContentService
            .Setup(s => s.EnsureContentLoadedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        _viewModel = new WorldBuilderViewModel(
            _mockMapService.Object,
            _mockValidationService.Object,
            _mockGenerationService.Object,
            _mockPreviewService.Object,
            _mockTeamService.Object,
            _mockProjectService.Object,
            _mockSidecarService.Object,
            _mockNotificationService.Object,
            _mockLocalizationService.Object,
            _mockDialogService.Object,
            _mockContentService.Object,
            Mock.Of<ILogger<WorldBuilderViewModel>>());
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHubWbVmTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _viewModel.Dispose();

        // Join fire-and-forget render passes so no background work touches the
        // headless dispatcher after this test ends (see TestAppBuilder).
        _viewModel.RenderIdleAsync().Wait(TimeSpan.FromSeconds(30));
        try
        {
            Directory.Delete(_tempDirectory, true);
        }
        catch (IOException)
        {
            // Best effort cleanup of temp files.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of temp files.
        }
    }

    /// <summary>
    /// Tests that opening a map that fails to load reports an error.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task OpenMap_LoadFailure_ReturnsFalseAsync()
    {
        // Arrange
        _mockMapService
            .Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorldBuilderMap>.CreateFailure("Corrupt."));

        // Act
        var opened = await _viewModel.OpenMapAsync(Path.Combine(_tempDirectory, "missing.map"));

        // Assert
        opened.Should().BeFalse();
        _viewModel.HasDocument.Should().BeFalse();
        _mockNotificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that opening a map adopts the document state.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task OpenMap_Success_AdoptsDocumentAsync()
    {
        // Arrange
        var mapPath = Path.Combine(_tempDirectory, "adopt.map");
        var map = CreateMap("Adopted Map");
        _mockMapService
            .Setup(s => s.LoadAsync(mapPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorldBuilderMap>.CreateSuccess(map));
        _mockMapService
            .Setup(s => s.Summarize(It.IsAny<WorldBuilderMap>()))
            .Returns(new MapSummaryReport { MapName = "Adopted Map" });

        // Act
        var opened = await _viewModel.OpenMapAsync(mapPath);

        // Assert
        opened.Should().BeTrue();
        _viewModel.HasDocument.Should().BeTrue();
        _viewModel.FilePath.Should().Be(mapPath);
        _viewModel.MapName.Should().Be("Adopted Map");
        _viewModel.Summary.Should().NotBeNull();
        _viewModel.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// Tests that declining the discard prompt keeps the open document.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task OpenMap_WhenDirtyAndCancelled_KeepsDocumentAsync()
    {
        // Arrange
        var mapPath = Path.Combine(_tempDirectory, "first.map");
        _mockMapService
            .Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorldBuilderMap>.CreateSuccess(CreateMap("First")));
        await _viewModel.OpenMapAsync(mapPath);
        _viewModel.MapName = "Changed";
        _mockDialogService
            .Setup(s => s.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(false);

        // Act
        var opened = await _viewModel.OpenMapAsync(Path.Combine(_tempDirectory, "second.map"));

        // Assert
        opened.Should().BeFalse();
        _viewModel.FilePath.Should().Be(mapPath);
        _viewModel.MapName.Should().Be("Changed");
    }

    /// <summary>
    /// Tests that generating a map adopts an unsaved document.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GenerateNewMap_Success_AdoptsUnsavedDocumentAsync()
    {
        // Arrange
        MapGenSettings? captured = null;
        _mockGenerationService
            .Setup(s => s.Generate(It.IsAny<MapGenSettings>(), It.IsAny<CancellationToken>()))
            .Callback<MapGenSettings, CancellationToken>((settings, _) => captured = settings)
            .Returns(OperationResult<WorldBuilderMap>.CreateSuccess(CreateMap("Generated")));
        _mockPreviewService
            .Setup(s => s.BuildPreview(It.IsAny<WorldBuilderMap>()))
            .Returns(new MapPreviewData { Width = 4, Height = 4, Pixels = new int[16] });
        _viewModel.Seed = 7;
        _viewModel.MapWidth = 64;
        _viewModel.MapHeight = 64;
        _viewModel.PlayerCount = 2;

        // Act
        await _viewModel.GenerateNewMapCommand.ExecuteAsync(null);

        // Assert
        captured.Should().NotBeNull();
        captured!.Seed.Should().Be(7);
        captured.PlayableWidth.Should().Be(64);
        _viewModel.HasDocument.Should().BeTrue();
        _viewModel.FilePath.Should().BeNull();
        _viewModel.IsDirty.Should().BeTrue();
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that a failed generation reports an error and opens nothing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GenerateNewMap_Failure_ShowsErrorAsync()
    {
        // Arrange
        _mockGenerationService
            .Setup(s => s.Generate(It.IsAny<MapGenSettings>(), It.IsAny<CancellationToken>()))
            .Returns(OperationResult<WorldBuilderMap>.CreateFailure("No memory."));

        // Act
        await _viewModel.GenerateNewMapCommand.ExecuteAsync(null);

        // Assert
        _viewModel.HasDocument.Should().BeFalse();
        _mockNotificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that generating over an open map leaves a single undo step.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GenerateNewMap_SecondGenerate_LeavesOneUndoAsync()
    {
        // Arrange
        _mockGenerationService
            .SetupSequence(s => s.Generate(It.IsAny<MapGenSettings>(), It.IsAny<CancellationToken>()))
            .Returns(OperationResult<WorldBuilderMap>.CreateSuccess(CreateMap("First")))
            .Returns(OperationResult<WorldBuilderMap>.CreateSuccess(CreateMap("Second")));
        _mockPreviewService
            .Setup(s => s.BuildPreview(It.IsAny<WorldBuilderMap>()))
            .Returns(new MapPreviewData { Width = 4, Height = 4, Pixels = new int[16] });

        // Act
        await _viewModel.GenerateNewMapCommand.ExecuteAsync(null);

        // Assert
        _viewModel.CanUndo.Should().BeFalse();

        // Act
        await _viewModel.GenerateNewMapCommand.ExecuteAsync(null);

        // Assert
        _viewModel.CanUndo.Should().BeTrue();
        _viewModel.UndoCommand.Execute(null);
        _viewModel.CanUndo.Should().BeFalse();
        _viewModel.CanRedo.Should().BeTrue();
    }

    /// <summary>
    /// Tests that script search matches condition parameter text.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ScriptSearch_FindsConditionParameterTextAsync()
    {
        // Arrange
        await OpenScriptMapAsync("search.map", CreateScriptMap());

        // Act
        _viewModel.ScriptSearchText = "SpecialTank";

        // Assert
        _viewModel.GroupScripts.Select(s => s.Name).Should().ContainSingle().Which.Should().Be("Beta");
    }

    /// <summary>
    /// Tests that difficulty chips filter scripts by their difficulty flags.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ScriptDifficultyChips_FilterByFlagsAsync()
    {
        // Arrange
        await OpenScriptMapAsync("chips.map", CreateScriptMap());

        // Act
        _viewModel.ScriptFilterEasy = false;

        // Assert
        _viewModel.GroupScripts.Select(s => s.Name).Should().NotContain("Alpha");
    }

    /// <summary>
    /// Tests that the warnings chip shows only scripts with broken references.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ScriptWarningsChip_ShowsOnlyBrokenAsync()
    {
        // Arrange
        await OpenScriptMapAsync("warnings.map", CreateScriptMap());

        // Act
        _viewModel.ScriptFilterWarnings = true;

        // Assert
        _viewModel.GroupScripts.Select(s => s.Name).Should().ContainSingle().Which.Should().Be("Beta");
    }

    /// <summary>
    /// Tests that renaming a script updates the selection and undoes cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RenameScript_RenamesSelectionAsync()
    {
        // Arrange
        await OpenScriptMapAsync("rename.map", CreateScriptMap());
        _viewModel.SelectedScript.Should().NotBeNull();

        // Act
        _viewModel.RenameScriptCommand.Execute("Renamed");

        // Assert
        _viewModel.SelectedScript!.Name.Should().Be("Renamed");
        _viewModel.UndoCommand.Execute(null);
        _viewModel.GroupScripts.Select(s => s.Name).Should().Contain("Alpha");
    }

    /// <summary>
    /// Tests that clear-all empties every side with a single undo step.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ClearAllScripts_Confirm_ClearsWithSingleUndoAsync()
    {
        // Arrange
        await OpenScriptMapAsync("clear.map", CreateScriptMap());

        // Act
        await _viewModel.ClearAllScriptsCommand.ExecuteAsync(null);

        // Assert
        _viewModel.GroupScripts.Should().BeEmpty();
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _viewModel.UndoCommand.Execute(null);
        _viewModel.GroupScripts.Select(s => s.Name).Should().BeEquivalentTo("Alpha", "Beta");
    }

    /// <summary>
    /// Tests that replace-all rewrites a parameter value across scripts.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReplaceAllScriptValues_ReplacesAcrossScriptsAsync()
    {
        // Arrange
        await OpenScriptMapAsync("replace.map", CreateScriptMap());
        _viewModel.ScriptFindText = "OldDepot";
        _viewModel.ScriptReplaceText = "NewDepot";

        // Act
        _viewModel.ReplaceAllScriptValuesCommand.Execute(null);

        // Assert
        AllScriptParamValues().Should().NotContain("OldDepot");
        AllScriptParamValues().Count(v => v == "NewDepot").Should().Be(2);
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _viewModel.UndoCommand.Execute(null);
        AllScriptParamValues().Count(v => v == "OldDepot").Should().Be(2);
    }

    /// <summary>
    /// Tests that script export and import round trip with a single undo step.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExportImportScripts_RoundTripAsync()
    {
        // Arrange
        await OpenScriptMapAsync("scripts.map", CreateScriptMap());
        var path = Path.Combine(Path.GetTempPath(), "GenHubWbVmTests", Guid.NewGuid().ToString("N") + ".scb");

        try
        {
            // Act
            var exported = await _viewModel.ExportScriptsToFileAsync(path);

            // Assert
            exported.Success.Should().BeTrue();
            exported.Data.Should().Be(2);
            File.Exists(path).Should().BeTrue();
            await _viewModel.ClearAllScriptsCommand.ExecuteAsync(null);
            _viewModel.GroupScripts.Should().BeEmpty();

            // Act
            var imported = await _viewModel.ImportScriptsFromFileAsync(path);

            // Assert
            imported.Success.Should().BeTrue();
            imported.Data.Should().Be(2);
            _viewModel.GroupScripts.Select(s => s.Name).Should().BeEquivalentTo("Alpha", "Beta");
            _viewModel.UndoCommand.Execute(null);
            _viewModel.GroupScripts.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// Tests that importing garbage bytes fails with an error toast.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportScriptsFromFile_InvalidBytes_FailsAsync()
    {
        // Arrange
        await OpenScriptMapAsync("badscripts.map", CreateScriptMap());
        var path = Path.Combine(Path.GetTempPath(), "GenHubWbVmTests", Guid.NewGuid().ToString("N") + ".scb");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);

            // Act
            var result = await _viewModel.ImportScriptsFromFileAsync(path);

            // Assert
            result.Success.Should().BeFalse();
            _mockNotificationService.Verify(
                n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
            _viewModel.GroupScripts.Should().HaveCount(2);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// Tests that fix team owner reassigns teams with missing owners.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FixTeamOwners_ReassignsBrokenTeamsAsync()
    {
        // Arrange
        await OpenScriptMapAsync("teams.map", CreateTeamMap());
        _viewModel.TeamOwnerFixPlayer = "PlyrCivilian";

        // Act
        _viewModel.FixTeamOwnersCommand.Execute(null);

        // Assert
        _viewModel.SkirmishTeams.Should().ContainSingle();
        _viewModel.SkirmishTeams[0].Properties
            .GetString(WorldBuilderConstants.DictKeys.TeamOwner, string.Empty).Should().Be("PlyrCivilian");
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that fix team owner rejects an unknown player.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FixTeamOwners_InvalidPlayer_ShowsErrorAsync()
    {
        // Arrange
        await OpenScriptMapAsync("badteam.map", CreateTeamMap());
        _viewModel.TeamOwnerFixPlayer = "Nobody";

        // Act
        _viewModel.FixTeamOwnersCommand.Execute(null);

        // Assert
        _viewModel.SkirmishTeams[0].Properties
            .GetString(WorldBuilderConstants.DictKeys.TeamOwner, string.Empty).Should().Be("GhostPlayer");
        _mockNotificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that replace-missing swaps a broken reference for its closest match.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReplaceAllMissingReferences_ReplacesWithClosestMatchAsync()
    {
        // Arrange
        var map = CreateScriptMap();
        map.Objects.Add(new MapObjectEntry { Name = "SupplyDock", X = 20, Y = 20 });
        map.Scripts[0].Scripts[1].OrConditions[0].Conditions[0].Parameters[0].StringValue = "SupplyDok";
        await OpenScriptMapAsync("missing.map", map);

        // Act
        _viewModel.ReplaceAllMissingReferencesCommand.Execute(null);

        // Assert
        AllScriptParamValues().Should().Contain("SupplyDock");
        AllScriptParamValues().Should().NotContain("SupplyDok");
        _viewModel.BrokenReferenceReport.Should().ContainSingle();
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _viewModel.UndoCommand.Execute(null);
        AllScriptParamValues().Should().Contain("SupplyDok");
    }

    /// <summary>
    /// Tests that replace-missing reports when every reference resolves.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReplaceAllMissingReferences_NoMissing_ShowsInfoAsync()
    {
        // Arrange
        var map = CreateScriptMap();
        map.Scripts[0].Scripts[1].OrConditions[0].Conditions[0].Parameters[0].StringValue = "OldDepot";
        await OpenScriptMapAsync("clean.map", map);

        // Act
        _viewModel.ReplaceAllMissingReferencesCommand.Execute(null);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that adding a side writes the full side record with undo.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AddSide_WritesFullPropsAndUndoesAsync()
    {
        // Arrange
        await OpenScriptMapAsync("side.map", CreateScriptMap());
        _viewModel.NewSideName = "PlyrUSA";

        // Act
        _viewModel.AddSideCommand.Execute(null);

        // Assert
        _viewModel.PlayerSides.Should().HaveCount(2);
        var added = _viewModel.PlayerSides[1];
        added.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName).Should().Be("PlyrUSA");
        added.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerDisplayName).Should().Be("PlyrUSA");
        added.Properties.GetInt(WorldBuilderConstants.DictKeys.PlayerIsHuman).Should().Be(0);
        _viewModel.UndoCommand.Execute(null);
        _viewModel.PlayerSides.Should().ContainSingle();
    }

    /// <summary>
    /// Tests that applying team properties renames and reassigns the owner.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ApplyTeamProperties_RenamesAndReownersAsync()
    {
        // Arrange
        await OpenScriptMapAsync("teamprops.map", CreateTeamMap());
        _viewModel.SelectedSkirmishTeam = _viewModel.SkirmishTeams[0];
        _viewModel.SelectedTeamName.Should().Be("TeamAlpha");
        _viewModel.SelectedTeamName = "TeamBeta";
        _viewModel.SelectedTeamOwner = "PlyrCivilian";

        // Act
        _viewModel.ApplyTeamPropertiesCommand.Execute(null);

        // Assert
        _viewModel.SkirmishTeams[0].Properties
            .GetString(WorldBuilderConstants.DictKeys.TeamName).Should().Be("TeamBeta");
        _viewModel.SkirmishTeams[0].Properties
            .GetString(WorldBuilderConstants.DictKeys.TeamOwner).Should().Be("PlyrCivilian");
        _viewModel.UndoCommand.Execute(null);
        _viewModel.SkirmishTeams[0].Properties
            .GetString(WorldBuilderConstants.DictKeys.TeamName).Should().Be("TeamAlpha");
        _viewModel.SkirmishTeams[0].Properties
            .GetString(WorldBuilderConstants.DictKeys.TeamOwner).Should().Be("GhostPlayer");
    }

    /// <summary>
    /// Tests that applying side properties edits display name, human flag, and faction.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ApplySideProperties_EditsAndUndoesAsync()
    {
        // Arrange
        await OpenScriptMapAsync("sideprops.map", CreateScriptMap());
        _viewModel.SelectedPlayerSide = _viewModel.PlayerSides[0];
        _viewModel.SelectedSideDisplayName = "Civilians";
        _viewModel.SelectedSideIsHuman = true;
        _viewModel.SelectedSideFaction = "FactionAmerica";

        // Act
        _viewModel.ApplySidePropertiesCommand.Execute(null);

        // Assert
        var side = _viewModel.PlayerSides[0];
        side.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerDisplayName).Should().Be("Civilians");
        side.Properties.GetInt(WorldBuilderConstants.DictKeys.PlayerIsHuman).Should().Be(1);
        side.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerFaction).Should().Be("FactionAmerica");
        _viewModel.UndoCommand.Execute(null);
        _viewModel.PlayerSides[0].Properties
            .GetInt(WorldBuilderConstants.DictKeys.PlayerIsHuman).Should().Be(0);
    }

    /// <summary>
    /// Tests that applying object properties writes every property group.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ApplyObjectProperties_WritesAllFieldsAsync()
    {
        // Arrange
        await OpenScriptMapAsync("props.map", CreateScriptMap());
        _viewModel.SelectedObject = _viewModel.Objects[0];
        _viewModel.SelectedObjectHitPoints = 500;
        _viewModel.SelectedObjectUnsellable = true;
        _viewModel.SelectedObjectTargetable = false;
        _viewModel.SelectedObjectRecruitableAI = false;
        _viewModel.SelectedObjectPowered = false;
        _viewModel.SelectedObjectStoppingDistance = 12.5f;
        _viewModel.SelectedObjectVisionDistance = 200f;
        _viewModel.SelectedObjectShroudClearingDistance = 40f;
        _viewModel.SelectedObjectWeather = "Snow";
        _viewModel.SelectedObjectTime = "Night";
        _viewModel.SelectedObjectScale = 2f;
        _viewModel.SelectedObjectScaleEnabled = true;
        _viewModel.SelectedObjectSound = "SndTest";
        _viewModel.SelectedObjectSoundCustomize = true;
        _viewModel.SelectedObjectSoundLooping = true;
        _viewModel.SelectedObjectSoundLoopCount = 3;
        _viewModel.SelectedObjectSoundPriority = "High";
        _viewModel.SelectedObjectSoundVolume = 0.8f;
        _viewModel.SelectedObjectSoundMinVolume = 0.1f;
        _viewModel.SelectedObjectSoundMinRange = 5f;
        _viewModel.SelectedObjectSoundMaxRange = 60f;
        _viewModel.SelectedObjectUpgrades = "UpgA;UpgB";
        _viewModel.SelectedObjectReflectsInMirror = true;

        // Act
        _viewModel.ApplyObjectPropertiesCommand.Execute(null);

        // Assert
        var props = _viewModel.SelectedObject!.Properties;
        props.GetInt(WorldBuilderConstants.DictKeys.ObjectHitPoints).Should().Be(500);
        props.GetBool(WorldBuilderConstants.DictKeys.ObjectUnsellable).Should().BeTrue();
        props.GetBool(WorldBuilderConstants.DictKeys.ObjectTargetable).Should().BeFalse();
        props.GetBool(WorldBuilderConstants.DictKeys.ObjectRecruitableAI).Should().BeFalse();
        props.GetBool(WorldBuilderConstants.DictKeys.ObjectPowered).Should().BeFalse();
        props.GetReal(WorldBuilderConstants.DictKeys.ObjectStoppingDistance).Should().Be(12.5f);
        props.GetReal(WorldBuilderConstants.DictKeys.ObjectVisionDistance).Should().Be(200f);
        props.GetReal(WorldBuilderConstants.DictKeys.ObjectShroudClearingDistance).Should().Be(40f);
        props.GetString(WorldBuilderConstants.DictKeys.Weather).Should().Be("Snow");
        props.GetString(WorldBuilderConstants.DictKeys.ObjectTime).Should().Be("Night");
        props.GetReal(WorldBuilderConstants.DictKeys.ObjectScale).Should().Be(2f);
        props.GetBool(WorldBuilderConstants.DictKeys.ObjectScaleEnabled).Should().BeTrue();
        props.GetString(WorldBuilderConstants.DictKeys.ObjectSound).Should().Be("SndTest");
        props.GetBool(WorldBuilderConstants.DictKeys.ObjectSoundCustomize).Should().BeTrue();
        props.GetBool(WorldBuilderConstants.DictKeys.ObjectSoundLooping).Should().BeTrue();
        props.GetInt(WorldBuilderConstants.DictKeys.ObjectSoundLoopCount).Should().Be(3);
        props.GetString(WorldBuilderConstants.DictKeys.ObjectSoundPriority).Should().Be("High");
        props.GetReal(WorldBuilderConstants.DictKeys.ObjectSoundVolume).Should().Be(0.8f);
        props.GetReal(WorldBuilderConstants.DictKeys.ObjectSoundMinVolume).Should().Be(0.1f);
        props.GetReal(WorldBuilderConstants.DictKeys.ObjectSoundMinRange).Should().Be(5f);
        props.GetReal(WorldBuilderConstants.DictKeys.ObjectSoundMaxRange).Should().Be(60f);
        props.GetString(WorldBuilderConstants.DictKeys.ObjectUpgrades).Should().Be("UpgA;UpgB");
        props.GetBool(WorldBuilderConstants.DictKeys.ObjectReflectsInMirror).Should().BeTrue();

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        _viewModel.SelectedObject = _viewModel.Objects[0];
        _viewModel.SelectedObjectHitPoints.Should().Be(0);
        _viewModel.SelectedObjectSound.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that the object angle editor converts between UI degrees and
    /// stored radians in both directions.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ApplyObjectProperties_Angle_ConvertsDegreesAndRadiansAsync()
    {
        // Arrange
        await OpenScriptMapAsync("angle.map", CreateScriptMap());
        _viewModel.Objects[0].Angle = MathF.PI;

        // Act: selecting shows degrees.
        _viewModel.SelectedObject = _viewModel.Objects[0];

        // Assert
        _viewModel.SelectedObjectAngle.Should().BeApproximately(180.0f, 0.01f);

        // Act: applying converts back to radians.
        _viewModel.SelectedObjectAngle = 90.0f;
        _viewModel.ApplyObjectPropertiesCommand.Execute(null);

        // Assert
        _viewModel.SelectedObject!.Angle.Should().BeApproximately(MathF.PI / 2.0f, 0.0001f);
    }

    /// <summary>
    /// Tests that selecting an object loads the extended properties.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectObject_PopulatesExtendedFieldsAsync()
    {
        // Arrange
        var map = CreateScriptMap();
        var entry = map.Objects[0];
        entry.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectHitPoints, WorldBuilderConstants.DictValueType.Int, IntValue: 750));
        entry.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectUnsellable, WorldBuilderConstants.DictValueType.Bool, IntValue: 1));
        entry.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectVisionDistance, WorldBuilderConstants.DictValueType.Real, RealValue: 150f));
        entry.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.Weather, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "Snow"));
        entry.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectSound, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "SndHorn"));
        entry.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectReflectsInMirror, WorldBuilderConstants.DictValueType.Bool, IntValue: 1));
        await OpenScriptMapAsync("select.map", map);

        // Act
        _viewModel.SelectedObject = _viewModel.Objects[0];

        // Assert
        _viewModel.SelectedObjectHitPoints.Should().Be(750);
        _viewModel.SelectedObjectUnsellable.Should().BeTrue();
        _viewModel.SelectedObjectVisionDistance.Should().Be(150f);
        _viewModel.SelectedObjectWeather.Should().Be("Snow");
        _viewModel.SelectedObjectSound.Should().Be("SndHorn");
        _viewModel.SelectedObjectReflectsInMirror.Should().BeTrue();
    }

    /// <summary>
    /// Tests that validating a clean map reports success.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateMap_Valid_ShowsSuccessAsync()
    {
        // Arrange
        await OpenMapAsync("valid.map");
        _mockValidationService
            .Setup(s => s.Validate(It.IsAny<WorldBuilderMap>()))
            .Returns(new ValidationResult("valid.map", null));

        // Act
        await _viewModel.ValidateMapCommand.ExecuteAsync(null);

        // Assert
        _viewModel.ValidationIssues.Should().BeEmpty();
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that validating a broken map surfaces issues with a warning.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateMap_WithIssues_ShowsWarningAsync()
    {
        // Arrange
        await OpenMapAsync("broken.map");
        _mockValidationService
            .Setup(s => s.Validate(It.IsAny<WorldBuilderMap>()))
            .Returns(new ValidationResult("broken.map", [new ValidationIssue("Missing side.", ValidationSeverity.Error)]));

        // Act
        await _viewModel.ValidateMapCommand.ExecuteAsync(null);

        // Assert
        _viewModel.ValidationIssues.Should().ContainSingle();
        _mockNotificationService.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that saving a dirty map clears the dirty flag.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveMap_Success_ClearsDirtyAsync()
    {
        // Arrange
        var mapPath = await OpenMapAsync("save.map");
        _viewModel.MapName = "Renamed";
        _mockMapService
            .Setup(s => s.SaveAsync(It.IsAny<WorldBuilderMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        await _viewModel.SaveMapCommand.ExecuteAsync(null);

        // Assert
        _viewModel.IsDirty.Should().BeFalse();
        _viewModel.FilePath.Should().Be(mapPath);
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that a failed save keeps the dirty flag and reports an error.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveMap_Failure_ShowsErrorAsync()
    {
        // Arrange
        await OpenMapAsync("nosave.map");
        _viewModel.MapName = "Renamed";
        _mockMapService
            .Setup(s => s.SaveAsync(It.IsAny<WorldBuilderMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Locked."));

        // Act
        await _viewModel.SaveMapCommand.ExecuteAsync(null);

        // Assert
        _viewModel.IsDirty.Should().BeTrue();
        _mockNotificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that renaming the map updates the world dictionary.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MapName_Change_UpdatesWorldDictAsync()
    {
        // Arrange
        var map = CreateMap("Before");
        _mockMapService
            .Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorldBuilderMap>.CreateSuccess(map));
        await _viewModel.OpenMapAsync(Path.Combine(_tempDirectory, "rename.map"));

        // Act
        _viewModel.MapName = "Renamed";

        // Assert
        _viewModel.IsDirty.Should().BeTrue();
        map.World.GetString(WorldBuilderConstants.DictKeys.MapName, string.Empty).Should().Be("Renamed");
    }

    /// <summary>
    /// Tests that deleting a road checkpoints first, so the delete is undoable
    /// and the dirty flag round-trips through undo.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteRoadSegment_CheckpointsAndUndoesAsync()
    {
        // Arrange
        await OpenScriptMapAsync("roads.map", CreateRoadMap());
        _viewModel.RoadSegments.Should().ContainSingle();
        _viewModel.IsDirty.Should().BeFalse();

        // Act
        _viewModel.SelectedRoadSegment = _viewModel.RoadSegments[0];
        _viewModel.DeleteRoadSegmentCommand.Execute(null);

        // Assert
        _viewModel.RoadSegments.Should().BeEmpty();
        _viewModel.IsDirty.Should().BeTrue();
        _viewModel.CanUndo.Should().BeTrue();

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        _viewModel.RoadSegments.Should().ContainSingle();
        _viewModel.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// Tests that deleting a bridge checkpoints first, so the delete is undoable
    /// and the dirty flag round-trips through undo.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteBridge_CheckpointsAndUndoesAsync()
    {
        // Arrange
        await OpenScriptMapAsync("bridges.map", CreateRoadMap());
        _viewModel.BridgeSegments.Should().ContainSingle();
        _viewModel.IsDirty.Should().BeFalse();

        // Act
        _viewModel.SelectedBridgeSegment = _viewModel.BridgeSegments[0];
        _viewModel.DeleteBridgeCommand.Execute(null);

        // Assert
        _viewModel.BridgeSegments.Should().BeEmpty();
        _viewModel.IsDirty.Should().BeTrue();
        _viewModel.CanUndo.Should().BeTrue();

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        _viewModel.BridgeSegments.Should().ContainSingle();
        _viewModel.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// Tests that applying lighting checkpoints first, so the change is undoable
    /// and the dirty flag round-trips through undo.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ApplyLighting_CheckpointsAndUndoesAsync()
    {
        // Arrange
        await OpenScriptMapAsync("lighting.map", CreateRoadMap());
        _viewModel.IsDirty.Should().BeFalse();

        // Act
        _viewModel.SelectedTimeOfDayIndex = 3;
        _viewModel.SelectedWeather = WorldBuilderConstants.WeatherPresets.Snow;
        _viewModel.ApplyLightingCommand.Execute(null);

        // Assert
        _viewModel.CurrentMap.Should().NotBeNull();
        _viewModel.CurrentMap!.Lighting.TimeOfDay.Should().Be(4);
        _viewModel.IsDirty.Should().BeTrue();
        _viewModel.CanUndo.Should().BeTrue();

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        _viewModel.CurrentMap!.Lighting.TimeOfDay.Should().Be(0);
        _viewModel.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// Tests that tidying without a companion ini reports the missing file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TidyMapIni_MissingFile_ShowsInfoAsync()
    {
        // Arrange
        await OpenMapAsync("noini.map");

        // Act
        await _viewModel.TidyMapIniCommand.ExecuteAsync(null);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that editing the companion ini sends an open request to the INI editor.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EditMapIniInEditor_WithIniPresent_SendsOpenMessageAsync()
    {
        // Arrange
        var mapPath = await OpenMapAsync("editini.map");
        var iniPath = Path.ChangeExtension(mapPath, WorldBuilderConstants.FileExtensions.MapIni);
        await File.WriteAllTextAsync(iniPath, "Object Edit\nEnd\n");
        var recipient = new OpenFileMessageRecipient();
        try
        {
            WeakReferenceMessenger.Default.Register<OpenFileInToolMessage>(recipient);

            // Act
            _viewModel.EditMapIniInEditorCommand.Execute(null);

            // Assert
            var message = Assert.Single(recipient.Received, m => m.FilePath == iniPath);
            Assert.Equal(WorldBuilderConstants.Tool.IniEditorToolId, message.ToolId);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    /// <summary>
    /// Tests that editing the companion ini without an open map reports guidance.
    /// </summary>
    [Fact]
    public void EditMapIniInEditor_WithoutDocument_ShowsInfo()
    {
        // Act
        _viewModel.EditMapIniInEditorCommand.Execute(null);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that tidying normalizes the companion ini and reports success.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TidyMapIni_RewritesFile_ShowsSuccessAsync()
    {
        // Arrange
        var mapPath = await OpenMapAsync("tidy.map");
        var iniPath = Path.ChangeExtension(mapPath, WorldBuilderConstants.FileExtensions.MapIni);
        await File.WriteAllTextAsync(iniPath, "MapName = Tidy\r\n");

        // Act
        await _viewModel.TidyMapIniCommand.ExecuteAsync(null);

        // Assert
        var expected = MapIniSanitizer.Sanitize("MapName = Tidy\r\n").Text;
        (await File.ReadAllTextAsync(iniPath)).Should().Be(expected);
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that importing game data without a project reports guidance.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportGameData_WithoutProject_ShowsInfoAsync()
    {
        // Act
        await _viewModel.ImportGameDataWithDialogCommand.ExecuteAsync(null);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that the fence tool routes through two-point dispatch and places posts.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Fence_PressTwice_PlacesFencePostsAsync()
    {
        // Arrange
        var map = CreateCanvasMap();
        await OpenScriptMapAsync("fence.map", map);
        _viewModel.SelectedCanvasTool = MapCanvasTool.Fence;

        // Act: first press arms the two-point preview.
        _viewModel.HandleCanvasCellPressed(new CellPointerEventArgs(2, 2, true, false, false));

        // Assert
        _viewModel.ActiveLinePreview.Should().NotBeNull();

        // Act: second press completes the span.
        _viewModel.HandleCanvasCellPressed(new CellPointerEventArgs(5, 5, true, false, false));

        // Assert
        _viewModel.ActiveLinePreview.Should().BeNull();
        map.Objects.Should().NotBeEmpty();
        _viewModel.IsDirty.Should().BeTrue();
    }

    /// <summary>
    /// Tests that the flood fill tool fills the region under a single press.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TileFloodFill_Press_FillsRegionAsync()
    {
        // Arrange
        var map = CreateCanvasMap();
        await OpenScriptMapAsync("fill.map", map);
        _viewModel.SelectedTexture = map.Terrain.TextureClasses[1];
        _viewModel.SelectedCanvasTool = MapCanvasTool.TileFloodFill;

        // Act
        _viewModel.HandleCanvasCellPressed(new CellPointerEventArgs(0, 0, true, false, false));

        // Assert
        map.Terrain.TileIndices.Should().OnlyContain(tile => tile != 0);
        _viewModel.IsDirty.Should().BeTrue();
    }

    /// <summary>
    /// Tests that syncing the camera from the 3D viewport updates orientation
    /// without re-rendering the canvas or requesting a 3D rebuild.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task SetCameraFromViewport_DoesNotRaiseRefreshViewAsync()
    {
        // Arrange
        var mapPath = Path.Combine(_tempDirectory, "camera.map");
        _mockMapService
            .Setup(s => s.LoadAsync(mapPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorldBuilderMap>.CreateSuccess(CreateCanvasMap()));
        _mockMapService
            .Setup(s => s.Summarize(It.IsAny<WorldBuilderMap>()))
            .Returns(new MapSummaryReport { MapName = "Canvas Map" });
        await _viewModel.OpenMapAsync(mapPath);
        await _viewModel.RenderIdleAsync();
        Dispatcher.UIThread.RunJobs();
        var refreshes = 0;
        _viewModel.RequestRefreshView += OnRefresh;

        // Act
        _viewModel.SetCameraFromViewport(60.0, 30.0);
        await _viewModel.RenderIdleAsync();
        Dispatcher.UIThread.RunJobs();
        _viewModel.RequestRefreshView -= OnRefresh;

        // Assert
        _viewModel.CameraYaw.Should().Be(60.0);
        _viewModel.CameraPitch.Should().Be(30.0);
        refreshes.Should().Be(0);

        void OnRefresh() => refreshes++;
    }

    /// <summary>
    /// Tests that render passes skip the 2D bitmap while the 3D viewport is
    /// visible but still request the 3D scene refresh.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task RefreshCanvasBitmap_In3DMode_SkipsBitmapButRefreshesSceneAsync()
    {
        // Arrange
        var mapPath = Path.Combine(_tempDirectory, "skip2d.map");
        _mockMapService
            .Setup(s => s.LoadAsync(mapPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorldBuilderMap>.CreateSuccess(CreateCanvasMap()));
        _mockMapService
            .Setup(s => s.Summarize(It.IsAny<WorldBuilderMap>()))
            .Returns(new MapSummaryReport { MapName = "Canvas Map" });
        var refreshes = 0;
        _viewModel.RequestRefreshView += OnRefresh;
        await _viewModel.OpenMapAsync(mapPath);
        await _viewModel.RenderIdleAsync();
        Dispatcher.UIThread.RunJobs();

        // Assert
        _viewModel.Is3DViewportVisible.Should().BeTrue();
        _viewModel.CanvasBitmap.Should().BeNull();
        refreshes.Should().BeGreaterThan(0);
        _viewModel.RequestRefreshView -= OnRefresh;

        void OnRefresh() => refreshes++;
    }

    /// <summary>
    /// Tests that switching back to the 2D canvas re-renders the bitmap.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task RefreshCanvasBitmap_AfterSwitchTo2D_RendersBitmapAsync()
    {
        // Arrange
        var mapPath = Path.Combine(_tempDirectory, "back2d.map");
        _mockMapService
            .Setup(s => s.LoadAsync(mapPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorldBuilderMap>.CreateSuccess(CreateCanvasMap()));
        _mockMapService
            .Setup(s => s.Summarize(It.IsAny<WorldBuilderMap>()))
            .Returns(new MapSummaryReport { MapName = "Canvas Map" });
        await _viewModel.OpenMapAsync(mapPath);
        await _viewModel.RenderIdleAsync();
        Dispatcher.UIThread.RunJobs();

        // Act
        _viewModel.ViewMode = MapCanvasViewMode.TopDown2D;
        await _viewModel.RenderIdleAsync();
        Dispatcher.UIThread.RunJobs();

        // Assert
        _viewModel.Is3DViewportVisible.Should().BeFalse();
        _viewModel.CanvasBitmap.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that zoom commands dolly the 3D viewport instead of the hidden 2D canvas.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ZoomCommands_In3DMode_RequestViewportZoomStepAsync()
    {
        // Arrange
        var mapPath = Path.Combine(_tempDirectory, "zoom3d.map");
        _mockMapService
            .Setup(s => s.LoadAsync(mapPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorldBuilderMap>.CreateSuccess(CreateCanvasMap()));
        _mockMapService
            .Setup(s => s.Summarize(It.IsAny<WorldBuilderMap>()))
            .Returns(new MapSummaryReport { MapName = "Canvas Map" });
        await _viewModel.OpenMapAsync(mapPath);
        await _viewModel.RenderIdleAsync();
        Dispatcher.UIThread.RunJobs();
        var steps = new List<int>();
        _viewModel.RequestZoomStep += OnZoomStep;

        // Act
        _viewModel.ZoomIn();
        _viewModel.ZoomOut();
        _viewModel.RequestZoomStep -= OnZoomStep;

        // Assert
        _viewModel.Is3DViewportVisible.Should().BeTrue();
        steps.Should().Equal(1, -1);
        _viewModel.Zoom.Should().Be(1.0);

        void OnZoomStep(int direction) => steps.Add(direction);
    }

    /// <summary>
    /// Tests that zoom commands scale the 2D canvas when it is visible.
    /// </summary>
    [Fact]
    public void ZoomCommands_In2DMode_ScaleCanvasZoom()
    {
        // Act
        _viewModel.ZoomIn();
        var zoomedIn = _viewModel.Zoom;
        _viewModel.ZoomOut();
        _viewModel.ZoomOut();

        // Assert
        _viewModel.Is3DViewportVisible.Should().BeFalse();
        zoomedIn.Should().Be(1.25);
        _viewModel.Zoom.Should().Be(0.8);
    }

    private static WorldBuilderMap CreateMap(string name)
    {
        var map = new WorldBuilderMap();
        map.World.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.MapName,
            WorldBuilderConstants.DictValueType.UnicodeString,
            StringValue: name));
        return map;
    }

    private static WorldBuilderMap CreateCanvasMap()
    {
        var map = CreateMap("Canvas Map");
        map.Terrain.Width = 8;
        map.Terrain.Height = 8;
        map.Terrain.Heights = new byte[64];
        map.Terrain.TileIndices = new short[64];
        map.Terrain.BlendTileIndices = new short[64];
        map.Terrain.ExtraBlendTileIndices = new short[64];
        map.Terrain.CliffInfoIndices = new short[64];
        map.Terrain.TextureClasses.Add(new MapTextureClass(0, 16, 4, "Dirt"));
        map.Terrain.TextureClasses.Add(new MapTextureClass(16, 16, 4, "Grass"));
        return map;
    }

    private static WorldBuilderMap CreateScriptMap()
    {
        var map = CreateMap("Scripts");
        map.Objects.Add(new MapObjectEntry { Name = "OldDepot", X = 10, Y = 10 });
        var side = new MapSideEntry();
        side.Properties.Add(new MapDictValue(
            WorldBuilderConstants.DictKeys.PlayerName,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: "PlyrCivilian"));
        map.Sides.Add(side);

        var alpha = new ScriptModel
        {
            Name = "Alpha",
            Comment = "hello",
            IsActive = true,
            Easy = true,
            Normal = false,
            Hard = false,
        };
        var alphaBranch = new ScriptOrBranch();
        var alphaCondition = new ScriptCondition { ConditionType = 1, InternalName = "CONDITION_UNIT_EXISTS" };
        alphaCondition.Parameters.Add(new ScriptParameter
        {
            Type = WorldBuilderConstants.ScriptParameterType.Unit,
            StringValue = "OldDepot",
        });
        alphaBranch.Conditions.Add(alphaCondition);
        alpha.OrConditions.Add(alphaBranch);

        var beta = new ScriptModel { Name = "Beta", IsActive = true };
        var betaBranch = new ScriptOrBranch();
        var betaCondition = new ScriptCondition { ConditionType = 1, InternalName = "CONDITION_UNIT_EXISTS" };
        betaCondition.Parameters.Add(new ScriptParameter
        {
            Type = WorldBuilderConstants.ScriptParameterType.Unit,
            StringValue = "SpecialTank",
        });
        betaBranch.Conditions.Add(betaCondition);
        beta.OrConditions.Add(betaBranch);
        var betaAction = new ScriptActionModel { ActionType = 2, InternalName = "ACTION_MOVE_TO" };
        betaAction.Parameters.Add(new ScriptParameter
        {
            Type = WorldBuilderConstants.ScriptParameterType.Unit,
            StringValue = "OldDepot",
        });
        beta.ActionsTrue.Add(betaAction);

        var list = new ScriptListModel();
        list.Scripts.Add(alpha);
        list.Scripts.Add(beta);
        map.Scripts.Add(list);
        return map;
    }

    private static WorldBuilderMap CreateRoadMap()
    {
        var map = CreateScriptMap();
        MapOverlayTools.AddRoadSegment(map, new RoadSegment("PavedRoad", 10f, 20f, 0f, 50f, 60f, 0f, IsAngled: false, IsTight: false));
        MapOverlayTools.AddBridge(map, "BridgeConcrete", 100f, 100f, 200f, 100f);
        return map;
    }

    private static WorldBuilderMap CreateTeamMap()
    {
        var map = CreateScriptMap();
        var team = new MapTeamEntry();
        team.Properties.Add(new MapDictValue(
            WorldBuilderConstants.DictKeys.TeamName,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: "TeamAlpha"));
        team.Properties.Add(new MapDictValue(
            WorldBuilderConstants.DictKeys.TeamOwner,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: "GhostPlayer"));
        map.Teams.Add(team);
        return map;
    }

    private async Task<string> OpenMapAsync(string fileName)
    {
        var mapPath = Path.Combine(_tempDirectory, fileName);
        _mockMapService
            .Setup(s => s.LoadAsync(mapPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorldBuilderMap>.CreateSuccess(CreateMap("Test Map")));
        await _viewModel.OpenMapAsync(mapPath);
        return mapPath;
    }

    private async Task OpenScriptMapAsync(string fileName, WorldBuilderMap map)
    {
        var mapPath = Path.Combine(_tempDirectory, fileName);
        _mockMapService
            .Setup(s => s.LoadAsync(mapPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorldBuilderMap>.CreateSuccess(map));
        await _viewModel.OpenMapAsync(mapPath);
    }

    private IEnumerable<string> AllScriptParamValues()
    {
        return _viewModel.GroupScripts
            .SelectMany(s => s.OrConditions
                .SelectMany(b => b.Conditions)
                .SelectMany(c => c.Parameters)
                .Concat(s.ActionsTrue.SelectMany(a => a.Parameters))
                .Concat(s.ActionsFalse.SelectMany(a => a.Parameters)))
            .Select(p => p.StringValue);
    }

    /// <summary>
    /// Captures open-file messages sent through the messenger.
    /// </summary>
    internal sealed class OpenFileMessageRecipient : IRecipient<OpenFileInToolMessage>
    {
        /// <summary>
        /// Gets the received messages.
        /// </summary>
        public List<OpenFileInToolMessage> Received { get; } = [];

        /// <inheritdoc />
        public void Receive(OpenFileInToolMessage message)
        {
            Received.Add(message);
        }
    }
}
