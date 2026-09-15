using System;
using System.Globalization;
using System.Linq;
using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Tests.Core.Collections;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.DependencyInjection;

/// <summary>
/// Unit tests verifying DI registration and live culture resolution in <see cref="LocalizationModule"/>.
/// </summary>
[Collection(LocalizationCultureCollection.Name)]
public sealed class LocalizationModuleTests : IDisposable
{
    private readonly CultureInfo? _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private readonly CultureInfo _originalThreadUiCulture = CultureInfo.CurrentUICulture;

    /// <summary>
    /// Restores process-wide UI culture defaults changed by localization resolution.
    /// </summary>
    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalThreadUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
    }

    /// <summary>
    /// Verifies that localization resolves as one shared service with default English resources.
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
        Assert.Equal("en", service1.CurrentCulture.Name);
        Assert.Equal("GenHub", service1.GetString("App.Name"));
    }

    /// <summary>
    /// Verifies that Russian and Arabic satellite assemblies are discovered automatically.
    /// </summary>
    [Fact]
    public void AddLocalizationServices_DiscoversRussianAndArabicCultures()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalizationServices();

        using var provider = services.BuildServiceProvider();
        var localizationService = provider.GetRequiredService<ILocalizationService>();
        var cultureNames = localizationService.AvailableCultures.Select(c => c.Name).ToList();

        Assert.Contains("en", cultureNames);
        Assert.Contains("ru", cultureNames);
        Assert.Contains("ar", cultureNames);
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
