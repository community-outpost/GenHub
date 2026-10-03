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
        var localization = CreateLocalization(CultureInfo.GetCultureInfo(cultureName));
        var launch = new GameLaunchInfo
        {
            LaunchId = "early-exit", ProfileId = "profile", WorkspaceId = "workspace",
            ProcessInfo = new GameProcessInfo(), ExitCode = 0,
            FailureReason = "Internal diagnostic text",
        };
        var message = LaunchExitMessages.Describe(launch, localization);
        Assert.Contains(expected, message);
        Assert.Contains("0", message);
        Assert.DoesNotContain(launch.FailureReason, message);
        launch.ExitCode = null;
        Assert.Contains(expected, LaunchExitMessages.Describe(launch, localization));
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
            Assert.Equal(expectedTitle, LaunchExitMessages.GetString(ProfileValidationConstants.ToolLaunchSuccessTitleKey, null));
            Assert.Contains("Tool", LaunchExitMessages.GetString(ProfileValidationConstants.ToolLaunchSuccessMessageKey, null, "Tool"));

            // Missing arguments trigger the same FormatException as an invalid translation placeholder.
            Assert.Contains("{0}", LaunchExitMessages.GetString(ProfileValidationConstants.EarlyExitWithCodeKey, null));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    /// <summary>Each well-known Windows startup NTSTATUS code maps to its own explanation.</summary>
    /// <param name="exitCode">The NTSTATUS value as reported by Process.ExitCode.</param>
    /// <param name="expectedKey">The resource key that explains it.</param>
    [Theory]
    [InlineData(-1_073_741_515, StartupExitCodeConstants.DllNotFoundKey)]
    [InlineData(-1_073_741_701, StartupExitCodeConstants.InvalidImageFormatKey)]
    [InlineData(-1_073_741_502, StartupExitCodeConstants.DllInitFailedKey)]
    [InlineData(-1_073_741_819, StartupExitCodeConstants.AccessViolationKey)]
    [InlineData(-1_073_740_791, StartupExitCodeConstants.StackBufferOverrunKey)]
    public void GetExplanationKey_MapsKnownStartupCodes(int exitCode, string expectedKey)
    {
        Assert.Equal(expectedKey, LaunchExitMessages.GetExplanationKey(exitCode));
    }

    /// <summary>Ordinary exit codes have no explanation, so callers keep their existing wording.</summary>
    /// <param name="exitCode">An exit code that is not a known startup failure.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(-1)]
    [InlineData(-1_073_741_510)]
    public void UnknownCodes_HaveNoExplanation(int exitCode)
    {
        Assert.Null(LaunchExitMessages.GetExplanationKey(exitCode));
        Assert.Null(LaunchExitMessages.DescribeImmediateExit(exitCode, "stderr", null));
        Assert.Equal("message", LaunchExitMessages.AppendExplanation("message", exitCode, null));
    }

    /// <summary>The immediate-exit failure for STATUS_DLL_NOT_FOUND names a missing DLL and keeps the raw code in every language.</summary>
    /// <param name="cultureName">The requested UI culture.</param>
    /// <param name="expected">A translated phrase about the missing DLL.</param>
    [Theory]
    [InlineData("en", "A required DLL could not be found")]
    [InlineData("ar", "تعذر العثور على مكتبة DLL مطلوبة")]
    [InlineData("ru", "Не удалось найти необходимую библиотеку DLL")]
    public void DescribeImmediateExit_ForDllNotFound_NamesTheMissingDll(string cultureName, string expected)
    {
        var localization = CreateLocalization(CultureInfo.GetCultureInfo(cultureName));

        var message = LaunchExitMessages.DescribeImmediateExit(StartupExitCodeConstants.StatusDllNotFound, null, localization);

        Assert.NotNull(message);
        Assert.Contains(expected, message);
        Assert.Contains("-1073741515", message);
        Assert.Contains("0xC0000135", message);
    }

    /// <summary>Known launcher failures translate the entire sentence; unknown ones keep legacy wording.</summary>
    /// <param name="cultureName">The UI language.</param>
    /// <param name="prefix">The translated launcher subject.</param>
    /// <param name="processPrefix">The process-neutral immediate-exit subject.</param>
    [Theory]
    [InlineData("en", "The launcher exited", "The process exited")]
    [InlineData("ru", "Программа запуска завершилась", "Процесс завершился")]
    [InlineData("ar", "خرج برنامج التشغيل", "خرجت العملية")]
    public void DescribeLauncherExit_TranslatesKnownCodes(string cultureName, string prefix, string processPrefix)
    {
        var localization = CreateLocalization(CultureInfo.GetCultureInfo(cultureName));
        var message = LaunchExitMessages.DescribeLauncherExit(StartupExitCodeConstants.StatusDllNotFound, "generalszh", localization);
        Assert.StartsWith(prefix, message);
        Assert.Contains("generalszh", message);
        Assert.Contains("-1073741515", message);
        Assert.StartsWith(processPrefix, LaunchExitMessages.DescribeImmediateExit(StartupExitCodeConstants.StatusDllNotFound, null, localization));
        Assert.Equal("Launcher exited with code 127 before starting generalszh.", LaunchExitMessages.DescribeLauncherExit(127, "generalszh", localization));
    }

    /// <summary>Captured output still follows the explanation so nothing the client printed is lost.</summary>
    [Fact]
    public void DescribeImmediateExit_KeepsCapturedOutput()
    {
        var localization = CreateLocalization(CultureInfo.GetCultureInfo("en"));

        var message = LaunchExitMessages.DescribeImmediateExit(StartupExitCodeConstants.StatusInvalidImageFormat, "loader said no", localization);

        Assert.NotNull(message);
        Assert.Contains("32-bit and 64-bit", message);
        Assert.EndsWith("loader said no", message);
    }

    /// <summary>An early exit with a known code explains it after the existing message.</summary>
    [Fact]
    public void Describe_WithDllNotFoundExit_ExplainsTheMissingDll()
    {
        var localization = CreateLocalization(CultureInfo.GetCultureInfo("en"));
        var launch = new GameLaunchInfo
        {
            LaunchId = "early-exit", ProfileId = "profile", WorkspaceId = "workspace",
            ProcessInfo = new GameProcessInfo(), ExitCode = -1_073_741_515,
        };

        var message = LaunchExitMessages.Describe(launch, localization);

        Assert.Contains("before launch completed", message);
        Assert.Contains("-1073741515", message);
        Assert.Contains("A required DLL could not be found", message);
    }

    /// <summary>An early exit with an unknown code keeps the existing message exactly.</summary>
    [Fact]
    public void Describe_WithUnknownExit_IsUnchanged()
    {
        var culture = CultureInfo.GetCultureInfo("en");
        var localization = CreateLocalization(culture);
        var launch = new GameLaunchInfo
        {
            LaunchId = "early-exit", ProfileId = "profile", WorkspaceId = "workspace",
            ProcessInfo = new GameProcessInfo(), ExitCode = 1,
        };

        var resources = new ResourceManager(LocalizationConstants.StringResourceBaseName, typeof(LaunchExitMessages).Assembly);
        var expected = string.Format(culture, resources.GetString(ProfileValidationConstants.EarlyExitWithCodeKey, culture)!, 1);
        Assert.Equal(expected, LaunchExitMessages.Describe(launch, localization));
    }

    private static ILocalizationService CreateLocalization(CultureInfo culture)
    {
        var resources = new ResourceManager(LocalizationConstants.StringResourceBaseName, typeof(LaunchExitMessages).Assembly);
        var localization = new Mock<ILocalizationService>();
        localization.Setup(m => m.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns<string, object?[]>((key, arguments) => string.Format(culture, resources.GetString(key, culture)!, arguments));
        return localization.Object;
    }
}
