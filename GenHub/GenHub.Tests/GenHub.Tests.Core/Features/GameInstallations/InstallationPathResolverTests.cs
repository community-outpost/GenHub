using System.Reflection;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Features.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.GameInstallations;

/// <summary>Tests archive-based recovery of moved native installations.</summary>
public class InstallationPathResolverTests
{
    /// <summary>Recovery searches accept native archives and require the requested games.</summary>
    /// <returns>The async task.</returns>
    [Theory]
    [InlineData(false, true, "INIZH.big", true)]
    [InlineData(true, false, "INI.big", true)]
    [InlineData(true, true, "INIZH.big", false)]
    [InlineData(false, true, "ControlBarProZH.big", false)]
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
}
