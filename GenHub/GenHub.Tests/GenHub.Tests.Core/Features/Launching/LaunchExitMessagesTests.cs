using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Features.Launching;
using Moq;
using System.Globalization;
using System.Resources;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>Early exits are localized and retain known exit codes, including clean exits.</summary>
public class LaunchExitMessagesTests
{
    /// <summary>Every supported translation describes early exits without leaking technical English diagnostics.</summary>
    /// <param name="cultureName">The requested UI culture.</param>
    /// <param name="expected">A translated phrase expected in the result.</param>
    [Theory]
    [InlineData("en", "before launch completed")]
    [InlineData("ar", "قبل اكتمال التشغيل")]
    [InlineData("ru", "до окончания запуска")]
    public void Describe_UsesLocalizedResources(string cultureName, string expected)
    {
        var resources = new ResourceManager(LocalizationConstants.StringResourceBaseName, typeof(LaunchExitMessages).Assembly);
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var localization = new Mock<ILocalizationService>();
        localization.Setup(m => m.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns<string, object?[]>((key, arguments) => string.Format(culture, resources.GetString(key, culture)!, arguments));
        var launch = new GameLaunchInfo
        {
            LaunchId = "early-exit", ProfileId = "profile", WorkspaceId = "workspace",
            ProcessInfo = new GameProcessInfo(), ExitCode = 0,
            FailureReason = "Internal diagnostic text",
        };
        var message = LaunchExitMessages.Describe(launch, localization.Object);
        Assert.Contains(expected, message);
        Assert.Contains("0", message);
        Assert.DoesNotContain(launch.FailureReason, message);
        launch.ExitCode = null;
        Assert.Contains(expected, LaunchExitMessages.Describe(launch, localization.Object));
    }

    /// <summary>Standalone callers receive translated success messages and a safe formatting fallback.</summary>
    /// <param name="cultureName">The requested UI culture.</param>
    /// <param name="expectedTitle">The translated success title.</param>
    [Theory]
    [InlineData("en", "Tool Launched")]
    [InlineData("ar", "تم تشغيل الأداة")]
    [InlineData("ru", "Инструмент запущен")]
    public void GetString_WithoutService_LocalizesAndToleratesInvalidArguments(string cultureName, string expectedTitle)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            Assert.Equal(expectedTitle, LaunchExitMessages.GetString("GameProfiles.Notification.ToolLaunchSuccess.Title", null));
            Assert.Contains("Tool", LaunchExitMessages.GetString("GameProfiles.Notification.ToolLaunchSuccess.Message", null, "Tool"));

            // Missing arguments trigger the same FormatException as an invalid translation placeholder.
            Assert.Contains("{0}", LaunchExitMessages.GetString("GameProfiles.Notification.EarlyExit.WithCode", null));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }
}
