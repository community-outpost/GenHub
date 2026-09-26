using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Features.Info.Services;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for <see cref="DefaultInfoContentProvider"/>.
/// </summary>
public class DefaultInfoContentProviderTests
{
    private readonly DefaultInfoContentProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInfoContentProviderTests"/> class.
    /// </summary>
    public DefaultInfoContentProviderTests()
    {
        _provider = new DefaultInfoContentProvider();
    }

    /// <summary>
    /// Verifies that GetAllSectionsAsync returns all expected info sections.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetAllSectionsAsync_ReturnsOrderedSectionsAsync()
    {
        var sections = (await _provider.GetAllSectionsAsync()).ToList();

        sections.Should().NotBeEmpty();
        sections.Select(section => section.Id).Should().Equal(
        [
            InfoConstants.SectionQuickstart,
            InfoConstants.SectionGameProfiles,
            InfoConstants.SectionGameSettings,
            InfoConstants.SectionGameProfileContent,
            InfoConstants.SectionShortcuts,
            InfoConstants.SectionSteam,
            InfoConstants.SectionLocalContent,
            InfoConstants.SectionTools,
            InfoConstants.SectionScanGames,
            InfoConstants.SectionWorkspaces,
            InfoConstants.SectionAppUpdates,
            InfoConstants.SectionChangelogs,
            InfoConstants.SectionFaq,
            InfoConstants.SectionGoChangelog,
        ]);
    }

    /// <summary>
    /// Verifies that GetSectionAsync returns the workspace section with comprehensive strategy explanations.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetSectionAsync_WorkspaceSection_ContainsComprehensiveStrategyExplanationsAsync()
    {
        var section = await _provider.GetSectionAsync(InfoConstants.SectionWorkspaces);

        section.Should().NotBeNull();
        section!.Title.Should().Be("Virtual Workspaces");
        section.Cards.Should().NotBeEmpty();

        var titles = section.Cards.Select(c => c.Title).ToList();
        titles.Should().Contain("The Magic Mirror");
        titles.Should().Contain("Workspace Strategies Compared");
        titles.Should().Contain("Hardlinks vs Symlinks vs Copies: Deep Dive");
        titles.Should().Contain("Troubleshooting & Permissions");
        titles.Should().Contain("Performance Specs");

        var comparisonCard = section.Cards.First(c => c.Title == "Workspace Strategies Compared");
        comparisonCard.DetailedContent.Should().Contain("HardLink");
        comparisonCard.DetailedContent.Should().Contain("SymlinkOnly");
        comparisonCard.DetailedContent.Should().Contain("HybridCopySymlink");
        comparisonCard.DetailedContent.Should().Contain("FullCopy");

        var deepDiveCard = section.Cards.First(c => c.Title == "Hardlinks vs Symlinks vs Copies: Deep Dive");
        deepDiveCard.DetailedContent.Should().Contain("Hardlink");
        deepDiveCard.DetailedContent.Should().Contain("Symlink");
        deepDiveCard.DetailedContent.Should().Contain("Full Copy");
        deepDiveCard.DetailedContent.Should().Contain("Automatic Fallback");
    }

    /// <summary>
    /// Verifies that GetSectionAsync returns the tools section with all recent tools and PR feature guides.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetSectionAsync_ToolsSection_ContainsAllToolsAndOpenPrGuidesAsync()
    {
        var section = await _provider.GetSectionAsync(InfoConstants.SectionTools);

        section.Should().NotBeNull();

        var cardIds = section!.Cards.Select(c => c.Id).ToList();
        cardIds.Should().Contain([
            InfoConstants.CardToolsReplayImport,
            InfoConstants.CardToolsReplayGameClientMapping,
            InfoConstants.CardToolsReplayCheckpointsTakeover,
            InfoConstants.CardToolsReplayCloud,
            InfoConstants.CardToolsReplayArchive,
            InfoConstants.CardToolsMapLibrary,
            InfoConstants.CardToolsMapPacks,
            InfoConstants.CardToolsHotkeyEditorRebind,
            InfoConstants.CardToolsHotkeyEditorAddons,
            InfoConstants.CardToolsPublisherStudioPipeline,
            InfoConstants.CardToolsPublisherStudioCdnHosting,
            InfoConstants.CardToolsPublisherStudioUpdateNotifications,
            InfoConstants.CardToolsPublisherStudioAuthoring,
            InfoConstants.CardToolsModbuilderSuitePipeline,
            InfoConstants.CardToolsModbuilderSuiteWndBuild,
        ]);

        var crcCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsReplayGameClientMapping);
        crcCard.DetailedContent.Should().Contain("CRC");
        crcCard.DetailedContent.Should().Contain("140+");
        crcCard.DetailedContent.Should().Contain("Steam Zero Hour 1.04");
        crcCard.DetailedContent.Should().Contain("Retail Zero Hour 1.04");
        crcCard.DetailedContent.Should().Contain("Steam Generals 1.09");
        crcCard.DetailedContent.Should().Contain("TheSuperHackers");
        crcCard.DetailedContent.Should().Contain("Launch");
        crcCard.DetailedContent.Should().Contain("Setup");
        crcCard.DetailedContent.Should().Contain("Profile");

        var takeoverCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsReplayCheckpointsTakeover);
        takeoverCard.DetailedContent.Should().Contain("Create Checkpoint");
        takeoverCard.DetailedContent.Should().Contain("Resume Replay");
        takeoverCard.DetailedContent.Should().Contain("Takeover Match");
        takeoverCard.DetailedContent.Should().Contain("Takeover Player");

        var replayCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsReplayImport);
        replayCard.DetailedContent.Should().NotContain("player IP hashes");

        var mapCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsMapLibrary);
        mapCard.DetailedContent.Should().Contain(".tga");
        mapCard.DetailedContent.Should().NotContain("player count");

        var hotkeyRebindCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsHotkeyEditorRebind);
        hotkeyRebindCard.DetailedContent.Should().Contain("Command Card");
        hotkeyRebindCard.DetailedContent.Should().Contain("Conflict");

        var hotkeyAddonCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsHotkeyEditorAddons);
        hotkeyAddonCard.DetailedContent.Should().Contain("Cameo");
        hotkeyAddonCard.DetailedContent.Should().Contain(".manifest.json");

        var publisherPipelineCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsPublisherStudioPipeline);
        publisherPipelineCard.DetailedContent.Should().Contain("publisher.json");
        publisherPipelineCard.DetailedContent.Should().Contain("catalog-*.json");
        publisherPipelineCard.DetailedContent.Should().Contain("SHA-256");

        var publisherCdnCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsPublisherStudioCdnHosting);
        publisherCdnCard.DetailedContent.Should().Contain("CDN");
        publisherCdnCard.DetailedContent.Should().Contain("GitHub Releases");
        publisherCdnCard.DetailedContent.Should().Contain("Google Drive");

        var publisherNotifCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsPublisherStudioUpdateNotifications);
        publisherNotifCard.DetailedContent.Should().Contain("PublisherCatalogUpdateService");
        publisherNotifCard.DetailedContent.Should().Contain("genhub://subscribe");
        publisherNotifCard.DetailedContent.Should().Contain("SemVer");

        var publisherAuthorCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsPublisherStudioAuthoring);
        publisherAuthorCard.Title.Should().Contain("Publisher Studio");
        publisherAuthorCard.Actions.Should().ContainSingle(a => a.ActionId == InfoConstants.ActionNavTools);

        var modbuilderPipelineCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsModbuilderSuitePipeline);
        modbuilderPipelineCard.DetailedContent.Should().Contain(".mbproj");
        modbuilderPipelineCard.DetailedContent.Should().Contain("DDS");
        modbuilderPipelineCard.DetailedContent.Should().ContainEquivalentOf("CSF");

        var modbuilderWndCard = section.Cards.First(c => c.Id == InfoConstants.CardToolsModbuilderSuiteWndBuild);
        modbuilderWndCard.DetailedContent.Should().Contain(".wnd");
        modbuilderWndCard.DetailedContent.Should().Contain("Incremental");
    }

    /// <summary>
    /// Verifies that GetSectionAsync returns the app updates section documenting Device Flow, update window demo, and obsolete PAT.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetSectionAsync_AppUpdatesSection_ContainsDeviceFlowAndPatObsoleteAsync()
    {
        var section = await _provider.GetSectionAsync(InfoConstants.SectionAppUpdates);

        section.Should().NotBeNull();

        var cardIds = section!.Cards.Select(c => c.Id).ToList();
        cardIds.Should().Contain([
            InfoConstants.CardUpdatesVersionControl,
            InfoConstants.CardUpdatesWorkflow,
            InfoConstants.CardUpdatesRollback,
            InfoConstants.CardUpdatesGithubOauthDevice,
            InfoConstants.CardUpdatesCiArtifactsPrTesting,
            InfoConstants.CardUpdatesOfflineDownloads,
        ]);

        var updateDemoCard = section.Cards.First(c => c.Id == InfoConstants.CardUpdatesVersionControl);
        updateDemoCard.DetailedContent.Should().Contain("Update Tab");
        updateDemoCard.DetailedContent.Should().Contain("Browse Builds Tab");
        updateDemoCard.DetailedContent.Should().Contain("Open Pull Requests");
        updateDemoCard.DetailedContent.Should().Contain("Branches");
        updateDemoCard.DetailedContent.Should().Contain("Install Update");

        var authCard = section.Cards.First(c => c.Id == InfoConstants.CardUpdatesGithubOauthDevice);
        authCard.DetailedContent.Should().Contain("PAT");
        authCard.DetailedContent.Should().Contain("OAuth Device Flow");
        authCard.DetailedContent.Should().Contain("Copy and Open");

        var ciCard = section.Cards.First(c => c.Id == InfoConstants.CardUpdatesCiArtifactsPrTesting);
        ciCard.DetailedContent.Should().ContainEquivalentOf("pull requests");
        ciCard.DetailedContent.Should().Contain("False Update Suppression");

        var offlineCard = section.Cards.First(c => c.Id == InfoConstants.CardUpdatesOfflineDownloads);
        offlineCard.DetailedContent.Should().Contain("CAS");
        offlineCard.DetailedContent.Should().Contain("SHA-256");
        offlineCard.DetailedContent.Should().Contain("Cascade Deletion");
    }

    /// <summary>
    /// Verifies that GetSectionAsync returns the game detection section with cross-platform and archive bridging.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetSectionAsync_GameDetectionSection_ContainsCrossPlatformDetectionAsync()
    {
        var section = await _provider.GetSectionAsync(InfoConstants.SectionScanGames);

        section.Should().NotBeNull();

        var cardIds = section!.Cards.Select(c => c.Id).ToList();
        cardIds.Should().Contain([
            InfoConstants.CardScanAutoDetection,
            InfoConstants.CardScanSignatureVerification,
            InfoConstants.CardScanCrossPlatformDetection,
        ]);

        var autoDetectCard = section.Cards.First(c => c.Id == InfoConstants.CardScanAutoDetection);
        autoDetectCard.DetailedContent.Should().Contain("1.09").And.Contain("1.04");
        autoDetectCard.DetailedContent.Should().NotContain("1.08");
        autoDetectCard.DetailedContent.Should().NotContain("community patch");

        var unixCard = section.Cards.First(c => c.Id == InfoConstants.CardScanCrossPlatformDetection);
        unixCard.DetailedContent.Should().Contain("Linux");
        unixCard.DetailedContent.Should().Contain("macOS");
        unixCard.DetailedContent.Should().Contain("Supplemental Archive Linking");
    }

    /// <summary>
    /// Verifies that GetSectionAsync returns the game profiles section with controls and window persistence.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetSectionAsync_GameProfilesSection_ContainsControlsAndManifestScrubbingAsync()
    {
        var section = await _provider.GetSectionAsync(InfoConstants.SectionGameProfiles);

        section.Should().NotBeNull();

        var controlsCard = section!.Cards.First(c => c.Id == InfoConstants.CardProfilesControls);
        controlsCard.DetailedContent.Should().Contain("Manifest Scrubbing");
        controlsCard.DetailedContent.Should().Contain("Window & Layout Persistence");

        var advancedCard = section!.Cards.First(c => c.Id == InfoConstants.CardProfilesAdvancedOptions);
        advancedCard.DetailedContent.Should().Contain("Camera & Visual Tuning");
        advancedCard.DetailedContent.Should().NotContain("pitch");
    }

    /// <summary>
    /// Verifies that all sections have cards with IsExpandable correctly reflecting detailed content presence.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetAllSectionsAsync_AllCardsHaveConsistentIsExpandableAsync()
    {
        var sections = await _provider.GetAllSectionsAsync();

        foreach (var section in sections)
        {
            foreach (var card in section.Cards)
            {
                card.IsExpandable.Should().Be(!string.IsNullOrWhiteSpace(card.DetailedContent), $"Card '{card.Id}' in section '{section.Id}' should have IsExpandable match DetailedContent presence");
            }
        }
    }
}
