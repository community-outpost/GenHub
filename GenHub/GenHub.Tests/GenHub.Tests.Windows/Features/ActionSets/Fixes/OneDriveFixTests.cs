namespace GenHub.Tests.Windows.Features.ActionSets.Fixes;

using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Fixes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="OneDriveFix"/>.
/// </summary>
public class OneDriveFixTests
{
    private readonly Mock<ILogger<OneDriveFix>> _loggerMock = new();

    /// <summary>
    /// Verifies properties and basic instantiation.
    /// </summary>
    [Fact]
    public void Properties_ReturnExpectedDefaults()
    {
        var fix = new OneDriveFix(_loggerMock.Object);

        Assert.Equal("OneDriveFix", fix.Id);
        Assert.Equal("OneDrive Protection & Relocation", fix.Title);
        Assert.True(fix.IsCoreFix);
        Assert.True(fix.IsCrucialFix);
    }

    /// <summary>
    /// Verifies that Undo returns success when no backups exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task UndoAsync_WhenNoBackupsExist_ReturnsSuccess()
    {
        var fix = new OneDriveFix(_loggerMock.Object);
        var installation = new GameInstallation("C:\\TestPath", GameInstallationType.Steam);

        var result = await fix.UndoAsync(installation);

        Assert.True(result.Success);
    }
}
