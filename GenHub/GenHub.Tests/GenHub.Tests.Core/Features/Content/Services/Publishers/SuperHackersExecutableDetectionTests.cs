using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Content.Services.Publishers;

/// <summary>Verifies platform preference and native client identification.</summary>
public class SuperHackersExecutableDetectionTests
{
    /// <summary>File creation order cannot change the selected launch target.</summary>
    /// <param name="nativeFirst">Whether the native file is created first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetectGameExecutables_MixedForms_PrefersCurrentPlatform(bool nativeFirst)
    {
        var root = Directory.CreateTempSubdirectory("GenHub.SuperHackers.").FullName;
        try
        {
            var windows = Path.Combine(root, GameClientConstants.SuperHackersGeneralsExecutable);
            var native = Path.Combine(root, Path.GetFileNameWithoutExtension(windows));
            foreach (var path in nativeFirst ? new[] { native, windows } : new[] { windows, native })
            {
                File.WriteAllBytes(path, [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]);
            }

            var factory = new SuperHackersManifestFactory(
                NullLogger<SuperHackersManifestFactory>.Instance, Mock.Of<IFileHashProvider>());
            var detected = factory.DetectGameExecutables(root, CancellationToken.None);
            Assert.Equal(OperatingSystem.IsWindows() ? windows : native, detected[GameType.Generals]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>A cancelled scan does not start enumerating content.</summary>
    [Fact]
    public void DetectGameExecutables_Cancelled_ThrowsCancellation()
    {
        var factory = new SuperHackersManifestFactory(
            NullLogger<SuperHackersManifestFactory>.Instance, Mock.Of<IFileHashProvider>());
        Assert.Throws<OperationCanceledException>(() => factory.DetectGameExecutables(
            Path.GetTempPath(), new CancellationToken(true)));
    }

    /// <summary>Both executable forms identify the correct game.</summary>
    /// <param name="windowsName">The known Windows executable name.</param>
    /// <param name="gameType">The expected game.</param>
    [Theory]
    [InlineData(GameClientConstants.SuperHackersGeneralsExecutable, GameType.Generals)]
    [InlineData(GameClientConstants.SuperHackersZeroHourExecutable, GameType.ZeroHour)]
    public void Identify_WindowsAndNativeForms_ReturnsGame(string windowsName, GameType gameType)
    {
        var identifier = new SuperHackersClientIdentifier();
        foreach (var name in new[] { windowsName, Path.GetFileNameWithoutExtension(windowsName) })
        {
            Assert.True(identifier.CanIdentify(name));
            Assert.Equal(gameType, identifier.Identify(name)!.GameType);
        }

        Assert.Null(identifier.Identify("unrelated"));
    }
}
