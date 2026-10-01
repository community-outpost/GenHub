// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using System.Numerics;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for GlobalLighting slot selection.
/// </summary>
public sealed class WbLightingServiceTests
{
    /// <summary>
    /// Verifies the active slot drives ambient, sun direction, and sun angles.
    /// </summary>
    [Fact]
    public void SelectSlot_ActiveSlot_ResolvesSun()
    {
        var lighting = new MapLightingData { TimeOfDay = 2 };
        lighting.TimesOfDay.Add(Slot(0.1f, 0, 0, 1));
        lighting.TimesOfDay.Add(Slot(0.4f, 1, 0, 0));

        var setup = WbLightingService.SelectSlot(lighting);

        Assert.Equal(0.4f, setup.Ambient.X);
        Assert.Equal(new Vector3(1, 0, 0), setup.LightDirections[0]);
        Assert.Equal(0.0f, setup.SunPitchDegrees, 3);
        Assert.Equal(0.0f, setup.SunYawDegrees, 3);
    }

    /// <summary>
    /// Verifies out-of-range time of day clamps to the available slots.
    /// </summary>
    [Fact]
    public void SelectSlot_OutOfRange_ClampsToLastSlot()
    {
        var lighting = new MapLightingData { TimeOfDay = 9 };
        lighting.TimesOfDay.Add(Slot(0.1f, 0, 0, 1));
        lighting.TimesOfDay.Add(Slot(0.7f, 0, 1, 0));

        var setup = WbLightingService.SelectSlot(lighting);

        Assert.Equal(0.7f, setup.Ambient.X);
        Assert.Equal(0.0f, setup.SunPitchDegrees, 3);
        Assert.Equal(90.0f, setup.SunYawDegrees, 3);
    }

    /// <summary>
    /// Verifies degenerate directions fall back to the noon setup.
    /// </summary>
    [Fact]
    public void SelectSlot_DegenerateLight_FallsBackToNoon()
    {
        var lighting = new MapLightingData { TimeOfDay = 1 };
        lighting.TimesOfDay.Add(Slot(0.2f, 0, 0, 0));

        var setup = WbLightingService.SelectSlot(lighting);

        Assert.Equal(0.45f, setup.Ambient.X);
        Assert.Equal(45.0f, setup.SunPitchDegrees, 3);
        Assert.Equal(45.0f, setup.SunYawDegrees, 3);
    }

    /// <summary>
    /// Verifies a map without lighting chunks still renders.
    /// </summary>
    [Fact]
    public void SelectSlot_NoSlots_FallsBackToNoon()
    {
        var setup = WbLightingService.SelectSlot(new MapLightingData());

        Assert.Single(setup.LightDirections);
        Assert.Single(setup.LightDiffuse);
    }

    private static MapTimeOfDayLighting Slot(float ambient, float x, float y, float z)
    {
        var slot = new MapTimeOfDayLighting();
        slot.TerrainLights.Add(new MapLight
        {
            AmbientR = ambient,
            AmbientG = ambient,
            AmbientB = ambient,
            DiffuseR = 1,
            DiffuseG = 1,
            DiffuseB = 1,
            PosX = x,
            PosY = y,
            PosZ = z,
        });

        return slot;
    }
}
