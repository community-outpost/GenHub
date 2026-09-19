using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Features.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace GenHub.Tests.Core.Features.GameInstallations;

/// <summary>Tests archive-based recovery of moved native installations.</summary>
public class InstallationPathResolverArchiveTests
{
    /// <summary>Recovery searches accept native archives and require the requested games.</summary>
    /// <param name="generals">Whether Generals archives are required.</param>
    /// <param name="zeroHour">Whether Zero Hour archives are required.</param>
    /// <param name="archive">The archive available at the candidate path.</param>
    /// <param name="expected">Whether the candidate should match.</param>
    /// <returns>The async task.</returns>
    [Theory]
    [InlineData(false, true, "INIZH.big", true)]
    [InlineData(true, false, "INI.big", true)]
    [InlineData(true, true, "INIZH.big", false)]
    [InlineData(false, true, "ControlBarProZH.big", true)]
    public async Task SearchCandidate_UsesRetailArchivesAsync(bool generals, bool zeroHour, string archive, bool expected)
    {
        var directory = Directory.CreateTempSubdirectory("GenHub.Recovery.");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, archive), "archive");
            var installation = new GameInstallation("missing", GameInstallationType.Retail)
            {
                HasGenerals = generals, HasZeroHour = zeroHour,
            };
            var resolver = new InstallationPathResolver(NullLogger<InstallationPathResolver>.Instance);
            var method = typeof(InstallationPathResolver).GetMethod("IsValidGameInstallationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var valid = await (Task<bool>)method.Invoke(resolver, [directory.FullName, installation, null, CancellationToken.None])!;
            Assert.Equal(expected, valid);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    /// <summary>Every flagged game must retain its archives before recovery can be skipped.</summary>
    /// <param name="remainingArchive">The archive remaining after one game was removed.</param>
    /// <returns>The async task.</returns>
    [Theory]
    [InlineData("INI.big")]
    [InlineData("INIZH.big")]
    public async Task ValidateCombinedInstallation_MissingOneGame_RequiresRecoveryAsync(string remainingArchive)
    {
        var directory = Directory.CreateTempSubdirectory("GenHub.Validation.");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, remainingArchive), "archive");
            var installation = new GameInstallation(directory.FullName, GameInstallationType.Retail)
            {
                HasGenerals = true,
                HasZeroHour = true,
                GeneralsPath = directory.FullName,
                ZeroHourPath = directory.FullName,
            };
            var resolver = new InstallationPathResolver(NullLogger<InstallationPathResolver>.Instance);
            var result = await resolver.ValidateInstallationPathAsync(installation);
            Assert.True(result.Success);
            Assert.False(result.Data);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
