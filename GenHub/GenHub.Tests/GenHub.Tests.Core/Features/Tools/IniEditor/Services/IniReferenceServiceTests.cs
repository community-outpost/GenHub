using FluentAssertions;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Tools.IniEditor;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.IniEditor;
using GenHub.Features.Tools.IniEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.IniEditor.Services;

/// <summary>
/// Unit tests for cross-file reverse lookups in <see cref="IniReferenceService"/>.
/// </summary>
public sealed class IniReferenceServiceTests
{
    /// <summary>
    /// Verifies that folder headers are indexed for cross-file resolution.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RebuildIndexAsync_IndexesFolderHeadersAsync()
    {
        var folder = NewFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "Power.ini"), PowerIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "Airforce.ini"), BomberIni());
            var service = CreateService();

            var result = await service.RebuildIndexAsync(null, folder);

            result.Success.Should().BeTrue();
            service.Entries.Should().Contain(entry => entry.BlockType == "SpecialPower" && entry.Name == "SuperweaponChinaCarpetBomb");
            service.Entries.Should().Contain(entry => entry.BlockType == "Object" && entry.Name == "ChinaCarpetBomber");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// Verifies that reverse lookup finds document and folder referencers through nested modules.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task FindReferencersAsync_FindsDocumentAndFolderReferencersAsync()
    {
        var folder = NewFolder();
        try
        {
            var buttonPath = Path.Combine(folder, "Button.ini");
            await File.WriteAllTextAsync(buttonPath, ButtonIni());
            await File.WriteAllTextAsync(Path.Combine(folder, "Airforce.ini"), BomberIni());
            var service = CreateService();
            var document = await ParseAsync(buttonPath);

            var result = await service.RebuildIndexAsync(document, folder);
            result.Success.Should().BeTrue();
            var referencers = await service.FindReferencersAsync("SuperweaponChinaCarpetBomb");

            referencers.Success.Should().BeTrue();
            var matches = referencers.Data!;
            matches.Should().Contain(entry => entry.BlockType == "CommandButton" && entry.Name == "Command_ChinaCarpetBomb");
            matches.Should().Contain(entry => entry.BlockType == "Object" && entry.Name == "ChinaCarpetBomber");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// Verifies that an unknown name returns no referencers.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task FindReferencersAsync_UnknownName_ReturnsEmptyAsync()
    {
        var folder = NewFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "Airforce.ini"), BomberIni());
            var service = CreateService();
            await service.RebuildIndexAsync(null, folder);

            var referencers = await service.FindReferencersAsync("NoSuchBlock");

            referencers.Success.Should().BeTrue();
            referencers.Data!.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// Verifies that value token extraction keeps names and drops numerics.
    /// </summary>
    [Fact]
    public void ScanValueTokens_KeepsNamesDropsNumerics()
    {
        var tokens = IniReferenceService.ScanValueTokens("SpecialPower = SuperweaponChinaCarpetBomb\nReloadTime = 240000\n");

        tokens.Should().Contain("SuperweaponChinaCarpetBomb");
        tokens.Should().NotContain("240000");
    }

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"GenHubRefIdx{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static IniReferenceService CreateService()
    {
        var mockLocalization = new Mock<GenHub.Core.Interfaces.Common.ILocalizationService>();
        mockLocalization
            .Setup(service => service.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => key);
        var mockInstallations = new Mock<IGameInstallationService>();
        mockInstallations
            .Setup(service => service.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(new List<GameInstallation>(), TimeSpan.Zero));
        return new IniReferenceService(
            new IniDocumentService(Mock.Of<ILogger<IniDocumentService>>(), mockLocalization.Object),
            mockInstallations.Object,
            Mock.Of<IArchiveService>(),
            Mock.Of<ILogger<IniReferenceService>>());
    }

    private static async Task<IniDocument> ParseAsync(string filePath)
    {
        var mockLocalization = new Mock<GenHub.Core.Interfaces.Common.ILocalizationService>();
        mockLocalization
            .Setup(service => service.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => key);
        var service = new IniDocumentService(Mock.Of<ILogger<IniDocumentService>>(), mockLocalization.Object);
        var parsed = await service.ParseFileAsync(filePath);
        parsed.Success.Should().BeTrue();
        return parsed.Data!;
    }

    private static string ButtonIni()
    {
        return "CommandButton Command_ChinaCarpetBomb\n" +
            "  Command = SPECIAL_POWER\n" +
            "  SpecialPower = SuperweaponChinaCarpetBomb\n" +
            "End\n";
    }

    private static string PowerIni()
    {
        return "SpecialPower SuperweaponChinaCarpetBomb\n" +
            "  ReloadTime = 240000\n" +
            "End\n";
    }

    private static string BomberIni()
    {
        return "Object ChinaCarpetBomber\n" +
            "  Model = TestUnit\n" +
            "  Behavior = CarpetBombBehavior ModuleTag_Deliver\n" +
            "    SpecialPower = SuperweaponChinaCarpetBomb\n" +
            "  End\n" +
            "End\n";
    }
}
