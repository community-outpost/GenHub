using Avalonia.Threading;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.IniEditor;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Interfaces.Tools.ModelViewer;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.IniEditor;
using GenHub.Features.Tools.IniEditor.Services;
using GenHub.Features.Tools.IniEditor.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.IniEditor;

/// <summary>
/// Shared factory helpers for INI editor tests.
/// </summary>
public static class IniEditorTestFactory
{
    /// <summary>
    /// Creates an <see cref="IniEditorViewModel"/> instance configured with standard mock dependencies.
    /// </summary>
    /// <param name="notificationService">Optional notification service override.</param>
    /// <returns>A new <see cref="IniEditorViewModel"/>.</returns>
    public static IniEditorViewModel CreateViewModel(
        INotificationService? notificationService = null) =>
        CreateViewModel(null, notificationService);

    /// <summary>
    /// Creates an <see cref="IniEditorViewModel"/> instance configured with standard mock dependencies.
    /// </summary>
    /// <param name="modelResolver">Optional model resolver override.</param>
    /// <param name="notificationService">Optional notification service override.</param>
    /// <param name="referenceService">Optional reference service override.</param>
    /// <param name="useRealReferenceService">Whether to index real fixture files instead of mocking.</param>
    /// <returns>A new <see cref="IniEditorViewModel"/>.</returns>
    public static IniEditorViewModel CreateViewModel(
        IW3dModelResolver? modelResolver,
        INotificationService? notificationService = null,
        IIniReferenceService? referenceService = null,
        bool useRealReferenceService = false)
    {
        var mockLocalization = new Mock<ILocalizationService>();
        mockLocalization
            .Setup(service => service.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => key);

        var documentService = new IniDocumentService(Mock.Of<ILogger<IniDocumentService>>(), mockLocalization.Object);
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
        mockReferenceService
            .Setup(service => service.FindReferencersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<IniReferenceEntry>>.CreateSuccess(new List<IniReferenceEntry>(), TimeSpan.Zero));
        mockReferenceService
            .Setup(service => service.CloneBlockAsync(It.IsAny<IniReferenceEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IniBlock?>.CreateSuccess(null, TimeSpan.Zero));

        return new IniEditorViewModel(
            documentService,
            new IniSchemaService(mockLocalization.Object),
            referenceService ?? (useRealReferenceService ? RealReferenceService(documentService) : mockReferenceService.Object),
            Mock.Of<ISageMappedImageParser>(),
            Mock.Of<IWndImageAssetService>(),
            Mock.Of<IGameInstallationService>(),
            modelResolver ?? Mock.Of<IW3dModelResolver>(),
            notificationService ?? Mock.Of<INotificationService>(),
            mockLocalization.Object,
            Mock.Of<IDialogService>(),
            Mock.Of<ILogger<IniEditorViewModel>>());
    }

    /// <summary>
    /// Pumps the UI dispatcher and polls until the specified condition is met or the timeout expires.
    /// </summary>
    /// <param name="condition">Condition predicate to check.</param>
    /// <param name="timeout">Timeout duration.</param>
    /// <returns>True if the condition evaluated to true before timeout, otherwise false.</returns>
    public static Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout) =>
        WaitForAsync(condition, (int)timeout.TotalMilliseconds);

    /// <summary>
    /// Pumps the UI dispatcher and polls until the specified condition is met or the timeout expires.
    /// </summary>
    /// <param name="condition">Condition predicate to check.</param>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    /// <returns>True if the condition evaluated to true before timeout, otherwise false.</returns>
    public static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
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

    private static IniReferenceService RealReferenceService(IniDocumentService documentService)
    {
        var mockInstallations = new Mock<IGameInstallationService>();
        mockInstallations
            .Setup(service => service.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(new List<GameInstallation>(), TimeSpan.Zero));
        return new IniReferenceService(
            documentService,
            mockInstallations.Object,
            Mock.Of<IArchiveService>(),
            Mock.Of<ILogger<IniReferenceService>>());
    }
}
