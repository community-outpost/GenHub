namespace GenHub.Tests.Windows.Features.ActionSets.Fixes;

using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Fixes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="FirewallExceptionFix"/>.
/// </summary>
public class FirewallExceptionFixTests
{
    private readonly Mock<ILogger<FirewallExceptionFix>> _loggerMock = new();
    private readonly FirewallExceptionFix _fix;

    /// <summary>
    /// Initializes a new instance of the <see cref="FirewallExceptionFixTests"/> class.
    /// </summary>
    public FirewallExceptionFixTests()
    {
        _fix = new FirewallExceptionFix(_loggerMock.Object);
    }

    /// <summary>
    /// Verifies properties return expected defaults.
    /// </summary>
    [Fact]
    public void Properties_ReturnExpectedDefaults()
    {
        Assert.Equal("FirewallExceptionFix", _fix.Id);
        Assert.Equal("Windows Firewall Exceptions", _fix.Title);
        Assert.Equal(ActionSetConstants.Categories.Multiplayer, _fix.Category);
        Assert.False(_fix.IsCoreFix);
        Assert.False(_fix.IsCrucialFix);
    }

    /// <summary>
    /// Verifies that IsApplicableAsync returns true for installations with Generals or Zero Hour.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task IsApplicableAsync_WhenGamePresent_ReturnsTrueAsync()
    {
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = "C:\\Games\\Generals",
        };

        var result = await _fix.IsApplicableAsync(installation);

        Assert.True(result);
    }
}
