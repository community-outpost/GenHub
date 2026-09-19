using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.Services.Publishers;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.Publishers;

/// <summary>
/// Unit tests for the Okladnoj and GeneralsX client identifiers.
/// </summary>
public sealed class PublisherClientIdentifierTests
{
    private readonly OkladnojClientIdentifier _okladnoj = new();
    private readonly GeneralsXClientIdentifier _generalsX = new();

    /// <summary>
    /// The Okladnoj identifier recognizes the extensionless macOS binary only.
    /// </summary>
    [Fact]
    public void Okladnoj_IdentifiesMacBinary()
    {
        Assert.True(_okladnoj.CanIdentify("/Applications/GeneralsOnlineZH"));
        Assert.False(_okladnoj.CanIdentify("/Applications/GeneralsLauncher"));
        Assert.False(_okladnoj.CanIdentify("/Games/generals.exe"));

        var identification = _okladnoj.Identify("/Applications/GeneralsOnlineZH");
        Assert.NotNull(identification);
        Assert.Equal(PublisherTypeConstants.Okladnoj, identification.PublisherId);
        Assert.Equal(GameClientConstants.OkladnojZeroHourDisplayName, identification.DisplayName);
        Assert.Equal(GameType.ZeroHour, identification.GameType);
        Assert.Null(_okladnoj.Identify("/Games/generals.exe"));
    }

    /// <summary>
    /// The GeneralsX identifier recognizes the Windows client binary only.
    /// </summary>
    [Fact]
    public void GeneralsX_IdentifiesWindowsBinary()
    {
        var clientPath = Path.Combine("Games", "GeneralsXZH.exe");
        Assert.True(_generalsX.CanIdentify(clientPath));
        Assert.False(_generalsX.CanIdentify(Path.Combine("Games", "run.sh")));
        Assert.False(_generalsX.CanIdentify(Path.Combine("Games", "generals.exe")));

        var identification = _generalsX.Identify(clientPath);
        Assert.NotNull(identification);
        Assert.Equal(PublisherTypeConstants.GeneralsX, identification.PublisherId);
        Assert.Equal(GameClientConstants.GeneralsXZeroHourDisplayName, identification.DisplayName);
        Assert.Equal(GameType.ZeroHour, identification.GameType);
        Assert.Null(_generalsX.Identify(Path.Combine("Games", "generals.exe")));
    }
}
