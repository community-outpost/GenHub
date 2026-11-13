using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Shows the dialog that lists the required items missing from content and offers to install them.
/// Adding content from Downloads, creating a profile, and saving in the profile editor all use it.
/// </summary>
public static class MissingDependencyDialog
{
    private const string MarkdownSpecialCharacters = "\\`*_[]<>";

    /// <summary>
    /// Shows the dialog and waits for the player's choice.
    /// </summary>
    /// <param name="dialogService">The dialog service.</param>
    /// <param name="localizationService">The localization service, or null to use English.</param>
    /// <param name="contentName">The name to show when the items are needed by more than one content item.</param>
    /// <param name="missingDependencies">The missing items.</param>
    /// <param name="cancellationToken">A token that closes the dialog without a choice.</param>
    /// <returns><see langword="true"/> when the player chose to install the items.</returns>
    public static async Task<bool> ConfirmInstallAsync(
        IDialogService dialogService,
        ILocalizationService? localizationService,
        string contentName,
        IReadOnlyList<ContentDependencyGap> missingDependencies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(missingDependencies);

        var installAction = new DialogAction
        {
            Text = missingDependencies.All(gap => gap.CanDownload)
                ? localizationService.GetLocalizedString("GameProfiles.MissingDependencies.Install", "Install required items")
                : localizationService.GetLocalizedString("GameProfiles.MissingDependencies.InstallAvailable", "Install available items"),
            Style = NotificationActionStyle.Primary,
        };
        var cancelAction = new DialogAction
        {
            Text = localizationService.GetLocalizedString("Common.Cancel", "Cancel"),
            Style = NotificationActionStyle.Secondary,
        };

        var actions = new List<DialogAction> { cancelAction };
        if (missingDependencies.Any(gap => gap.CanDownload))
        {
            actions.Add(installAction);
        }

        var (action, _) = await dialogService.ShowMessageWithDetailsAsync(
            BuildTitle(localizationService, contentName, missingDependencies),
            BuildContent(localizationService, contentName, missingDependencies),
            actions,
            localizationService.GetLocalizedString("GameProfiles.MissingDependencies.Details", "Technical details"),
            BuildDetails(missingDependencies),
            cancellationToken);

        return ReferenceEquals(action, installAction);
    }

    /// <summary>
    /// Builds the dialog title, which names the single missing item or counts several.
    /// </summary>
    /// <param name="localizationService">The localization service, or null to use English.</param>
    /// <param name="contentName">The name to show when the items are needed by more than one content item.</param>
    /// <param name="missingDependencies">The missing items.</param>
    /// <returns>The title.</returns>
    public static string BuildTitle(
        ILocalizationService? localizationService,
        string contentName,
        IReadOnlyList<ContentDependencyGap> missingDependencies)
    {
        ArgumentNullException.ThrowIfNull(missingDependencies);

        return missingDependencies.Count == 1
            ? localizationService.GetLocalizedString("GameProfiles.MissingDependencies.Title.Single", "One required item is missing")
            : localizationService.GetLocalizedString("GameProfiles.MissingDependencies.Title.Multiple", "{0} required items are missing", missingDependencies.Count);
    }

    /// <summary>
    /// Builds the player-facing Markdown body without diagnostic IDs.
    /// </summary>
    /// <param name="localizationService">The localization service, or null to use English.</param>
    /// <param name="contentName">The name to show when the items are needed by more than one content item.</param>
    /// <param name="missingDependencies">The missing items.</param>
    /// <returns>The Markdown body.</returns>
    public static string BuildContent(
        ILocalizationService? localizationService,
        string contentName,
        IReadOnlyList<ContentDependencyGap> missingDependencies)
    {
        ArgumentNullException.ThrowIfNull(missingDependencies);

        var ownerName = GetOwnerName(contentName, missingDependencies);
        var showRequiredBy = GetOwnerNames(missingDependencies).Count > 1;

        var builder = new StringBuilder();
        builder.AppendLine(localizationService.GetLocalizedString(
            "GameProfiles.MissingDependencies.Intro",
            "{0} only works when these items are in the profile:",
            EscapeMarkdown(ownerName)));
        builder.AppendLine();

        foreach (var gap in missingDependencies)
        {
            builder.Append("- **").Append(EscapeMarkdown(GetDisplayName(localizationService, gap))).Append("**");
            if (showRequiredBy && !string.IsNullOrWhiteSpace(gap.RequiredByName))
            {
                builder.Append(' ').Append(localizationService.GetLocalizedString(
                    "GameProfiles.MissingDependencies.RequiredBy",
                    "(needed by {0})",
                    EscapeMarkdown(gap.RequiredByName)));
            }

            if (!gap.CanDownload)
            {
                builder.Append(". ").Append(GetManualInstallHint(localizationService, gap.DependencyType));
            }

            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine(GetOutcomeText(localizationService, missingDependencies));
        return builder.ToString();
    }

    /// <summary>
    /// Builds plain diagnostic text for the collapsed details panel.
    /// </summary>
    /// <param name="missingDependencies">The missing items.</param>
    /// <returns>The declared dependency and owner IDs.</returns>
    public static string BuildDetails(IReadOnlyList<ContentDependencyGap> missingDependencies)
    {
        ArgumentNullException.ThrowIfNull(missingDependencies);
        return string.Join(Environment.NewLine, missingDependencies.Select(gap => $"{gap.DependencyId} ({gap.RequiredById})"));
    }

    /// <summary>
    /// Builds a short sentence that names every missing item, for notifications.
    /// </summary>
    /// <param name="localizationService">The localization service, or null to use English.</param>
    /// <param name="contentName">The name of the content that needs the items.</param>
    /// <param name="missingDependencies">The missing items.</param>
    /// <returns>The sentence.</returns>
    public static string BuildStillMissingMessage(
        ILocalizationService? localizationService,
        string contentName,
        IReadOnlyList<ContentDependencyGap> missingDependencies)
    {
        ArgumentNullException.ThrowIfNull(missingDependencies);

        return localizationService.GetLocalizedString(
            "GameProfiles.MissingDependencies.StillMissing.Message",
            "{0} was not added. It still needs {1}.",
            contentName,
            string.Join(", ", missingDependencies.Select(gap => GetDisplayName(localizationService, gap))));
    }

    /// <summary>
    /// Gets the name to show for a missing item, with a generic name when none is declared.
    /// </summary>
    /// <param name="localizationService">The localization service, or null to use English.</param>
    /// <param name="gap">The missing item.</param>
    /// <returns>The player-facing name.</returns>
    public static string GetDisplayName(ILocalizationService? localizationService, ContentDependencyGap gap) =>
        string.IsNullOrWhiteSpace(gap.DisplayName)
            ? localizationService.GetLocalizedString("GameProfiles.MissingDependencies.UnnamedItem", "A required item")
            : gap.DisplayName;

    /// <summary>
    /// Escapes Markdown in names taken from manifests, so a name cannot add links or formatting.
    /// </summary>
    private static string EscapeMarkdown(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (MarkdownSpecialCharacters.Contains(character))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static List<string> GetOwnerNames(IReadOnlyList<ContentDependencyGap> missingDependencies) =>
        missingDependencies
            .Select(gap => gap.RequiredByName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string GetOwnerName(string contentName, IReadOnlyList<ContentDependencyGap> missingDependencies)
    {
        var owners = GetOwnerNames(missingDependencies);
        return owners.Count == 1 ? owners[0] : contentName;
    }

    private static string GetManualInstallHint(ILocalizationService? localizationService, ContentType dependencyType) =>
        dependencyType switch
        {
            ContentType.GameInstallation => localizationService.GetLocalizedString(
                "GameProfiles.MissingDependencies.ManualHint.GameInstallation",
                "GenHub can't download this. Install the game, then let GenHub detect it."),
            ContentType.GameClient => localizationService.GetLocalizedString(
                "GameProfiles.MissingDependencies.ManualHint.GameClient",
                "GenHub can't download this. Get it from Downloads or from its publisher."),
            _ => localizationService.GetLocalizedString(
                "GameProfiles.MissingDependencies.ManualHint.Content",
                "GenHub can't download this. Get it from its publisher, then add it to GenHub."),
        };

    private static string GetOutcomeText(
        ILocalizationService? localizationService,
        IReadOnlyList<ContentDependencyGap> missingDependencies)
    {
        if (missingDependencies.All(gap => gap.CanDownload))
        {
            return localizationService.GetLocalizedString(
                "GameProfiles.MissingDependencies.Outcome.Install",
                "Install required items downloads and enables them first. Cancel saves nothing.");
        }

        if (missingDependencies.Any(gap => gap.CanDownload))
        {
            return localizationService.GetLocalizedString(
                "GameProfiles.MissingDependencies.Outcome.Partial",
                "Install required items downloads what GenHub can. Nothing is saved until every item is present.");
        }

        return localizationService.GetLocalizedString(
            "GameProfiles.MissingDependencies.Outcome.Manual",
            "Nothing is saved until these items are present.");
    }
}
