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
/// Tests for the <see cref="EAAppRegistryFix"/> class.
/// </summary>
public class EAAppRegistryFixTests
{
    private readonly Mock<IRegistryService> _registryMock;
    private readonly Mock<ILogger<EAAppRegistryFix>> _loggerMock;
    private readonly EAAppRegistryFix _fix;

    /// <summary>
    /// Initializes a new instance of the <see cref="EAAppRegistryFixTests"/> class.
    /// </summary>
    public EAAppRegistryFixTests()
    {
        _registryMock = new Mock<IRegistryService>();
        _registryMock.Setup(r => r.IsRunningAsAdministrator()).Returns(true);

        // Mock Set operations to return true (success)
        _registryMock.Setup(r => r.SetStringValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                     .Returns(true);
        _registryMock.Setup(r => r.SetIntValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()))
                     .Returns(true);

        _loggerMock = new Mock<ILogger<EAAppRegistryFix>>();
        _fix = new EAAppRegistryFix(_registryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that IsApplicableAsync returns true when Generals registry keys are missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsApplicable_ReturnsTrue_WhenGeneralsKeysMissingAsync()
    {
        var installation = new GameInstallation("C:\\Games", GameInstallationType.EaApp)
        {
            GeneralsPath = "C:\\Games\\Generals",
            ZeroHourPath = "C:\\Games\\Zero Hour",
            HasGenerals = true,
            HasZeroHour = true,
        };

        // Mock Registry: Any call to GetStringValue for Install Path returns null (missing)
        _registryMock.Setup(r => r.GetStringValue(It.IsAny<string>(), RegistryConstants.InstallPathValueName, It.IsAny<bool>()))
                     .Returns((string?)null);

        var result = await _fix.IsApplicableAsync(installation);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that IsApplicableAsync returns true when ergc registry keys are missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsApplicable_ReturnsTrue_WhenErgcMissingAsync()
    {
        var installation = new GameInstallation("C:\\Games", GameInstallationType.EaApp)
        {
            GeneralsPath = "C:\\Games\\Generals",
            ZeroHourPath = "C:\\Games\\Zero Hour",
            HasGenerals = true,
            HasZeroHour = true,
        };

        // Mock returns correct paths
        _registryMock.Setup(r => r.GetStringValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.InstallPathValueName, It.IsAny<bool>()))
                     .Returns(installation.GeneralsPath);
        _registryMock.Setup(r => r.GetIntValue(RegistryConstants.EAAppGeneralsKeyPath, "Version", It.IsAny<bool>()))
                     .Returns(65544); // 1.08

        // Mock zero hour correct
        _registryMock.Setup(r => r.GetStringValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.InstallPathValueName, It.IsAny<bool>()))
                     .Returns(installation.ZeroHourPath);
        _registryMock.Setup(r => r.GetIntValue(RegistryConstants.EAAppZeroHourKeyPath, "Version", It.IsAny<bool>()))
                     .Returns(65540); // 1.04

        // Ergc missing (returns empty or null)
        _registryMock.Setup(r => r.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty, It.IsAny<bool>()))
                     .Returns(string.Empty);

        var result = await _fix.IsApplicableAsync(installation);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that ApplyAsync sets the correct registry keys.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Apply_SetsRegistryKeysAsync()
    {
        var installation = new GameInstallation("C:\\Games", GameInstallationType.EaApp)
        {
            GeneralsPath = "C:\\Games\\Generals",
            ZeroHourPath = "C:\\Games\\Zero Hour",
            HasGenerals = true,
            HasZeroHour = true,
        };

        var result = await _fix.ApplyAsync(installation);

        Assert.True(result.Success);

        // Verify installs - Verify SET usage
        _registryMock.Verify(r => r.SetStringValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.InstallPathValueName, installation.GeneralsPath, It.IsAny<bool>()), Times.Once);
        _registryMock.Verify(r => r.SetIntValue(RegistryConstants.EAAppGeneralsKeyPath, "Version", RegistryConstants.GeneralsVersionDWord, It.IsAny<bool>()), Times.Once);

        // Verify serials logic - should attempt to write if missing (default mock returns null/empty so logic thinks it's missing)
        _registryMock.Verify(r => r.SetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty, It.IsAny<string>(), It.IsAny<bool>()), Times.AtLeast(1));
    }

    /// <summary>
    /// Verifies that IsApplicableAsync returns false when all registry keys and serials are already correct.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsApplicable_ReturnsFalse_WhenAllKeysValidAsync()
    {
        var installation = new GameInstallation("C:\\Games", GameInstallationType.EaApp)
        {
            GeneralsPath = "C:\\Games\\Generals",
            ZeroHourPath = "C:\\Games\\Zero Hour",
            HasGenerals = true,
            HasZeroHour = true,
        };

        _registryMock.Setup(r => r.GetStringValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.InstallPathValueName, It.IsAny<bool>()))
                     .Returns(installation.GeneralsPath);
        _registryMock.Setup(r => r.GetIntValue(RegistryConstants.EAAppGeneralsKeyPath, "Version", It.IsAny<bool>()))
                     .Returns(RegistryConstants.GeneralsVersionDWord);
        _registryMock.Setup(r => r.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty, It.IsAny<bool>()))
                     .Returns("VALIDSERIAL12345678");

        _registryMock.Setup(r => r.GetStringValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.InstallPathValueName, It.IsAny<bool>()))
                     .Returns(installation.ZeroHourPath);
        _registryMock.Setup(r => r.GetIntValue(RegistryConstants.EAAppZeroHourKeyPath, "Version", It.IsAny<bool>()))
                     .Returns(RegistryConstants.ZeroHourVersionDWord);
        _registryMock.Setup(r => r.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty, It.IsAny<bool>()))
                     .Returns("VALIDSERIAL87654321");

        var result = await _fix.IsApplicableAsync(installation);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that IsApplicableAsync returns false when installation type is not EA App or Unknown.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsApplicable_ReturnsFalse_WhenNotEaAppInstallationAsync()
    {
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam)
        {
            GeneralsPath = "C:\\Games\\Generals",
            HasGenerals = true,
        };

        var result = await _fix.IsApplicableAsync(installation);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that IsAppliedAsync returns true when all keys are present and valid, and false otherwise.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsApplied_ReturnsTrue_WhenAllKeysValid_AndFalseWhenMissingAsync()
    {
        var installation = new GameInstallation("C:\\Games", GameInstallationType.EaApp)
        {
            GeneralsPath = "C:\\Games\\Generals",
            HasGenerals = true,
            HasZeroHour = false,
        };

        _registryMock.Setup(r => r.GetStringValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.InstallPathValueName, It.IsAny<bool>()))
                     .Returns(installation.GeneralsPath);
        _registryMock.Setup(r => r.GetIntValue(RegistryConstants.EAAppGeneralsKeyPath, "Version", It.IsAny<bool>()))
                     .Returns(RegistryConstants.GeneralsVersionDWord);
        _registryMock.Setup(r => r.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty, It.IsAny<bool>()))
                     .Returns("VALIDSERIAL");

        var appliedResult = await _fix.IsAppliedAsync(installation);
        Assert.True(appliedResult);

        // Missing serial
        _registryMock.Setup(r => r.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty, It.IsAny<bool>()))
                     .Returns((string?)null);

        var unappliedResult = await _fix.IsAppliedAsync(installation);
        Assert.False(unappliedResult);
    }

    /// <summary>
    /// Verifies that ApplyAsync returns failure when setting registry keys fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Apply_ReturnsFailure_WhenSetStringValueFailsAsync()
    {
        var installation = new GameInstallation("C:\\Games", GameInstallationType.EaApp)
        {
            GeneralsPath = "C:\\Games\\Generals",
            HasGenerals = true,
            HasZeroHour = false,
        };

        _registryMock.Setup(r => r.SetStringValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                     .Returns(false);

        var result = await _fix.ApplyAsync(installation);

        Assert.False(result.Success);
        Assert.Contains("Failed to write", result.ErrorMessage);
    }
}
