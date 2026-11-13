namespace GenHub.Core.Interfaces.Tools.WndEditor;

/// <summary>
/// Aggregates asset services used by the WND editor.
/// </summary>
public interface IWndEditorAssetService
{
    /// <summary>
    /// Gets the image asset service.
    /// </summary>
    IWndImageAssetService Images { get; }

    /// <summary>
    /// Gets the string table service.
    /// </summary>
    IWndStringTableService Strings { get; }

    /// <summary>
    /// Invalidates cached asset indexes, preview images, and string tables.
    /// </summary>
    void InvalidateCache();
}
