using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Features.Tools.GenHotkeys.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="TechTreeService"/> layout consolidation and deduplication.
/// </summary>
public class TechTreeServiceTests
{
    private readonly TechTreeService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="TechTreeServiceTests"/> class.
    /// </summary>
    public TechTreeServiceTests()
    {
        var mockLogger = new Mock<ILogger<TechTreeService>>();
        _service = new TechTreeService(mockLogger.Object);
    }

    /// <summary>
    /// Verifies that multi-layout Chinese buildings with upgrade variations (e.g. Land Mines vs Neutron Mines)
    /// are consolidated into a single layout without duplicate actions.
    /// </summary>
    /// <param name="factionShortName">The faction short name.</param>
    /// <param name="buildingName">The building name.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("PRC", "PRCNuclearReactor")]
    [InlineData("PRC", "PRCWarFactory")]
    [InlineData("INF", "PRCWarFactory")]
    [InlineData("NUK", "PRCAdvancedNuclearReactor")]
    [InlineData("TNK", "PRCNuclearReactor")]
    public async Task ChineseBuildings_ConsolidatedIntoSingleLayoutAsync(string factionShortName, string buildingName)
    {
        var factions = await _service.LoadTechTreeAsync(GameType.ZeroHour);
        var faction = factions.FirstOrDefault(f => f.ShortName == factionShortName);
        Assert.NotNull(faction);

        var building = faction.GameObjects.FirstOrDefault(b => b.Name == buildingName);
        Assert.NotNull(building);

        // Should be consolidated into 1 layout (no duplicate layout boxes)
        Assert.Single(building.KeyboardLayouts);

        var layout = building.KeyboardLayouts[0];

        // Should not have any duplicate actions (every action unique by IconName or HotkeyString)
        var duplicates = layout
            .GroupBy(a => a.HotkeyString ?? a.IconName)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);

        // Both Land Mines and Neutron Mines must be present
        Assert.Contains(layout, a => a.IconName == "PRCLandMine" || a.HotkeyString == "CONTROLBAR:UpgradeChinaMines");
        Assert.Contains(layout, a => a.IconName == "PRCNeutronMines" || a.HotkeyString == "CONTROLBAR:UpgradeEMPMines");
    }

    /// <summary>
    /// Verifies that GLAWorker legitimately preserves 2 separate layout pages (Real vs Fake structures).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GlaWorker_RetainsTwoDistinctLayoutsAsync()
    {
        var factions = await _service.LoadTechTreeAsync(GameType.ZeroHour);
        var gla = factions.FirstOrDefault(f => f.ShortName == "GLA");
        Assert.NotNull(gla);

        var worker = gla.GameObjects.FirstOrDefault(o => o.Name == "GLAWorker");
        Assert.NotNull(worker);

        // GLAWorker is a 2-page builder (Page 1 = Real, Page 2 = Fake) and must retain 2 layouts
        Assert.Equal(2, worker.KeyboardLayouts.Count);
    }
}
