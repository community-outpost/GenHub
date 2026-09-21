using GenHub.Core.Interfaces.Tools.WndEditor;

namespace GenHub.Features.Tools.WndEditor.Services;

/// <summary>
/// Aggregates asset services for the WND editor.
/// </summary>
public sealed class WndEditorAssetService(
    IWndImageAssetService imageAssetService,
    IWndStringTableService stringTableService) : IWndEditorAssetService
{
    /// <inheritdoc />
    public IWndImageAssetService Images { get; } = imageAssetService;

    /// <inheritdoc />
    public IWndStringTableService Strings { get; } = stringTableService;

    /// <inheritdoc />
    public void InvalidateCache()
    {
        Images.InvalidateCache();
        Strings.InvalidateCache();
    }
}
