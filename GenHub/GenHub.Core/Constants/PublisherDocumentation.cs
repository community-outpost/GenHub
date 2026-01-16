namespace GenHub.Core.Constants;

/// <summary>
/// Documentation strings for the Publisher Studio UI.
/// These provide clear explanations of the 3-tier URL architecture
/// and hosting workflow to publishers.
/// </summary>
#pragma warning disable SA1600 // Elements should be documented
#pragma warning disable SA1124 // Do not use regions
public static class PublisherDocumentation
{
    #region Hosting Setup

    /// <summary>Title for hosting setup section.</summary>
    public const string HostingSetupTitle = "Hosting Setup";

    /// <summary>Description for hosting setup section.</summary>
    public const string HostingSetupDescription =
        "GenHub uses Google Drive to host your catalog and artifact files. " +
        "When you connect your Google account, GenHub creates a folder called " +
        "'GenHub_Publisher' in your Drive. All your published files go there.\n\n" +
        "Your URLs remain stable across updates - when you publish changes, " +
        "GenHub updates the existing files rather than creating new ones.";

    /// <summary>Help text for hosting setup section.</summary>
    public const string HostingSetupHelp =
        "Click 'Connect Google Drive' to authorize GenHub to create and manage files " +
        "in your Drive. GenHub only has access to files it creates - not your personal files.";

    #endregion

    #region Publish

    /// <summary>Title for publish section.</summary>
    public const string PublishTitle = "Publish";

    /// <summary>Description for publish section.</summary>
    public const string PublishDescription =
        "When you click Publish:\n" +
        "• New artifacts (ZIPs) are uploaded - each version gets a unique URL\n" +
        "• Catalogs are updated in-place - the URL never changes\n" +
        "• Your definition is updated in-place - the URL never changes\n" +
        "• Subscribers automatically see your changes when they refresh\n\n" +
        "This means your subscription link is permanent. Share it once, " +
        "and it works forever.";

    /// <summary>Help text for first-time publishing.</summary>
    public const string PublishFirstTimeHelp =
        "First time publishing? GenHub will:\n" +
        "1. Create your GenHub_Publisher folder on Drive\n" +
        "2. Upload your publisher.json (definition)\n" +
        "3. Upload your catalog files\n" +
        "4. Upload any artifact files with local paths\n" +
        "5. Generate your permanent subscription link";

    /// <summary>Help text for publishing updates.</summary>
    public const string PublishUpdateHelp =
        "Publishing an update? GenHub will:\n" +
        "1. Upload any new artifacts (new versions)\n" +
        "2. Update your existing catalog files (same URLs)\n" +
        "3. Update your definition if needed (same URL)\n\n" +
        "Subscribers will see your changes automatically.";

    #endregion

    #region Share

    /// <summary>Title for share section.</summary>
    public const string ShareTitle = "Share";

    /// <summary>Description for share section.</summary>
    public const string ShareDescription =
        "This subscription link is your permanent entry point. Share it on:\n" +
        "• Your website or mod page\n" +
        "• Discord servers\n" +
        "• Forums and communities\n\n" +
        "When users click this link (or paste it into GenHub), they'll " +
        "subscribe to all your catalogs and receive updates automatically.";

    /// <summary>Help text for the subscription link.</summary>
    public const string SubscriptionLinkHelp =
        "The subscription link format: genhub://subscribe?url=<your-definition-url>\n\n" +
        "This URL points to your publisher.json file on Google Drive. " +
        "Since GenHub updates the file in-place, the URL never changes.";

    #endregion

    #region Multi-Catalog

    /// <summary>Title for multiple catalogs section.</summary>
    public const string MultiCatalogTitle = "Multiple Catalogs";

    /// <summary>Description for multiple catalogs section.</summary>
    public const string MultiCatalogDescription =
        "You can organize your content into multiple catalogs. For example:\n" +
        "• 'ZH Mods' - your Zero Hour modifications\n" +
        "• 'Maps' - standalone map packs\n" +
        "• 'Generals Content' - content for the base game\n\n" +
        "Each catalog has its own URL but they're all linked through your " +
        "publisher definition. Subscribers can choose which catalogs to follow.";

    /// <summary>Help text for multiple catalogs.</summary>
    public const string MultiCatalogHelp =
        "To add a new catalog:\n" +
        "1. Click '+ Add Catalog' in the toolbar\n" +
        "2. Give it an ID (lowercase, no spaces) and name\n" +
        "3. Add content items to the catalog\n" +
        "4. Publish - the new catalog will be uploaded automatically";

    #endregion

    #region Recovery

    /// <summary>Title for recovery section.</summary>
    public const string RecoveryTitle = "Recovery";

    /// <summary>Description for recovery section.</summary>
    public const string RecoveryDescription =
        "If you lose your local project files, GenHub can recover your " +
        "hosting state from Google Drive. It looks for your 'GenHub_Publisher' " +
        "folder and rebuilds the file mappings.\n\n" +
        "This means you can publish from a new computer without losing your " +
        "subscription links.";

    /// <summary>Help text for recovery.</summary>
    public const string RecoveryHelp =
        "Recovery works by scanning your GenHub_Publisher folder for:\n" +
        "• publisher.json (your definition)\n" +
        "• catalog-*.json files (your catalogs)\n" +
        "• Artifact files\n\n" +
        "GenHub matches these to your project and restores the file IDs.";

    #endregion

    #region Three-Tier Architecture

    /// <summary>Overview of the 3-tier architecture.</summary>
    public const string ArchitectureOverview =
        "GenHub uses a 3-tier URL architecture:\n\n" +
        "Tier 1: Publisher Definition (publisher.json)\n" +
        "• Your identity and catalog locations\n" +
        "• This is what users subscribe to\n" +
        "• URL never changes\n\n" +
        "Tier 2: Catalogs (catalog-*.json)\n" +
        "• Your content items and releases\n" +
        "• Updated in-place when you add releases\n" +
        "• URL never changes\n\n" +
        "Tier 3: Artifacts (*.zip)\n" +
        "• The actual downloadable files\n" +
        "• Each version gets a new file (immutable)\n" +
        "• Old versions remain accessible";

    #endregion
}
