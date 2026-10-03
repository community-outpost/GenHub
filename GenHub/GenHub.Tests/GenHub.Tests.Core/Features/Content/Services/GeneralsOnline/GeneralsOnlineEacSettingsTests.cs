using GenHub.Features.Content.Services.GeneralsOnline;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests for <see cref="GeneralsOnlineEacSettings"/>, the package-local source of truth
/// for which binary the Easy Anti-Cheat bootstrapper starts.
/// </summary>
public class GeneralsOnlineEacSettingsTests : IDisposable
{
    private readonly string _packageDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineEacSettingsTests"/> class.
    /// </summary>
    public GeneralsOnlineEacSettingsTests()
    {
        _packageDirectory = Path.Combine(Path.GetTempPath(), $"genhub-eac-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_packageDirectory, "EasyAntiCheat"));
    }

    /// <summary>
    /// Valid settings yield the wrapped binary and the product ID.
    /// </summary>
    [Fact]
    public void TryRead_ValidSettings_ReturnsExecutableAndProductId()
    {
        WriteSettings("""{"executable": "GeneralsOnlineZH_TestEnvironment.exe", "productid": "5ae7777a6a924ddd846336c99dd1ace3"}""");

        var read = GeneralsOnlineEacSettings.TryRead(_packageDirectory, out var settings);

        Assert.True(read);
        Assert.NotNull(settings);
        Assert.Equal("GeneralsOnlineZH_TestEnvironment.exe", settings!.Executable);
        Assert.Equal("5ae7777a6a924ddd846336c99dd1ace3", settings.ProductId);
    }

    /// <summary>
    /// The product ID is optional: older settings name only the binary.
    /// </summary>
    [Fact]
    public void TryRead_SettingsWithoutProductId_ReturnsNullProductId()
    {
        WriteSettings("""{"executable": "generalsonlinezh_60.exe"}""");

        var read = GeneralsOnlineEacSettings.TryRead(_packageDirectory, out var settings);

        Assert.True(read);
        Assert.NotNull(settings);
        Assert.Equal("generalsonlinezh_60.exe", settings!.Executable);
        Assert.Null(settings.ProductId);
    }

    /// <summary>
    /// A missing or blank settings file reads as absent.
    /// </summary>
    /// <param name="contents">The file contents, or null to write no file.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryRead_PackageWithoutSettings_ReturnsFalse(string? contents)
    {
        if (contents is not null)
        {
            WriteSettings(contents);
        }

        Assert.False(GeneralsOnlineEacSettings.TryRead(_packageDirectory, out var settings));
        Assert.Null(settings);
    }

    /// <summary>
    /// Malformed settings, or settings without a usable executable, read as absent.
    /// </summary>
    /// <param name="contents">The file contents.</param>
    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"productid": "abc"}""")]
    [InlineData("""{"executable": ""}""")]
    [InlineData("""{"executable": "   "}""")]
    [InlineData("""{"executable": 42}""")]
    public void TryRead_UnusableSettings_ReturnsFalse(string contents)
    {
        WriteSettings(contents);

        Assert.False(GeneralsOnlineEacSettings.TryRead(_packageDirectory, out var settings));
        Assert.Null(settings);
    }

    /// <summary>
    /// A null package root reads as absent.
    /// </summary>
    [Fact]
    public void TryRead_NullPackageRoot_ReturnsFalse()
    {
        Assert.False(GeneralsOnlineEacSettings.TryRead(null, out var settings));
        Assert.Null(settings);
    }

    /// <summary>
    /// Normalizes executable paths with forward or backslashes to just the file name.
    /// </summary>
    /// <param name="input">The raw executable path input.</param>
    /// <param name="expected">The expected normalized file name.</param>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("generalsonlinezh_60.exe", "generalsonlinezh_60.exe")]
    [InlineData(@"bin\generalsonlinezh_60.exe", "generalsonlinezh_60.exe")]
    [InlineData("bin/generalsonlinezh_60.exe", "generalsonlinezh_60.exe")]
    [InlineData(@"C:\Games\ZeroHour\generalsonlinezh_60.exe", "generalsonlinezh_60.exe")]
    [InlineData("  bin/generalsonlinezh_60.exe  ", "generalsonlinezh_60.exe")]
    public void NormalizeExecutableName_ReturnsNormalizedFileName(string? input, string expected)
    {
        var result = GeneralsOnlineEacSettings.NormalizeExecutableName(input);
        Assert.Equal(expected, result);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_packageDirectory))
        {
            Directory.Delete(_packageDirectory, recursive: true);
        }
    }

    private void WriteSettings(string contents)
    {
        var path = Path.Combine(_packageDirectory, "EasyAntiCheat", "Settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
