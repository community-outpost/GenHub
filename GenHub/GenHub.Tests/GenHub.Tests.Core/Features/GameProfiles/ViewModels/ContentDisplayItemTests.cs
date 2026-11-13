using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using System.Collections.Generic;
using Xunit;
using CoreItem = GenHub.Core.Models.Content.ContentDisplayItem;
using VmItem = GenHub.Features.GameProfiles.ViewModels.ContentDisplayItem;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests for DisplayVersion and HasDisplayVersion properties on both Core and ViewModel ContentDisplayItem models.
/// </summary>
public class ContentDisplayItemTests
{
    /// <summary>
    /// Verifies that Core ContentDisplayItem correctly formats or suppresses DisplayVersion based on version values.
    /// </summary>
    /// <param name="inputVersion">The input raw version string.</param>
    /// <param name="expectedDisplayVersion">The expected formatted display version.</param>
    /// <param name="expectedHasDisplayVersion">The expected value of HasDisplayVersion.</param>
    [Theory]
    [InlineData("1.04", "v1.04", true)]
    [InlineData("1.06", "v1.06", true)]
    [InlineData("v2.1", "v2.1", true)]
    [InlineData("V3.0", "V3.0", true)]
    [InlineData("2026-08-21", "v2026-08-21", true)]
    [InlineData("0", null, false)]
    [InlineData("0.0", null, false)]
    [InlineData("0.00", null, false)]
    [InlineData("1", null, false)]
    [InlineData("1.0", null, false)]
    [InlineData("1.00", null, false)]
    [InlineData("1.000", null, false)]
    [InlineData("1.0.0", null, false)]
    [InlineData("v1.0", null, false)]
    [InlineData("Unknown", null, false)]
    [InlineData("Auto-Updated", null, false)]
    [InlineData("", null, false)]
    [InlineData(null, null, false)]
    public void CoreContentDisplayItem_DisplayVersion_FormatsOrSuppressesCorrectly(
        string? inputVersion,
        string? expectedDisplayVersion,
        bool expectedHasDisplayVersion)
    {
        var item = new CoreItem
        {
            Id = "test-id",
            DisplayName = "Test Mod",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            Version = inputVersion,
        };

        Assert.Equal(expectedDisplayVersion, item.DisplayVersion);
        Assert.Equal(expectedHasDisplayVersion, item.HasDisplayVersion);
    }

    /// <summary>
    /// Verifies that ViewModel ContentDisplayItem correctly formats or suppresses DisplayVersion based on version values.
    /// </summary>
    /// <param name="inputVersion">The input raw version string.</param>
    /// <param name="expectedDisplayVersion">The expected formatted display version.</param>
    /// <param name="expectedHasDisplayVersion">The expected value of HasDisplayVersion.</param>
    [Theory]
    [InlineData("1.04", "v1.04", true)]
    [InlineData("1.06", "v1.06", true)]
    [InlineData("v2.1", "v2.1", true)]
    [InlineData("V3.0", "V3.0", true)]
    [InlineData("0", null, false)]
    [InlineData("1.0", null, false)]
    [InlineData("1.00", null, false)]
    [InlineData("Unknown", null, false)]
    [InlineData("", null, false)]
    [InlineData(null, null, false)]
    public void VmContentDisplayItem_DisplayVersion_FormatsOrSuppressesCorrectly(
        string? inputVersion,
        string? expectedDisplayVersion,
        bool expectedHasDisplayVersion)
    {
        var item = new VmItem
        {
            Id = "test-id",
            DisplayName = "Test Mod",
            ManifestId = new ManifestId("mod-test"),
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Custom,
            Version = inputVersion,
        };

        Assert.Equal(expectedDisplayVersion, item.DisplayVersion);
        Assert.Equal(expectedHasDisplayVersion, item.HasDisplayVersion);
    }

    /// <summary>
    /// Verifies that mutating Version on ViewModel ContentDisplayItem raises PropertyChanged for DisplayVersion and HasDisplayVersion.
    /// </summary>
    [Fact]
    public void VmContentDisplayItem_ChangingVersion_RaisesPropertyChangedForDisplayVersionProperties()
    {
        var item = new VmItem
        {
            Id = "test-id",
            DisplayName = "Test Mod",
            ManifestId = new ManifestId("mod-test"),
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Custom,
            Version = "1.0",
        };

        var propertyChangedNames = new List<string>();
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
            {
                propertyChangedNames.Add(e.PropertyName);
            }
        };

        item.Version = "1.06";

        Assert.Contains(nameof(VmItem.Version), propertyChangedNames);
        Assert.Contains(nameof(VmItem.DisplayVersion), propertyChangedNames);
        Assert.Contains(nameof(VmItem.HasDisplayVersion), propertyChangedNames);
        Assert.Equal("v1.06", item.DisplayVersion);
        Assert.True(item.HasDisplayVersion);
    }
}
