using System.Globalization;
using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Tests.Core.Infrastructure.DependencyInjection;

/// <summary>
/// Unit tests verifying DI registration and live culture resolution in <see cref="LocalizationModule"/>.
/// </summary>
public class LocalizationModuleTests
{
    /// <summary>
    /// Verifies that localization services are registered as singletons and implement required contracts.
    /// </summary>
    [Fact]
    public void AddLocalizationServices_RegistersSingletonAndContract()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalizationServices();

        using var provider = services.BuildServiceProvider();
        var service1 = provider.GetService<ILocalizationService>();
        var service2 = provider.GetService<ILocalizationService>();

        Assert.NotNull(service1);
        Assert.Same(service1, service2);
    }

    /// <summary>
    /// Verifies that switching to Russian resolves localized strings from the Russian satellite assembly.
    /// </summary>
    [Fact]
    public void AddLocalizationServices_ResolvesRussianStrings()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalizationServices();

        using var provider = services.BuildServiceProvider();
        var localizationService = provider.GetRequiredService<ILocalizationService>();

        var switchResult = localizationService.SetCulture(CultureInfo.GetCultureInfo("ru"));
        Assert.True(switchResult.Success);
        Assert.Equal("ru", localizationService.CurrentCulture.Name);

        Assert.Equal("Внешний вид", localizationService["Settings.Section.Appearance"]);
        Assert.Equal("Цветовая тема оформления", localizationService["Settings.Appearance.Theme.Label"]);
        Assert.Equal("Выберите цветовую тему оформления для кнопок, вкладок, выделений, подсветки и полос прокрутки в GenHub.", localizationService["Settings.Appearance.Theme.Description"]);
        Assert.Equal("Активная тема:", localizationService["Settings.Appearance.Theme.ActiveTheme"]);
        Assert.Equal("Уведомления", localizationService["Notifications.Title"]);
    }

    /// <summary>
    /// Verifies that switching to Arabic resolves localized strings from the Arabic satellite assembly.
    /// </summary>
    [Fact]
    public void AddLocalizationServices_ResolvesArabicStrings()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalizationServices();

        using var provider = services.BuildServiceProvider();
        var localizationService = provider.GetRequiredService<ILocalizationService>();

        var switchResult = localizationService.SetCulture(CultureInfo.GetCultureInfo("ar"));
        Assert.True(switchResult.Success);
        Assert.Equal("ar", localizationService.CurrentCulture.Name);

        Assert.Equal("المظهر", localizationService["Settings.Section.Appearance"]);
        Assert.Equal("سمة لون التمييز", localizationService["Settings.Appearance.Theme.Label"]);
        Assert.Equal("اختر سمة لون تمييز للأزرار وعلامات التبويب والتحديدات والتوهجات وأشرطة التمرير عبر GenHub.", localizationService["Settings.Appearance.Theme.Description"]);
        Assert.Equal("السمة النشطة:", localizationService["Settings.Appearance.Theme.ActiveTheme"]);
        Assert.Equal("الإشعارات", localizationService["Notifications.Title"]);
    }
}
