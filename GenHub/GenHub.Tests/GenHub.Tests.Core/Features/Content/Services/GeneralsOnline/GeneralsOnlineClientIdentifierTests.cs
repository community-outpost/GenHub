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
    /// Easy Anti-Cheat wraps only the 60Hz binary. The ordinary client binary ships alongside it
    /// as workspace content and is not a supported entry point.
    /// </summary>
    [Fact]
    public void Identify_DefaultExecutable_IsNotRecognised()
    {
        var identifier = new GeneralsOnlineClientIdentifier();
        var path = Path.Combine("C:", "GO", GameClientConstants.GeneralsOnlineDefaultExecutable);

        Assert.False(identifier.CanIdentify(path));
        Assert.Null(identifier.Identify(path));
    }
}
