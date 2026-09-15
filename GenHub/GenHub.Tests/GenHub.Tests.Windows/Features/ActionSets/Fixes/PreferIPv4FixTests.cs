namespace GenHub.Tests.Windows.Features.ActionSets.Fixes;

using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Fixes;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="PreferIPv4Fix"/>.
/// </summary>
public class PreferIPv4FixTests
{
    private readonly Mock<IRegistryService> _registryMock = new();
    private readonly Mock<ILogger<PreferIPv4Fix>> _loggerMock = new();
    private readonly PreferIPv4Fix _fix;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreferIPv4FixTests"/> class.
    /// </summary>
    public PreferIPv4FixTests()
    {
        _registryMock.Setup(r => r.IsRunningAsAdministrator()).Returns(true);
        _registryMock.Setup(r => r.SetIntValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()))
                     .Returns(true);
        _registryMock.Setup(r => r.DeleteValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                     .Returns(true);

        _fix = new PreferIPv4Fix(_registryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies properties return expected defaults.
    /// </summary>
    [Fact]
    public void Properties_ReturnExpectedDefaults()
    {
        Assert.Equal("PreferIPv4Fix", _fix.Id);
        Assert.Equal("Prefer IPv4", _fix.Title);
        Assert.Equal(ActionSetConstants.Categories.Multiplayer, _fix.Category);
        Assert.False(_fix.IsCoreFix);
        Assert.False(_fix.IsCrucialFix);
    }

    /// <summary>
    /// Verifies that IsAppliedAsync returns true when DisabledComponents matches expected value.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task IsAppliedAsync_WhenRegistryMatches_ReturnsTrueAsync()
    {
        _registryMock.Setup(r => r.GetIntValue(
            RegistryConstants.Tcpip6ParametersKeyPath,
            RegistryConstants.DisabledComponentsValueName,
            It.IsAny<bool>()))
            .Returns(RegistryConstants.PreferIPv4DisabledComponentsValue);

        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam) { HasGenerals = true };
        var result = await _fix.IsAppliedAsync(installation);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that IsAppliedAsync returns false when DisabledComponents is missing or 0.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task IsAppliedAsync_WhenRegistryMissing_ReturnsFalseAsync()
    {
        _registryMock.Setup(r => r.GetIntValue(
            RegistryConstants.Tcpip6ParametersKeyPath,
            RegistryConstants.DisabledComponentsValueName,
            It.IsAny<bool>()))
            .Returns((int?)null);

        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam) { HasGenerals = true };
        var result = await _fix.IsAppliedAsync(installation);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that ApplyAsync returns success when already configured.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ApplyAsync_WhenAlreadyApplied_ReturnsSuccessAsync()
    {
        _registryMock.Setup(r => r.GetIntValue(
            RegistryConstants.Tcpip6ParametersKeyPath,
            RegistryConstants.DisabledComponentsValueName,
            It.IsAny<bool>()))
            .Returns(RegistryConstants.PreferIPv4DisabledComponentsValue);

        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam) { HasGenerals = true };
        var result = await _fix.ApplyAsync(installation);

        Assert.True(result.Success);
        _registryMock.Verify(r => r.SetIntValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that UndoAsync returns success when not configured.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task UndoAsync_WhenNotConfigured_ReturnsSuccessAsync()
    {
        _registryMock.Setup(r => r.GetIntValue(
            RegistryConstants.Tcpip6ParametersKeyPath,
            RegistryConstants.DisabledComponentsValueName,
            It.IsAny<bool>()))
            .Returns((int?)null);

        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam) { HasGenerals = true };
        var result = await _fix.UndoAsync(installation);

        Assert.True(result.Success);
        _registryMock.Verify(r => r.DeleteValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
