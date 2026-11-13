using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Infrastructure.Converters;
using System;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="LocalizedReplaySlotConverter"/>.
/// </summary>
public class LocalizedReplaySlotConverterTests
{
    private readonly LocalizedReplaySlotConverter _converter = LocalizedReplaySlotConverter.Instance;
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Verifies that null input returns an empty string.
    /// </summary>
    [Fact]
    public void Convert_WithNull_ReturnsEmptyString()
    {
        var result = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that non-ReplaySlotInfo values return their ToString() result.
    /// </summary>
    [Fact]
    public void Convert_WithNonSlotInfo_ReturnsToString()
    {
        var result = _converter.Convert(42, typeof(string), null, _culture);
        Assert.Equal("42", result);
    }

    /// <summary>
    /// Verifies that human slots format correctly with 1-based indexing.
    /// </summary>
    [Fact]
    public void Convert_WithHumanSlot_ReturnsExpectedLabel()
    {
        var slot = new ReplaySlotInfo(0, "GeneralZh", true, 1, 2);
        var result = _converter.Convert(slot, typeof(string), null, _culture) as string;

        Assert.NotNull(result);
        Assert.Contains("Slot 1: GeneralZh", result);
    }

    /// <summary>
    /// Verifies that AI slots without existing AI indicator append the AI indicator and format correctly.
    /// </summary>
    [Fact]
    public void Convert_WithAiSlot_ReturnsExpectedLabelWithAiTag()
    {
        var slot = new ReplaySlotInfo(2, "Easy Bot", false, 0, 1);
        var result = _converter.Convert(slot, typeof(string), null, _culture) as string;

        Assert.NotNull(result);
        Assert.Contains("Slot 3: Easy Bot", result);
        Assert.Contains("(AI)", result);
    }

    /// <summary>
    /// Verifies that AI slots already having an AI prefix/tag do not duplicate the AI indicator.
    /// </summary>
    [Fact]
    public void Convert_WithPreTaggedAiSlot_DoesNotDuplicateAiTag()
    {
        var slot = new ReplaySlotInfo(2, "AI (Hard)", false, 0, 1);
        var result = _converter.Convert(slot, typeof(string), null, _culture) as string;

        Assert.NotNull(result);
        Assert.Equal("Slot 3: AI (Hard)", result);
    }

    /// <summary>
    /// Verifies that ConvertBack throws NotSupportedException.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack("Slot 1", typeof(object), null, _culture));
    }
}
