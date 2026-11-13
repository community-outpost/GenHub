using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Infrastructure.Converters;
using Moq;
using System;
using System.Globalization;
using System.Resources;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="WorkspaceStrategyDisplayConverter"/>.
/// </summary>
public class WorkspaceStrategyDisplayConverterTests
{
    private static readonly ResourceManager Resources =
        new(LocalizationConstants.StringResourceBaseName, typeof(LocalizationService).Assembly);

    /// <summary>
    /// Gets every workspace strategy paired with every shipped culture.
    /// </summary>
    public static TheoryData<WorkspaceStrategy, string> StrategiesByCulture
    {
        get
        {
            var data = new TheoryData<WorkspaceStrategy, string>();
            foreach (var strategy in Enum.GetValues<WorkspaceStrategy>())
            {
                foreach (var culture in new[] { string.Empty, "ar", "ru" })
                {
                    data.Add(strategy, culture);
                }
            }

            return data;
        }
    }

    /// <summary>
    /// Verifies every strategy has a plain localized name and description in every shipped culture.
    /// </summary>
    /// <param name="strategy">The strategy under test.</param>
    /// <param name="cultureName">The culture to resolve resources in.</param>
    [Theory]
    [MemberData(nameof(StrategiesByCulture))]
    public void GetNameAndDescription_EveryStrategy_IsLocalizedAndNotTheEnumName(WorkspaceStrategy strategy, string cultureName)
    {
        var localization = CreateLocalization(CultureInfo.GetCultureInfo(cultureName));

        var name = WorkspaceStrategyDisplayConverter.GetName(strategy, localization);
        var description = WorkspaceStrategyDisplayConverter.GetDescription(strategy, localization, isWindows: false);

        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.NotEqual(strategy.ToString(), name);
        Assert.False(string.IsNullOrWhiteSpace(description));
    }

    /// <summary>
    /// Verifies only the default strategy is labeled as recommended.
    /// </summary>
    [Fact]
    public void GetName_OnlyDefaultStrategy_IsMarkedRecommended()
    {
        var localization = CreateLocalization(CultureInfo.InvariantCulture);

        foreach (var strategy in Enum.GetValues<WorkspaceStrategy>())
        {
            var name = WorkspaceStrategyDisplayConverter.GetName(strategy, localization);
            Assert.Equal(strategy == WorkspaceConstants.DefaultWorkspaceStrategy, name.Contains("recommended", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Verifies the administrator note appears only on Windows and only for strategies that create symbolic links.
    /// </summary>
    /// <param name="strategy">The strategy under test.</param>
    /// <param name="mayNeedElevation">Whether the strategy can need administrator rights or Developer Mode on Windows.</param>
    [Theory]
    [InlineData(WorkspaceStrategy.SymlinkOnly, true)]
    [InlineData(WorkspaceStrategy.HybridCopySymlink, true)]
    [InlineData(WorkspaceStrategy.HardLink, true)]
    [InlineData(WorkspaceStrategy.FullCopy, false)]
    public void GetDescription_AdminNote_AppearsOnlyOnWindowsForLinkingStrategies(WorkspaceStrategy strategy, bool mayNeedElevation)
    {
        var localization = CreateLocalization(CultureInfo.InvariantCulture);

        var windows = WorkspaceStrategyDisplayConverter.GetDescription(strategy, localization, isWindows: true);
        var other = WorkspaceStrategyDisplayConverter.GetDescription(strategy, localization, isWindows: false);

        Assert.DoesNotContain("administrator", other, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(mayNeedElevation, windows.Contains("administrator", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies the converter falls back to the enum name and an empty description without localization.
    /// </summary>
    [Fact]
    public void GetNameAndDescription_WithoutLocalization_FallBackSafely()
    {
        Assert.Equal(nameof(WorkspaceStrategy.HardLink), WorkspaceStrategyDisplayConverter.GetName(WorkspaceStrategy.HardLink, null));
        Assert.Equal(string.Empty, WorkspaceStrategyDisplayConverter.GetDescription(WorkspaceStrategy.HardLink, null, isWindows: true));
    }

    /// <summary>
    /// Verifies non-strategy values pass through as text.
    /// </summary>
    [Fact]
    public void Convert_WithNonStrategy_ReturnsStringForm()
    {
        Assert.Equal(string.Empty, WorkspaceStrategyDisplayConverter.Name.Convert((object?)null, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("42", WorkspaceStrategyDisplayConverter.Description.Convert(42, typeof(string), null, CultureInfo.InvariantCulture));
    }

    private static ILocalizationService CreateLocalization(CultureInfo culture)
    {
        var resourceSet = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        var mock = new Mock<ILocalizationService>();
        mock.Setup(service => service.TryGetString(It.IsAny<string>(), out It.Ref<string?>.IsAny, It.IsAny<object?[]>()))
            .Returns(new TryGetStringCallback((string key, out string? value, object?[] arguments) =>
            {
                value = resourceSet?.GetString(key) ?? string.Empty;
                return value.Length > 0;
            }));
        return mock.Object;
    }

    private delegate bool TryGetStringCallback(string key, out string? value, object?[] arguments);
}
