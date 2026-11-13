using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Info;
using GenHub.Features.Info.ViewModels;
using Moq;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for <see cref="InfoCardViewModel"/>.
/// </summary>
public class InfoCardViewModelTests
{
    private readonly Mock<ILocalizationService> _localizationServiceMock = new();

    /// <summary>
    /// Tests that an action resolves its localized label using ActionId when present in the catalog.
    /// </summary>
    [Fact]
    public void UpdateLocalizedContent_ResolvesActionLabel_UsingActionIdWhenPresent()
    {
        string? scanNowId = "Scan Now (ID)";
        _localizationServiceMock
            .Setup(l => l.TryGetString("Info.Card.quickstart.step1.Action.NAV_scan", out scanNowId, It.IsAny<object[]>()))
            .Returns(true);
        _localizationServiceMock
            .Setup(l => l.GetString("Info.Card.quickstart.step1.Action.NAV_scan", It.IsAny<object[]>()))
            .Returns("Scan Now (ID)");

        var card = new InfoCard
        {
            Id = "step1",
            Title = "Step 1",
            Content = "Content",
            Actions =
            [
                new InfoAction { ActionId = "NAV_scan", Label = "Default Model Label" },
            ],
        };

        var vm = new InfoCardViewModel(card, "quickstart", _localizationServiceMock.Object);

        vm.Actions.Should().HaveCount(1);
        vm.Actions[0].Label.Should().Be("Scan Now (ID)");
    }

    /// <summary>
    /// Tests that an action falls back to index-based key lookup when ActionId key is missing in the catalog.
    /// </summary>
    [Fact]
    public void UpdateLocalizedContent_FallsBackToIndex_WhenActionIdMissingInCatalog()
    {
        string? nullString = null;
        _localizationServiceMock
            .Setup(l => l.TryGetString("Info.Card.quickstart.step1.Action.NAV_scan", out nullString, It.IsAny<object[]>()))
            .Returns(false);
        _localizationServiceMock
            .Setup(l => l.GetString("Info.Card.quickstart.step1.Action.NAV_scan", It.IsAny<object[]>()))
            .Returns("Info.Card.quickstart.step1.Action.NAV_scan"); // Key echoed back on miss

        string? index0 = "Scan Now (Index 0)";
        _localizationServiceMock
            .Setup(l => l.TryGetString("Info.Card.quickstart.step1.Action.0", out index0, It.IsAny<object[]>()))
            .Returns(true);
        _localizationServiceMock
            .Setup(l => l.GetString("Info.Card.quickstart.step1.Action.0", It.IsAny<object[]>()))
            .Returns("Scan Now (Index 0)");

        var card = new InfoCard
        {
            Id = "step1",
            Title = "Step 1",
            Content = "Content",
            Actions =
            [
                new InfoAction { ActionId = "NAV_scan", Label = "Default Model Label" },
            ],
        };

        var vm = new InfoCardViewModel(card, "quickstart", _localizationServiceMock.Object);

        vm.Actions.Should().HaveCount(1);
        vm.Actions[0].Label.Should().Be("Scan Now (Index 0)");
    }

    /// <summary>
    /// Tests that an action falls back to the model label when both ActionId and index keys miss in the catalog.
    /// </summary>
    [Fact]
    public void UpdateLocalizedContent_FallsBackToModelLabel_WhenBothKeysMissing()
    {
        string? nullString = null;
        _localizationServiceMock
            .Setup(l => l.TryGetString(It.IsAny<string>(), out nullString, It.IsAny<object[]>()))
            .Returns(false);
        _localizationServiceMock
            .Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns<string, object[]?>((key, _) => key); // Always echoes key

        var card = new InfoCard
        {
            Id = "step1",
            Title = "Step 1",
            Content = "Content",
            Actions =
            [
                new InfoAction { ActionId = "NAV_unknown", Label = "Default Model Label" },
            ],
        };

        var vm = new InfoCardViewModel(card, "quickstart", _localizationServiceMock.Object);

        vm.Actions.Should().HaveCount(1);
        vm.Actions[0].Label.Should().Be("Default Model Label");
    }

    /// <summary>
    /// Tests that ToggleExpansion toggles IsExpanded when IsExpandable is true.
    /// </summary>
    [Fact]
    public void ToggleExpansion_TogglesIsExpanded_WhenIsExpandable()
    {
        var card = new InfoCard
        {
            Id = "expandable-step",
            Title = "Expandable",
            Content = "Content",
            IsExpandable = true,
        };

        var vm = new InfoCardViewModel(card, "quickstart", _localizationServiceMock.Object);

        vm.IsExpanded.Should().BeFalse();
        vm.ToggleExpansionCommand.Execute(null);
        vm.IsExpanded.Should().BeTrue();
        vm.ToggleExpansionCommand.Execute(null);
        vm.IsExpanded.Should().BeFalse();
    }

    /// <summary>
    /// Tests that IconKind returns expected default icon when CustomIconKind is not set.
    /// </summary>
    [Fact]
    public void IconKind_ReturnsDefaultBasedOnType_WhenCustomIconKindNotSet()
    {
        var card = new InfoCard { Id = "c1", Title = "Card 1", Type = InfoCardType.Warning };
        var vm = new InfoCardViewModel(card, "sec1");
        vm.IconKind.Should().Be(Material.Icons.MaterialIconKind.AlertCircleOutline);
    }

    /// <summary>
    /// Tests that CustomIconKind overrides the default icon.
    /// </summary>
    [Fact]
    public void IconKind_UsesCustomIconKind_WhenSet()
    {
        var card = new InfoCard { Id = "c1", Title = "Card 1", Type = InfoCardType.HowTo };
        var vm = new InfoCardViewModel(card, "sec1")
        {
            CustomIconKind = Material.Icons.MaterialIconKind.TagOutline,
        };
        vm.IconKind.Should().Be(Material.Icons.MaterialIconKind.TagOutline);
    }

    /// <summary>
    /// Tests that TargetItem can be set and retrieved.
    /// </summary>
    [Fact]
    public void TargetItem_CanBeAssignedAndRetrieved()
    {
        var card = new InfoCard { Id = "c1", Title = "Card 1" };
        var vm = new InfoCardViewModel(card, "sec1");
        var target = new object();
        vm.TargetItem = target;
        vm.TargetItem.Should().BeSameAs(target);
    }
}
