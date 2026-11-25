using GenHub.Core.Constants;
using GenHub.Features.Tools.WndEditor;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WndEditor;

/// <summary>
/// Unit tests for <see cref="WndEditorToolPlugin"/>.
/// </summary>
public class WndEditorToolPluginTests
{
    /// <summary>
    /// Verifies that Metadata returns expected values including IconPath.
    /// </summary>
    [Fact]
    public void Metadata_HasExpectedValues()
    {
        var plugin = new WndEditorToolPlugin();

        Assert.Equal(ToolConstants.WndEditor.Id, plugin.Metadata.Id);
        Assert.Equal(ToolConstants.WndEditor.Name, plugin.Metadata.Name);
        Assert.NotNull(plugin.Metadata.Version);
        Assert.Equal(ToolConstants.WndEditor.Author, plugin.Metadata.Author);
        Assert.Equal(ToolConstants.WndEditor.Description, plugin.Metadata.Description);
        Assert.Equal(ToolConstants.WndEditor.IconPath, plugin.Metadata.IconPath);
        Assert.Equal(UriConstants.WndEditorIconUri, plugin.Metadata.IconPath);
        Assert.True(plugin.Metadata.IsBundled);
    }
}
