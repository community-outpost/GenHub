using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.Views.PublisherStudio;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Globalization;
using System.Linq;
using System.Resources;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Views;

/// <summary>Exercises the localized hosted-inventory actions inside their actual table slot.</summary>
public class HostingStorageActionLayoutTests
{
    /// <summary>Long translated actions must remain inside the fixed inventory action column.</summary>
    [AvaloniaFact]
    public void RussianInventoryActions_FitInsideTheirColumn()
    {
        var application = Avalonia.Application.Current!;
        var hadLocalization = application.Resources.TryGetValue(LocalizationConstants.ResourceServiceKey, out var previousLocalization);
        var resources = new ResourceManager(LocalizationConstants.StringResourceBaseName, typeof(HostingStorageView).Assembly);
        var culture = CultureInfo.GetCultureInfo("ru");
        var localization = new Mock<ILocalizationService>();
        localization.Setup(service => service[It.IsAny<string>()]).Returns((string key) => resources.GetString(key, culture) ?? key);
        localization.Setup(service => service.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] values) => string.Format(culture, resources.GetString(key, culture) ?? key, values));
        localization.SetupGet(service => service.CurrentCulture).Returns(culture);
        application.Resources[LocalizationConstants.ResourceServiceKey] = localization.Object;
        using var viewModel = new PublishShareViewModel(new PublisherStudioProject(), Mock.Of<IPublisherStudioService>(), NullLogger.Instance);
        var asset = new HostedAssetItemViewModel
        {
            Name = "community.zip",
            Url = "https://example.invalid/community.zip",
            CanAddToCatalog = true,
            CanDelete = true,
        };
        viewModel.FilteredHostedAssets.Add(asset);
        var view = new HostingStorageView { DataContext = viewModel };
        var window = new Window { Width = 1400, Height = 1000, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var buttons = view.GetVisualDescendants().OfType<Button>()
                .Where(button => ReferenceEquals(button.DataContext, asset) && button.IsEffectivelyVisible)
                .ToArray();
            Assert.Equal(4, buttons.Length);
            var column = Assert.IsAssignableFrom<Panel>(buttons[0].GetVisualParent());
            foreach (var button in buttons)
            {
                var topLeft = button.TranslatePoint(new Point(0, 0), column);
                Assert.NotNull(topLeft);
                Assert.True(topLeft.Value.X >= 0);
                Assert.True(
                    topLeft.Value.X + button.Bounds.Width <= column.Bounds.Width + 0.1,
                    $"Action extends beyond its {column.Bounds.Width}px table column.");
            }
        }
        finally
        {
            window.Close();
            if (hadLocalization)
            {
                application.Resources[LocalizationConstants.ResourceServiceKey] = previousLocalization;
            }
            else
            {
                application.Resources.Remove(LocalizationConstants.ResourceServiceKey);
            }
        }
    }
}
