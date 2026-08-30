using System;
using Avalonia.Controls;
using FluentAssertions;
using GenHub.Common.ViewModels;
using Xunit;

namespace GenHub.Tests.Core;

/// <summary>
/// Unit tests for <see cref="ViewLocator"/>.
/// </summary>
public sealed class ViewLocatorTests
{
    /// <summary>
    /// Verifies that Match returns true for types ending in ViewModel or inheriting from ViewModelBase.
    /// </summary>
    [Fact]
    public void Match_WhenObjectIsViewModel_ReturnsTrue()
    {
        var locator = new ViewLocator();
        var vm = new SampleTestViewModel();

        var result = locator.Match(vm);

        result.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that Match returns false for objects that are not ViewModels.
    /// </summary>
    [Fact]
    public void Match_WhenObjectIsNotViewModel_ReturnsFalse()
    {
        var locator = new ViewLocator();
        var obj = "some string";

        var result = locator.Match(obj);

        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that Match returns false for null.
    /// </summary>
    [Fact]
    public void Match_WhenObjectIsNull_ReturnsFalse()
    {
        var locator = new ViewLocator();

        var result = locator.Match(null);

        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that Build returns null when data is null.
    /// </summary>
    [Fact]
    public void Build_WhenDataIsNull_ReturnsNull()
    {
        var locator = new ViewLocator();

        var control = locator.Build(null);

        control.Should().BeNull();
    }

    /// <summary>
    /// Verifies that Build returns a TextBlock when the matching view type cannot be found.
    /// </summary>
    [Fact]
    public void Build_WhenViewNotFound_ReturnsTextBlockWithMessage()
    {
        var locator = new ViewLocator();
        var vm = new SampleNonExistentViewModel();

        var control = locator.Build(vm);

        control.Should().NotBeNull();
        control.Should().BeOfType<TextBlock>();
        var textBlock = (TextBlock)control!;
        textBlock.Text.Should().Contain("Couldn't find view");
    }

    private sealed class SampleTestViewModel : ViewModelBase
    {
    }

    private sealed class SampleNonExistentViewModel
    {
    }
}
