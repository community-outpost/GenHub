using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.IniEditor;
using GenHub.Core.Interfaces.Tools.ModelViewer;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.IniEditor;
using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Features.Tools.IniEditor.Services;
using GenHub.Features.Tools.IniEditor.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static GenHub.Tests.Core.Features.Tools.IniEditor.IniEditorTestFactory;

namespace GenHub.Tests.Core.Features.Tools.IniEditor.Views;

/// <summary>
/// Unit tests for the 3D model preview in <see cref="IniEditorViewModel"/>.
/// </summary>
public sealed class IniEditorPreviewTests
{
    /// <summary>
    /// Verifies that selecting a block with a model resolves the preview scene.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingBlockWithModel_ResolvesPreviewSceneAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Tank";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = "Model";
        viewModel.NewFieldValue = "TestUnit";
        viewModel.AddFieldCommand.Execute(null);

        bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));

        Assert.True(loaded);
        Assert.Equal("TestUnit", viewModel.PreviewModelName);
        var mesh = Assert.Single(viewModel.PreviewMeshes);
        Assert.Equal("C.TURRET", mesh.Name);
        Assert.Equal("ROOT", mesh.BoneName);
        Assert.Single(viewModel.PreviewClips);
        Assert.Equal(1, viewModel.PreviewFrameCount);
        Assert.Equal("Tools.IniEditor.Preview3D.Ready", viewModel.PreviewStatusText);
    }

    /// <summary>
    /// Verifies that selecting a preview mesh lists referencing draw states.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingPreviewMesh_ListsReferencingDrawStatesAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubPreview{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, PreviewIni());
        try
        {
            Assert.True(await viewModel.OpenFileAsync(filePath));
            bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));
            Assert.True(loaded);

            viewModel.PreviewSelectedMeshIndex = 0;

            var module = Assert.Single(viewModel.PreviewMeshModules);
            Assert.Contains("ModelConditionState", module.DisplayName, StringComparison.Ordinal);
            Assert.True(viewModel.HasPreviewMeshModules);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that a missing model shows the not-found status without a toast.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task MissingModel_ShowsNotFoundWithoutToastAsync()
    {
        var mockResolver = new Mock<IW3dModelResolver>();
        mockResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<W3dResolvedModel>.CreateFailure("Model not found: Ghost", TimeSpan.Zero));
        var mockNotifications = new Mock<INotificationService>();
        using var viewModel = CreateViewModel(mockResolver.Object, mockNotifications.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Tank";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = "Model";
        viewModel.NewFieldValue = "Ghost";
        viewModel.AddFieldCommand.Execute(null);

        bool settled = await WaitForAsync(
            () => !viewModel.IsPreviewLoading && !viewModel.HasPreviewScene && viewModel.PreviewStatusText.Length > 0,
            TimeSpan.FromSeconds(5));

        Assert.True(settled);
        Assert.Equal("Tools.IniEditor.Preview3D.NotFound", viewModel.PreviewStatusText);
        mockNotifications.Verify(
            notifications => notifications.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a command set with no resolvable models clears the loading state without a toast.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingCommandSet_WithNoResolvableModels_ClearsLoadingWithoutToastAsync()
    {
        var mockResolver = new Mock<IW3dModelResolver>();
        mockResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<W3dResolvedModel>.CreateFailure("Model not found: Ghost", TimeSpan.Zero));
        var mockNotifications = new Mock<INotificationService>();
        using var viewModel = CreateViewModel(mockResolver.Object, mockNotifications.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubCommandSetAbort{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, CommandSetIni());
        try
        {
            Assert.True(await viewModel.OpenFileAsync(filePath));
            var setNode = FindNodeByName(viewModel, "SetBuildTank");
            Assert.NotNull(setNode);
            viewModel.SelectedNode = setNode;

            bool settled = await WaitForAsync(
                () => !viewModel.IsPreviewLoading && !viewModel.HasPreviewScene && viewModel.PreviewStatusText.Length > 0,
                TimeSpan.FromSeconds(5));

            Assert.True(settled);
            Assert.Equal("Tools.IniEditor.Preview3D.NotFound", viewModel.PreviewStatusText);
            mockNotifications.Verify(
                notifications => notifications.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Never);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that a corrupt model shows the error status and toasts once.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task CorruptModel_ShowsErrorAndToastsOnceAsync()
    {
        var mockResolver = new Mock<IW3dModelResolver>();
        mockResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<W3dResolvedModel>.CreateFailure("Invalid mesh chunk: Bad", TimeSpan.Zero));
        var mockNotifications = new Mock<INotificationService>();
        using var viewModel = CreateViewModel(mockResolver.Object, mockNotifications.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Tank";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = "Model";
        viewModel.NewFieldValue = "Bad";
        viewModel.AddFieldCommand.Execute(null);

        bool settled = await WaitForAsync(
            () => !viewModel.IsPreviewLoading && viewModel.PreviewStatusText.Length > 0,
            TimeSpan.FromSeconds(5));

        Assert.True(settled);
        Assert.Equal("Tools.IniEditor.Preview3D.ParseError", viewModel.PreviewStatusText);
        mockNotifications.Verify(
            notifications => notifications.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that changing the frame samples the animation pose.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task FrameChange_SamplesAnimationPoseAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Tank";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = "Model";
        viewModel.NewFieldValue = "TestUnit";
        viewModel.AddFieldCommand.Execute(null);
        bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));
        Assert.True(loaded);

        viewModel.PreviewFrame = 1;

        Assert.NotNull(viewModel.PreviewPose);
        var translation = viewModel.PreviewPose[0].Translation;
        Assert.Equal(6, translation.X);
        Assert.Equal(0, translation.Y);
    }

    /// <summary>
    /// Verifies that a static preview without animation clips still exposes
    /// the bind pose so rigid meshes render at their pivots.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task StaticPreviewWithoutClips_ExposesBindPoseAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel(false));
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Tank";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = "Model";
        viewModel.NewFieldValue = "TestUnit";
        viewModel.AddFieldCommand.Execute(null);

        bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));

        Assert.True(loaded);
        Assert.Null(viewModel.PreviewPose);
        Assert.NotNull(viewModel.PreviewBindPose);
        var bindPose = Assert.Single(viewModel.PreviewBindPose);
        Assert.Equal(1, bindPose.Translation.X);
    }

    /// <summary>
    /// Verifies that selecting a draw state highlights the referenced mesh in the 3D preview.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingDrawState_HighlightsReferencedPreviewMeshAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubPreview{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, PreviewIni());
        try
        {
            Assert.True(await viewModel.OpenFileAsync(filePath));
            bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));
            Assert.True(loaded);

            viewModel.SelectedNode = viewModel.RootNodes[0].Children[0].Children[0];

            Assert.Equal(0, viewModel.PreviewSelectedMeshIndex);
            Assert.NotNull(viewModel.SelectedPreviewMesh);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that blocks without a model clear the preview.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task BlockWithoutModel_ClearsPreviewAsync()
    {
        using var viewModel = CreateViewModel(Mock.Of<IW3dModelResolver>());
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Plain";
        viewModel.AddBlockCommand.Execute(null);

        Assert.False(viewModel.HasPreviewScene);
        Assert.Equal("Tools.IniEditor.Preview3D.Empty", viewModel.PreviewStatusText);
    }

    /// <summary>
    /// Verifies that selecting a command set lists its related objects and previews the first model.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingCommandSet_ListsRelatedObjectsAndPreviewsModelAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubCommandSet{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, CommandSetIni());
        try
        {
            Assert.True(await viewModel.OpenFileAsync(filePath));
            var setNode = FindNodeByName(viewModel, "SetBuildTank");
            Assert.NotNull(setNode);
            viewModel.SelectedNode = setNode;

            Assert.True(viewModel.HasPreviewRelatedObjects);
            var card = Assert.Single(viewModel.PreviewRelatedObjects);
            Assert.Equal("TestTank", card.Title);
            Assert.Equal("TestUnit", viewModel.SelectedBlockModel);

            bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));
            Assert.True(loaded);
            Assert.Equal("TestUnit", viewModel.PreviewModelName);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that selecting a command button resolves its object model and related card.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingCommandButton_ResolvesObjectModelAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubCommandButton{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, CommandSetIni());
        try
        {
            Assert.True(await viewModel.OpenFileAsync(filePath));
            var buttonNode = FindNodeByName(viewModel, "ButtonBuildTank");
            Assert.NotNull(buttonNode);
            viewModel.SelectedNode = buttonNode;

            Assert.True(viewModel.HasPreviewRelatedObjects);
            Assert.Single(viewModel.PreviewRelatedObjects);
            Assert.Equal("TestUnit", viewModel.SelectedBlockModel);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that a faction variant without its own model previews the base object model.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingVariantObject_ResolvesBaseObjectModelAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubVariant{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, VariantIni());
        try
        {
            Assert.True(await viewModel.OpenFileAsync(filePath));
            var variantNode = FindNodeByName(viewModel, "AirF_AmericaVehicleComanche");
            Assert.NotNull(variantNode);
            viewModel.SelectedNode = variantNode;

            Assert.Equal("TestUnit", viewModel.SelectedBlockModel);
            Assert.True(viewModel.HasPreviewModelSource);
            Assert.Equal("Tools.IniEditor.Preview3D.ViaSource", viewModel.PreviewModelSourceText);
            Assert.False(viewModel.HasFieldRows);
            Assert.True(viewModel.HasSelectedBlockModules);

            bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));
            Assert.True(loaded);
            Assert.Equal("TestUnit", viewModel.PreviewModelName);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that selecting a variant module keeps the base preview and shows the module fields.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingVariantModule_KeepsBasePreviewAndShowsModuleFieldsAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubVariantModule{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, VariantIni());
        try
        {
            Assert.True(await viewModel.OpenFileAsync(filePath));
            var variantNode = FindNodeByName(viewModel, "AirF_AmericaVehicleComanche");
            Assert.NotNull(variantNode);
            viewModel.SelectedNode = variantNode;
            bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));
            Assert.True(loaded);

            var moduleNode = Assert.Single(variantNode.Children);
            viewModel.SelectedNode = moduleNode;

            Assert.Equal("AirF_AmericaVehicleComanche", viewModel.SelectedBlockTitle);
            Assert.True(viewModel.HasPreviewScene);
            Assert.False(viewModel.HasFieldRows);

            var behaviorNode = Assert.Single(moduleNode.Children);
            viewModel.SelectedNode = behaviorNode;

            Assert.Equal("AirF_AmericaVehicleComanche", viewModel.SelectedBlockTitle);
            Assert.True(viewModel.HasPreviewScene);
            Assert.True(viewModel.HasFieldRows);
            Assert.Single(viewModel.FieldRows);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that a variant without a matching base object clears the preview.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task VariantWithoutBase_ClearsPreviewAsync()
    {
        using var viewModel = CreateViewModel(Mock.Of<IW3dModelResolver>());
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "AirF_Ghost";
        viewModel.AddBlockCommand.Execute(null);

        Assert.False(viewModel.HasPreviewScene);
        Assert.False(viewModel.HasPreviewModelSource);
        Assert.Equal("Tools.IniEditor.Preview3D.Empty", viewModel.PreviewStatusText);
    }

    /// <summary>
    /// Verifies that a special-power command button resolves the delivering object across files.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingSpecialPowerButton_ResolvesDeliveringObjectAcrossFilesAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object, useRealReferenceService: true);
        var folder = Path.Combine(Path.GetTempPath(), $"GenHubXRef{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "ChinaCommandButton.ini"), CarpetBombButtonIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "SpecialPower.ini"), CarpetBombPowerIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "ChinaAirforce.ini"), CarpetBomberIni());

            Assert.True(await viewModel.OpenFolderAsync(folder));
            Assert.True(await viewModel.OpenFileAsync(Path.Combine(folder, "ChinaCommandButton.ini")));
            var buttonNode = FindNodeByName(viewModel, "Command_ChinaCarpetBomb");
            Assert.NotNull(buttonNode);
            viewModel.SelectedNode = buttonNode;

            bool resolved = await WaitForAsync(() => viewModel.SelectedBlockModel == "TestUnit", TimeSpan.FromSeconds(8));
            Assert.True(resolved);
            Assert.True(viewModel.HasPreviewModelSource);
            Assert.Equal(3, viewModel.PreviewResolutionPath.Count);
            Assert.True(viewModel.HasPreviewReferencers);

            bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(8));
            Assert.True(loaded);
            Assert.Equal("TestUnit", viewModel.PreviewModelName);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// Verifies that a button resolves through SpecialPower OCL CreateObject to the delivering object.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingOclPowerButton_ResolvesCreatedObjectAcrossFilesAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object, useRealReferenceService: true);
        var folder = Path.Combine(Path.GetTempPath(), $"GenHubOcl{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "OclButton.ini"), OclButtonIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "OclPower.ini"), OclPowerIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "OclList.ini"), OclListIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "OclObject.ini"), OclObjectIni());

            Assert.True(await viewModel.OpenFolderAsync(folder));
            Assert.True(await viewModel.OpenFileAsync(Path.Combine(folder, "OclButton.ini")));
            var buttonNode = FindNodeByName(viewModel, "Command_OclStrike");
            Assert.NotNull(buttonNode);
            viewModel.SelectedNode = buttonNode;

            bool resolved = await WaitForAsync(() => viewModel.SelectedBlockModel == "TestUnit", TimeSpan.FromSeconds(8));
            Assert.True(resolved);
            Assert.Contains(viewModel.PreviewResolutionPath, hop => hop.Title == "OCL_TestDrop");

            bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(8));
            Assert.True(loaded);
            Assert.Equal("TestUnit", viewModel.PreviewModelName);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// Verifies that a faction variant resolves its base object model across files.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingVariant_ResolvesBaseObjectModelAcrossFilesAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object, useRealReferenceService: true);
        var folder = Path.Combine(Path.GetTempPath(), $"GenHubVariant{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "Map.ini"), VariantMapIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "Objects.ini"), VariantBaseIni());

            Assert.True(await viewModel.OpenFolderAsync(folder));
            Assert.True(await viewModel.OpenFileAsync(Path.Combine(folder, "Map.ini")));
            var variantNode = FindNodeByName(viewModel, "Lazr_AmericaInfantryPathfinder");
            Assert.NotNull(variantNode);
            viewModel.SelectedNode = variantNode;

            bool resolved = await WaitForAsync(() => viewModel.SelectedBlockModel == "TestUnit", TimeSpan.FromSeconds(8));
            Assert.True(resolved);

            bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(8));
            Assert.True(loaded);
            Assert.Equal("TestUnit", viewModel.PreviewModelName);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// Verifies that a command set composes its owner and related objects into one scene.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingCommandSet_BuildsCompositeSceneAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel());
        using var viewModel = CreateViewModel(mockResolver.Object, useRealReferenceService: true);
        var folder = Path.Combine(Path.GetTempPath(), $"GenHubComposite{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "Set.ini"), CompositeSetIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "Buttons.ini"), CompositeButtonsIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "Objects.ini"), CompositeObjectsIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "Owner.ini"), CompositeOwnerIni());

            Assert.True(await viewModel.OpenFolderAsync(folder));
            Assert.True(await viewModel.OpenFileAsync(Path.Combine(folder, "Set.ini")));
            var setNode = FindNodeByName(viewModel, "SupW_WarFactorySet");
            Assert.NotNull(setNode);
            viewModel.SelectedNode = setNode;

            bool composed = await WaitForAsync(
                () => viewModel.PreviewRelatedObjects.Count == 3 &&
                    viewModel.PreviewStatusText == "Tools.IniEditor.Preview3D.ReadyComposite",
                TimeSpan.FromSeconds(10));
            Assert.True(composed);
            Assert.True(viewModel.HasPreviewScene);
            Assert.Equal(3, viewModel.PreviewScene!.Meshes.Count);
            Assert.Equal("SupW_WarFactory", viewModel.PreviewRelatedObjects[0].Title);
            Assert.Equal(0, viewModel.PreviewSelectedMeshIndex);
            Assert.Equal("SupW_VehicleTomahawk", viewModel.PreviewMeshes[1].PartLabel);
            Assert.True(viewModel.PreviewRelatedObjects[0].IsHighlighted);
            Assert.False(viewModel.PreviewRelatedObjects[1].IsHighlighted);

            viewModel.PreviewSelectedMeshIndex = 1;
            Assert.True(viewModel.PreviewRelatedObjects[1].IsHighlighted);
            Assert.False(viewModel.PreviewRelatedObjects[0].IsHighlighted);

            viewModel.SelectedPreviewMesh = viewModel.PreviewMeshes[2];
            Assert.True(viewModel.PreviewRelatedObjects[2].IsHighlighted);
            Assert.False(viewModel.PreviewRelatedObjects[1].IsHighlighted);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// Verifies that missing texture names surface on the preview canvas.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task PreviewWithMissingTexture_ListsMissingNamesAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel(includeMissingTexture: true));
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Tank";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = "Model";
        viewModel.NewFieldValue = "TestUnit";
        viewModel.AddFieldCommand.Execute(null);

        bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));
        Assert.True(loaded);
        Assert.True(viewModel.HasPreviewMissingTextures);
        Assert.Contains("GhostTexture", viewModel.PreviewMissingTextures);
    }

    /// <summary>
    /// Verifies that a clip targeting an unknown hierarchy rests at the bind pose.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task ClipWithMissingHierarchy_RestsAtBindPoseAsync()
    {
        var mockResolver = ResolverReturning(ResolvedModel(clipHierarchyName: "Other"));
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Tank";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = "Model";
        viewModel.NewFieldValue = "TestUnit";
        viewModel.AddFieldCommand.Execute(null);

        bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));
        Assert.True(loaded);
        Assert.NotNull(viewModel.SelectedPreviewClip);
        Assert.Null(viewModel.PreviewPose);
        Assert.NotNull(viewModel.PreviewBindPose);
    }

    /// <summary>
    /// Verifies that selecting a non-samplable clip while playing stops playback.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingNonSamplableClipWhilePlaying_StopsPlaybackAsync()
    {
        var resolved = ResolvedModel();
        var raw = new W3dAnimationClip("RAW", "H", 2, 30, false, 0, []);
        var model = resolved with
        {
            Model = resolved.Model with { Animations = [resolved.Model.Animations[0], raw] },
        };
        var mockResolver = ResolverReturning(model);
        using var viewModel = CreateViewModel(mockResolver.Object);
        viewModel.FileExplorer.Directory = Path.GetTempPath();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Tank";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = "Model";
        viewModel.NewFieldValue = "TestUnit";
        viewModel.AddFieldCommand.Execute(null);

        bool loaded = await WaitForAsync(() => viewModel.HasPreviewScene, TimeSpan.FromSeconds(5));
        Assert.True(loaded);
        Assert.Equal(2, viewModel.PreviewClips.Count);
        viewModel.IsPreviewPlaying = true;
        Assert.True(viewModel.IsPreviewPlaying);

        viewModel.SelectedPreviewClip = raw;

        Assert.False(viewModel.IsPreviewPlaying);
        Assert.Null(viewModel.PreviewPose);
    }

    private static IniTreeNodeViewModel? FindNodeByName(IniEditorViewModel viewModel, string name)
    {
        var queue = new Queue<IniTreeNodeViewModel>(viewModel.RootNodes);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (string.Equals(node.Block.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            foreach (var child in node.Children)
            {
                queue.Enqueue(child);
            }
        }

        return null;
    }

    private static string CommandSetIni()
    {
        return "Object TestTank\n" +
            "  Model = TestUnit\n" +
            "End\n" +
            "CommandButton ButtonBuildTank\n" +
            "  Object = TestTank\n" +
            "End\n" +
            "CommandSet SetBuildTank\n" +
            "  1 = ButtonBuildTank\n" +
            "  2 = ButtonBuildTank\n" +
            "End\n";
    }

    private static Mock<IW3dModelResolver> ResolverReturning(W3dResolvedModel resolved)
    {
        var mockResolver = new Mock<IW3dModelResolver>();
        mockResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<W3dResolvedModel>.CreateSuccess(resolved, TimeSpan.Zero));
        return mockResolver;
    }

    private static W3dResolvedModel ResolvedModel(bool includeClip = true, string clipHierarchyName = "H", bool includeMissingTexture = false)
    {
        var origin = new W3dVector3(0, 0, 0);
        var mesh = new W3dMesh(
            "TURRET",
            "C",
            0,
            0,
            new W3dBoundingBox(origin, new W3dVector3(1, 1, 0), origin, 1),
            [new W3dVector3(0, 0, 0), new W3dVector3(1, 0, 0), new W3dVector3(0, 1, 0)],
            [new W3dVector3(0, 0, 1), new W3dVector3(0, 0, 1), new W3dVector3(0, 0, 1)],
            [new W3dTriangle(0, 1, 2, 0)],
            [],
            [],
            [],
            [],
            []);
        var hierarchy = new W3dHierarchy(
            "H",
            origin,
            [new W3dPivot("ROOT", -1, new W3dVector3(1, 0, 0), origin, new W3dQuaternion(0, 0, 0, 1))]);
        var clip = new W3dAnimationClip(
            "IDLE",
            clipHierarchyName,
            2,
            30,
            false,
            0,
            [new W3dAnimationChannel(0, 0, 0, 1, 1, [new W3dAnimationKey(0, [0]), new W3dAnimationKey(1, [5])], false)]);
        var lod = new W3dModelLod("L", "H", [new W3dLevelOfDetail(100, [new W3dSubObject(0, "C.TURRET")])]);
        List<W3dAnimationClip> clips = includeClip ? [clip] : [];
        var model = new W3dModel([mesh], [hierarchy], clips, [lod], []);
        List<string> missing = includeMissingTexture ? ["GhostTexture"] : [];
        return new W3dResolvedModel("TestUnit", "Art/TestUnit.w3d", model, [], missing);
    }

    private static string PreviewIni()
    {
        return "Object TestTank\n" +
            "  Draw = W3DTankDraw ModuleTag_01\n" +
            "    ModelConditionState = NONE\n" +
            "      Model = TestUnit\n" +
            "      ShowSubObjects = TURRET\n" +
            "    End\n" +
            "  End\n" +
            "End\n";
    }

    private static string CarpetBombButtonIni()
    {
        return "CommandButton Command_ChinaCarpetBomb\n" +
            "  Command = SPECIAL_POWER\n" +
            "  SpecialPower = SuperweaponChinaCarpetBomb\n" +
            "  Options = NEED_SPECIAL_POWER_CHINA_SCIENCE NEED_TARGET_POS CONTEXTMODE_COMMAND\n" +
            "  TextLabel = CONTROLBAR:CarpetBomb\n" +
            "  ButtonImage = SNCBomber\n" +
            "  ButtonBorderType = ACTION\n" +
            "  InvalidCursorName = GenericInvalid\n" +
            "  RadiusCursorType = CARPETBOMB\n" +
            "  DescriptLabel = CONTROLBAR:TooltipCarpetBomb\n" +
            "End\n" +
            "CommandSet SetCarpetBomb\n" +
            "  1 = Command_ChinaCarpetBomb\n" +
            "End\n";
    }

    private static string CarpetBombPowerIni()
    {
        return "SpecialPower SuperweaponChinaCarpetBomb\n" +
            "  ReloadTime = 240000\n" +
            "End\n";
    }

    private static string CarpetBomberIni()
    {
        return "Object ChinaCarpetBomber\n" +
            "  Model = TestUnit\n" +
            "  Behavior = CarpetBombBehavior ModuleTag_Deliver\n" +
            "    SpecialPower = SuperweaponChinaCarpetBomb\n" +
            "  End\n" +
            "End\n";
    }

    private static string OclButtonIni()
    {
        return "CommandButton Command_OclStrike\n" +
            "  Command = SPECIAL_POWER\n" +
            "  SpecialPower = SuperweaponOclTest\n" +
            "End\n";
    }

    private static string OclPowerIni()
    {
        return "SpecialPower SuperweaponOclTest\n" +
            "  ReloadTime = 60000\n" +
            "  OCL = OCL_TestDrop\n" +
            "End\n";
    }

    private static string OclListIni()
    {
        return "ObjectCreationList OCL_TestDrop\n" +
            "  CreateObject = TestDropShip\n" +
            "  Count = 1\n" +
            "End\n";
    }

    private static string OclObjectIni()
    {
        return "Object TestDropShip\n" +
            "  Model = TestUnit\n" +
            "End\n";
    }

    private static string CompositeSetIni()
    {
        return "CommandSet SupW_WarFactorySet\n" +
            "  2 = SupW_BuildTomahawk\n" +
            "  3 = SupW_BuildHumvee\n" +
            "End\n";
    }

    private static string CompositeButtonsIni()
    {
        return "CommandButton SupW_BuildTomahawk\n" +
            "  Object = SupW_VehicleTomahawk\n" +
            "End\n" +
            "CommandButton SupW_BuildHumvee\n" +
            "  Object = SupW_VehicleHumvee\n" +
            "End\n";
    }

    private static string CompositeObjectsIni()
    {
        return "Object SupW_VehicleTomahawk\n" +
            "  Model = TestUnit\n" +
            "End\n" +
            "Object SupW_VehicleHumvee\n" +
            "  Model = TestUnit\n" +
            "End\n";
    }

    private static string CompositeOwnerIni()
    {
        return "Object SupW_WarFactory\n" +
            "  CommandSet = SupW_WarFactorySet\n" +
            "  Model = TestUnit\n" +
            "End\n";
    }

    private static string VariantMapIni()
    {
        return "Object Lazr_AmericaInfantryPathfinder\n" +
            "  AddModule\n" +
            "    Behavior = VeterancyGainCreate ModuleTag_HeIden\n" +
            "      StartingLevel = HEROIC\n" +
            "    End\n" +
            "  End\n" +
            "End\n";
    }

    private static string VariantBaseIni()
    {
        return "Object AmericaInfantryPathfinder\n" +
            "  Model = TestUnit\n" +
            "End\n";
    }

    private static string VariantIni()
    {
        return "Object AmericaVehicleComanche\n" +
            "  Model = TestUnit\n" +
            "End\n" +
            "Object AirF_AmericaVehicleComanche\n" +
            "  AddModule\n" +
            "    Behavior = VeterancyGainCreate ModuleTag_HeIden\n" +
            "      StartingLevel = HEROIC\n" +
            "    End\n" +
            "  End\n" +
            "End\n";
    }
}
