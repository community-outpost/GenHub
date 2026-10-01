using GenHub.Core.Constants;
using GenHub.Features.Content.Services.GeneralsOnline;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests for <see cref="GeneralsOnlineClientIdentifier"/> across the pre- and post-EAC layouts.
/// </summary>
public class GeneralsOnlineClientIdentifierTests
{
    /// <summary>
    /// The Easy Anti-Cheat bootstrapper is the supported entry point, so publisher discovery
    /// must recognise it.
    /// </summary>
    [Fact]
    public void Identify_EacLauncher_ReturnsSixtyHertzClient()
    {
        var identifier = new GeneralsOnlineClientIdentifier();
        var path = Path.Combine("C:", "GO", GameClientConstants.GeneralsOnlineEacLauncherExecutable);

        Assert.True(identifier.CanIdentify(path));

        var identification = identifier.Identify(path);

        Assert.NotNull(identification);
        Assert.Equal(GameClientConstants.GeneralsOnline60HzDisplayName, identification!.DisplayName);
    }

    /// <summary>
    /// Pre-EAC packages ship the 60Hz binary as the entry point and must still be recognised.
    /// </summary>
    [Fact]
    public void Identify_SixtyHertzExecutable_ReturnsSixtyHertzClient()
    {
        var identifier = new GeneralsOnlineClientIdentifier();
        var path = Path.Combine("C:", "GO", GameClientConstants.GeneralsOnline60HzExecutable);

        Assert.True(identifier.CanIdentify(path));
        Assert.NotNull(identifier.Identify(path));
    }

    /// <summary>
    /// The default executable (generalsonlinezh.exe) is the Test Environment game client.
    /// </summary>
    [Fact]
    public void Identify_DefaultExecutable_ReturnsTestEnvironmentClient()
    {
        var identifier = new GeneralsOnlineClientIdentifier();
        var path = Path.Combine("C:", "GO", GameClientConstants.GeneralsOnlineDefaultExecutable);

        Assert.True(identifier.CanIdentify(path));
        var identification = identifier.Identify(path);
        Assert.NotNull(identification);
        Assert.Equal(GameClientConstants.GeneralsOnlineTestEnvironmentDisplayName, identification!.DisplayName);
        Assert.Equal(GeneralsOnlineConstants.VariantTestEnvironmentSuffix, identification.Variant);
    }

    /// <summary>
    /// Unix packages ship the extensionless native client instead of any Windows launcher,
    /// so it is a supported entry point too.
    /// </summary>
    [Fact]
    public void Identify_UnixExecutable_ReturnsSixtyHertzClient()
    {
        var identifier = new GeneralsOnlineClientIdentifier();
        var path = "/Applications/Generals Online.app/Contents/MacOS/" + GameClientConstants.GeneralsOnlineUnixExecutable;

        Assert.True(identifier.CanIdentify(path));
        Assert.NotNull(identifier.Identify(path));
    }
}
