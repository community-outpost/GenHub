using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.Views;
using GenHub.Features.Info.ViewModels;
using Moq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Views;

/// <summary>
/// Headless tests for the search box on the Game Settings page.
/// </summary>
public class GameSettingsViewSearchTests
{
    private static readonly ResourceManager Resources =
        new(LocalizationConstants.StringResourceBaseName, typeof(LocalizationService).Assembly);

    /// <summary>
    /// Verifies a query shows the rows whose label contains it and hides the rest.
    /// </summary>
    [AvaloniaFact]
    public void Search_MatchesRowLabel()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: false, isGeneralsOnline: false, (view, settings) =>
        {
            Search(settings, "  TEXTURE ");

            Assert.Equal(["Texture Quality"], VisibleRowLabels(view));
            Assert.Contains("search-hidden", view.FindControl<TextBlock>("NoSearchResults")!.Classes);
        });
    }

    /// <summary>
    /// Verifies a query that only appears in a row's tooltip still shows the row.
    /// </summary>
    [AvaloniaFact]
    public void Search_MatchesRowTooltip()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: false, isGeneralsOnline: false, (view, settings) =>
        {
            Search(settings, "display resolution");

            Assert.Equal(["Resolution"], VisibleRowLabels(view));
        });
    }

    /// <summary>
    /// Verifies a match on a section header shows every row of that section and hides the other sections.
    /// </summary>
    [AvaloniaFact]
    public void Search_MatchesSectionHeader_ShowsWholeSection()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: false, isGeneralsOnline: true, (view, settings) =>
        {
            Search(settings, "multiplayer");

            var rows = VisibleRowLabels(view);
            Assert.Contains("Auto Login", rows);
            Assert.Contains("Verbose Logging", rows);
            Assert.Contains("Friend Online (In Game)", rows);
            Assert.DoesNotContain("Resolution", rows);
            Assert.True(view.FindControl<TextBlock>("MultiplayerHeader")!.IsEffectivelyVisible);
            Assert.DoesNotContain("Video", VisibleSectionHeaders(view));
        });
    }

    /// <summary>
    /// Verifies a match on a sub-label shows its rows, and the sub-label hides when none of its rows match.
    /// </summary>
    [AvaloniaFact]
    public void Search_MatchesSubLabel_ShowsItsRows()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: true, isGeneralsOnline: false, (view, settings) =>
        {
            Search(settings, "screen edge");

            Assert.Equal(["In Fullscreen", "In Windowed"], VisibleRowLabels(view));
            Assert.Equal(["Screen Edge Scrolling"], VisibleSubLabels(view));

            Search(settings, "archive");

            Assert.Equal(["Archive Replays"], VisibleRowLabels(view));
            Assert.Empty(VisibleSubLabels(view));
        });
    }

    /// <summary>
    /// Verifies search never shows a row that the profile's game client hides.
    /// </summary>
    [AvaloniaFact]
    public void Search_DoesNotRevealClientHiddenRows()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: false, isGeneralsOnline: false, (view, settings) =>
        {
            Search(settings, "archive");

            Assert.False(view.FindControl<Border>("TshArchiveReplaysRow")!.IsEffectivelyVisible);
            Assert.Empty(VisibleRowLabels(view));
            Assert.Empty(VisibleSectionHeaders(view));
            Assert.True(view.FindControl<TextBlock>("NoSearchResults")!.IsEffectivelyVisible);

            Search(settings, "multiplayer");

            Assert.False(view.FindControl<TextBlock>("MultiplayerHeader")!.IsEffectivelyVisible);
            Assert.Equal(["Profanity Filter"], VisibleRowLabels(view));
            Assert.Equal(["Game"], VisibleSectionHeaders(view));
        });
    }

    /// <summary>
    /// Verifies a query with no matches shows the empty message, and clearing it restores every row.
    /// </summary>
    [AvaloniaFact]
    public void Search_NoMatches_ShowsMessage_AndClearingRestoresRows()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: true, isGeneralsOnline: true, (view, settings) =>
        {
            var allRows = VisibleRowLabels(view);
            var allHeaders = VisibleSectionHeaders(view);
            Assert.NotEmpty(allRows);

            Search(settings, "zzzz no such setting");

            Assert.Empty(VisibleRowLabels(view));
            Assert.Empty(VisibleSectionHeaders(view));
            Assert.True(view.FindControl<TextBlock>("NoSearchResults")!.IsEffectivelyVisible);

            Search(settings, string.Empty);

            Assert.Equal(allRows, VisibleRowLabels(view));
            Assert.Equal(allHeaders, VisibleSectionHeaders(view));
            Assert.False(view.FindControl<TextBlock>("NoSearchResults")!.IsEffectivelyVisible);
        });
    }

    /// <summary>
    /// Verifies Escape in the search box clears the query.
    /// </summary>
    [AvaloniaFact]
    public void Search_Escape_ClearsQuery()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: false, isGeneralsOnline: false, (view, settings) =>
        {
            Search(settings, "texture");
            var box = view.FindControl<TextBox>("SettingsSearchBox")!;
            Assert.True(box.IsTabStop);

            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(string.Empty, settings.SearchQuery);
            Assert.Contains("Resolution", VisibleRowLabels(view));
        });
    }

    /// <summary>
    /// Verifies search matches the localized text the user sees, not the English strings.
    /// </summary>
    [AvaloniaFact]
    public void Search_MatchesLocalizedText()
    {
        WithSettingsView("ru", isTheSuperHackers: false, isGeneralsOnline: false, (view, settings) =>
        {
            Search(settings, "разрешение");

            Assert.Equal(["Разрешение"], VisibleRowLabels(view));

            Search(settings, "Texture Quality");

            Assert.Empty(VisibleRowLabels(view));
            Assert.True(view.FindControl<TextBlock>("NoSearchResults")!.IsEffectivelyVisible);
        });
    }

    /// <summary>
    /// Verifies a row nested below a sub-label inside its own visibility panel keeps that sub-label for matching.
    /// </summary>
    [AvaloniaFact]
    public void Search_NestedRow_KeepsItsSubLabel()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: false, isGeneralsOnline: false, (view, settings) =>
        {
            Assert.True(settings.IsZeroHour);
            Assert.True(settings.IsCustomLodSelected);

            Search(settings, "heat");

            Assert.Equal(["Heat Effects"], VisibleRowLabels(view));
            Assert.Equal(["Advanced Display Options"], VisibleSubLabels(view));

            Search(settings, "advanced display options");

            Assert.Contains("Heat Effects", VisibleRowLabels(view));
            Assert.Contains("Show Trees", VisibleRowLabels(view));
        });
    }

    /// <summary>
    /// Verifies a word that only appears in a sub-label's tooltip shows that sub-label's rows.
    /// </summary>
    [AvaloniaFact]
    public void Search_MatchesSubLabelTooltip()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: true, isGeneralsOnline: false, (view, settings) =>
        {
            Search(settings, "accidentally");

            Assert.Equal(["Cursor Clipping / Capture"], VisibleSubLabels(view));
            Assert.Equal(["Fullscreen in Game", "Fullscreen in Menus", "Windowed in Game", "Windowed in Menus"], VisibleRowLabels(view));
        });
    }

    /// <summary>
    /// Verifies the results follow a client becoming available or unavailable while a query is active.
    /// </summary>
    [AvaloniaFact]
    public void Search_RefiltersWhenClientVisibilityChanges()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: false, isGeneralsOnline: false, (view, settings) =>
        {
            Search(settings, "multiplayer");
            Assert.False(view.FindControl<TextBlock>("MultiplayerHeader")!.IsEffectivelyVisible);
            Assert.DoesNotContain("Auto Login", VisibleRowLabels(view));

            settings.UpdateApplicableClientVisibility(isTheSuperHackers: false, isGeneralsOnline: true);
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.FindControl<TextBlock>("MultiplayerHeader")!.IsEffectivelyVisible);
            Assert.Contains("Auto Login", VisibleRowLabels(view));

            settings.UpdateApplicableClientVisibility(isTheSuperHackers: false, isGeneralsOnline: false);
            Dispatcher.UIThread.RunJobs();

            Assert.False(view.FindControl<TextBlock>("MultiplayerHeader")!.IsEffectivelyVisible);
            Assert.DoesNotContain("Auto Login", VisibleRowLabels(view));
            Assert.Equal(["Profanity Filter"], VisibleRowLabels(view));
        });
    }

    /// <summary>
    /// Verifies the results follow the custom detail level being switched off while a query is active.
    /// </summary>
    [AvaloniaFact]
    public void Search_RefiltersWhenCustomDetailLevelChanges()
    {
        WithSettingsView(string.Empty, isTheSuperHackers: false, isGeneralsOnline: false, (view, settings) =>
        {
            Search(settings, "advanced display options");
            Assert.Contains("Heat Effects", VisibleRowLabels(view));

            settings.StaticGameLOD = "High";
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(VisibleRowLabels(view));
            Assert.Empty(VisibleSubLabels(view));
            Assert.True(view.FindControl<TextBlock>("NoSearchResults")!.IsEffectivelyVisible);
        });
    }

    private static void Search(GameSettingsViewModel settings, string query)
    {
        settings.SearchQuery = query;
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string> VisibleRowLabels(GameSettingsView view) =>
        view.GetVisualDescendants().OfType<Border>()
            .Where(row => row.Classes.Contains("setting-row") && row.IsEffectivelyVisible)
            .Select(row => row.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("row-label")).Text ?? string.Empty)
            .ToList();

    private static List<string> VisibleSectionHeaders(GameSettingsView view) =>
        view.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.Classes.Contains("section-header") && text.IsEffectivelyVisible)
            .Select(text => text.Text ?? string.Empty)
            .ToList();

    private static List<string> VisibleSubLabels(GameSettingsView view) =>
        view.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.Classes.Contains("group-label") && text.IsEffectivelyVisible)
            .Select(text => text.Text ?? string.Empty)
            .ToList();

    private static void WithSettingsView(string cultureName, bool isTheSuperHackers, bool isGeneralsOnline, Action<GameSettingsView, GameSettingsViewModel> test)
    {
        var resourceSet = Resources.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;
        var localization = new Mock<ILocalizationService>();
        localization.Setup(service => service[It.IsAny<string>()]).Returns((string key) => resourceSet.GetString(key) ?? key);

        var appResources = Avalonia.Application.Current!.Resources;
        var hadPrevious = appResources.TryGetValue(LocalizationConstants.ResourceServiceKey, out var previous);
        appResources[LocalizationConstants.ResourceServiceKey] = localization.Object;
        var settings = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ProfileTab().GameSettingsViewModel;
        settings.UpdateApplicableClientVisibility(isTheSuperHackers, isGeneralsOnline);
        var view = new GameSettingsView { DataContext = settings };
        var window = new Window { Width = 900, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            test(view, settings);
        }
        finally
        {
            window.Close();
            if (hadPrevious)
            {
                appResources[LocalizationConstants.ResourceServiceKey] = previous;
            }
            else
            {
                appResources.Remove(LocalizationConstants.ResourceServiceKey);
            }
        }
    }
}
