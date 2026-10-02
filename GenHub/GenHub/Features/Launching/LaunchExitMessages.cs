using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.GameProfile;
using System;
using System.Globalization;
using System.Resources;

namespace GenHub.Features.Launching;

/// <summary>Shared localized messages for launch completion.</summary>
internal static class LaunchExitMessages
{
    private static readonly ResourceManager Resources = new(LocalizationConstants.StringResourceBaseName, typeof(LaunchExitMessages).Assembly);

    /// <summary>Describes the early exit without presenting a clean exit as a crash.</summary>
    /// <param name="launch">The launch whose diagnostic history remains in the registry.</param>
    /// <param name="localization">The application localization service, when available.</param>
    /// <returns>A localized message with the observed exit code when known.</returns>
    internal static string Describe(GameLaunchInfo launch, ILocalizationService? localization)
    {
        var key = launch.ExitCode.HasValue
            ? ProfileValidationConstants.EarlyExitWithCodeKey
            : ProfileValidationConstants.EarlyExitUnknownCodeKey;
        object?[] arguments = launch.ExitCode.HasValue ? [launch.ExitCode.Value] : [];
        return AppendExplanation(GetString(key, localization, arguments), launch.ExitCode, localization);
    }

    /// <summary>Maps a well-known Windows startup NTSTATUS exit code to the resource key that explains it.</summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <returns>The explanation resource key, or null when the code is not a known startup failure.</returns>
    internal static string? GetExplanationKey(int exitCode) => exitCode switch
    {
        StartupExitCodeConstants.StatusDllNotFound => StartupExitCodeConstants.DllNotFoundKey,
        StartupExitCodeConstants.StatusInvalidImageFormat => StartupExitCodeConstants.InvalidImageFormatKey,
        StartupExitCodeConstants.StatusDllInitFailed => StartupExitCodeConstants.DllInitFailedKey,
        StartupExitCodeConstants.StatusAccessViolation => StartupExitCodeConstants.AccessViolationKey,
        StartupExitCodeConstants.StatusStackBufferOverrun => StartupExitCodeConstants.StackBufferOverrunKey,
        _ => null,
    };

    /// <summary>Explains a well-known startup exit code in the user's language.</summary>
    /// <param name="exitCode">The process exit code, when known.</param>
    /// <param name="localization">The application localization service, when available.</param>
    /// <returns>The localized explanation, or null when the code is unknown.</returns>
    internal static string? Explain(int? exitCode, ILocalizationService? localization)
    {
        var key = exitCode is int code ? GetExplanationKey(code) : null;
        return key == null ? null : GetString(key, localization);
    }

    /// <summary>Appends the explanation of a well-known exit code to a message.</summary>
    /// <param name="message">The localized message that already names the exit code.</param>
    /// <param name="exitCode">The process exit code, when known.</param>
    /// <param name="localization">The application localization service, when available.</param>
    /// <returns>The message followed by the explanation, or the message unchanged for an unknown code.</returns>
    internal static string AppendExplanation(string message, int? exitCode, ILocalizationService? localization)
    {
        var explanation = Explain(exitCode, localization);
        return explanation == null ? message : $"{message} {explanation}";
    }

    /// <summary>Renders an exit code as its hexadecimal NTSTATUS value.</summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <returns>The eight-digit hexadecimal value without a prefix.</returns>
    internal static string FormatHex(int exitCode) =>
        unchecked((uint)exitCode).ToString(StartupExitCodeConstants.HexFormat, CultureInfo.InvariantCulture);

    /// <summary>Describes a process that exited immediately with a well-known startup exit code.</summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="standardErrorTail">The captured standard error tail, if any.</param>
    /// <param name="localization">The application localization service, when available.</param>
    /// <returns>
    /// A localized message that keeps the raw code and explains it, followed by any captured
    /// output, or null when the code is unknown and the caller keeps its own wording.
    /// </returns>
    internal static string? DescribeImmediateExit(int exitCode, string? standardErrorTail, ILocalizationService? localization)
    {
        var explanation = Explain(exitCode, localization);
        if (explanation == null)
        {
            return null;
        }

        var message = GetString(StartupExitCodeConstants.ImmediateExitExplainedKey, localization, exitCode, FormatHex(exitCode), explanation);
        return string.IsNullOrWhiteSpace(standardErrorTail) ? message : $"{message} {standardErrorTail}";
    }

    /// <summary>Describes a launcher failure, preserving legacy wording for unknown codes.</summary>
    /// <param name="exitCode">The observed exit code.</param>
    /// <param name="expectedName">The client the launcher was expected to start.</param>
    /// <param name="localization">The application localization service, when available.</param>
    /// <returns>The launcher failure and any known explanation.</returns>
    internal static string DescribeLauncherExit(int exitCode, string expectedName, ILocalizationService? localization)
    {
        var explanation = Explain(exitCode, localization);
        return explanation == null
            ? $"Launcher exited with code {exitCode} before starting {expectedName}."
            : GetString(StartupExitCodeConstants.LauncherExitExplainedKey, localization, exitCode, expectedName, explanation);
    }

    /// <summary>Resolves a launch message for both DI and standalone callers.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="localization">The application localization service, when available.</param>
    /// <param name="arguments">The format arguments.</param>
    /// <returns>The localized message, or its unformatted template if invalid.</returns>
    internal static string GetString(string key, ILocalizationService? localization, params object?[] arguments)
    {
        if (localization != null)
        {
            return localization.GetString(key, arguments);
        }

        // Preserve localization for callers constructing the launcher without application DI.
        var culture = CultureInfo.CurrentUICulture;
        var template = Resources.GetString(key, culture) ?? key;
        try
        {
            return string.Format(culture, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
