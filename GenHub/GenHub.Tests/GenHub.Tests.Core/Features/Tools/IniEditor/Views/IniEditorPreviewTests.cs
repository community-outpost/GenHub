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

    private static W3dResolvedModel ResolvedModel()
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
            "H",
            2,
            30,
            false,
            0,
            [new W3dAnimationChannel(0, 0, 0, 1, 1, [new W3dAnimationKey(0, [0]), new W3dAnimationKey(1, [5])], false)]);
        var lod = new W3dModelLod("L", "H", [new W3dLevelOfDetail(100, [new W3dSubObject(0, "C.TURRET")])]);
        var model = new W3dModel([mesh], [hierarchy], [clip], [lod], []);
        return new W3dResolvedModel("TestUnit", "Art/TestUnit.w3d", model, [], []);
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

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }).GetTask().ConfigureAwait(false);
            if (condition())
            {
                return true;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        await Dispatcher.UIThread.InvokeAsync(() => { }).GetTask().ConfigureAwait(false);
        return condition();
    }

    private static IniEditorViewModel CreateViewModel(IW3dModelResolver modelResolver, INotificationService? notificationService = null)
    {
        var mockLocalization = new Mock<ILocalizationService>();
        mockLocalization
            .Setup(service => service.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => key);

        var mockReferenceService = new Mock<IIniReferenceService>();
        mockReferenceService
            .Setup(service => service.RebuildIndexAsync(
                It.IsAny<IniDocument?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<int>.CreateSuccess(0, TimeSpan.Zero));
        mockReferenceService
            .Setup(service => service.Entries)
            .Returns(new List<IniReferenceEntry>());
        mockReferenceService
            .Setup(service => service.GetNames(It.IsAny<string>()))
            .Returns(new List<string>());

        return new IniEditorViewModel(
            new IniDocumentService(Mock.Of<ILogger<IniDocumentService>>(), mockLocalization.Object),
            new IniSchemaService(mockLocalization.Object),
            mockReferenceService.Object,
            Mock.Of<ISageMappedImageParser>(),
            Mock.Of<IWndImageAssetService>(),
            Mock.Of<IGameInstallationService>(),
            modelResolver,
            notificationService ?? Mock.Of<INotificationService>(),
            mockLocalization.Object,
            Mock.Of<IDialogService>(),
            Mock.Of<ILogger<IniEditorViewModel>>());
    }
}
