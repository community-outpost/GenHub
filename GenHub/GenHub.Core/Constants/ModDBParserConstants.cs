namespace GenHub.Core.Constants;

/// <summary>
/// CSS selectors and constants for parsing ModDB web pages.
/// Used by ModDBPageParser to extract content from ModDB pages.
/// </summary>
public static class ModDBParserConstants
{
    // ===== Global Context Selectors =====

    /// <summary>Selector for the header box containing global context.</summary>
    public const string HeaderBoxSelector = ".headerbox";

    /// <summary>Selector for the title in the header.</summary>
    public const string TitleSelector = "h1, h2, .title";

    /// <summary>Selector for developer/publisher links.</summary>
    public const string DeveloperSelector = "a[href*='/members/'], a[href*='/company/']";

    /// <summary>Selector for the profile/mod info box that carries the real developer name.</summary>
    public const string DeveloperProfileSelector = "#modsinfo a[href*='/members/'], #modsinfo a[href*='/company/'], .sidecolumn a[href*='/members/'], .sidecolumn a[href*='/company/']";

    /// <summary>Selector for release date.</summary>
    public const string ReleaseDateSelector = "time[datetime], .date, .released";

    /// <summary>Selector for game name.</summary>
    public const string GameNameSelector = ".game, .parentgame";

    /// <summary>Selector for icon/preview image.</summary>
    public const string IconSelector = "img.icon, .icon img, .preview img";

    /// <summary>Selector for description.</summary>
    public const string DescriptionSelector = ".description, .summary, p[itemprop='description']";

    // ===== Page Type Detection Selectors =====

    /// <summary>Selector for articles browse section (indicates summary/news page).</summary>
    public const string ArticlesBrowseSelector = "#articlesbrowse";

    /// <summary>Selector for downloads info section (indicates file detail page).</summary>
    public const string DownloadsInfoSelector = "#downloadsinfo";

    /// <summary>Selector for table elements (indicates list view).</summary>
    public const string TableSelector = ".table";

    /// <summary>Selector for row content elements (indicates list view).</summary>
    public const string RowContentSelector = ".row.rowcontent";

    // ===== File Detail Page Selectors =====
    // These target the metadata table on /downloads/ pages

    /// <summary>Selector for the file metadata table container.</summary>
    public const string FileMetadataContainerSelector = ".table, table.table, #downloadsfiles";

    /// <summary>Selector for individual rows in the metadata table.</summary>
    public const string FileMetadataRowSelector = "tr";

    /// <summary>Selector for row label cell (first td).</summary>
    public const string FileMetadataLabelSelector = "td:first-child";

    /// <summary>Selector for row value cell (second td).</summary>
    public const string FileMetadataValueSelector = "td:last-child";

    /// <summary>Selector for the main download button on file pages.</summary>
    public const string MainDownloadButtonSelector = "a.download, a.downloadarea, .downloadbutton a, a[href*='/downloads/start/'], a[href*='/addons/start/']";

    /// <summary>Selector for download size on the button.</summary>
    public const string DownloadSizeSelector = ".download .size, .downloadbutton .size";

    // ===== Profile Sidebar Selectors (right column) =====

    /// <summary>Selector for the profile sidebar container.</summary>
    public const string ProfileSidebarSelector = ".sidecolumn, aside, #sidecolumn, #profile";

    /// <summary>Selector for profile box within sidebar.</summary>
    public const string ProfileBoxSelector = ".profilebox, .profile";

    /// <summary>Selector for rows in the profile sidebar.</summary>
    public const string ProfileRowSelector = ".row, tr";

    /// <summary>Selector for the label of a profile row.</summary>
    public const string ProfileLabelSelector = "h5, .rowlabel, td:first-child, .label";

    /// <summary>Selector for the content of a profile row.</summary>
    public const string ProfileContentSelector = "span, a, td:last-child, .content";

    /// <summary>Selector for profile icon/avatar.</summary>
    public const string ProfileIconSelector = ".avatar img, .iconbox img, img.icon";

    // ===== Description/Summary Selectors =====

    /// <summary>Selector for full description content.</summary>
    public const string FullDescriptionSelector = "#downloaddescription, #downloadsummary, #articlebrowse .articlebody, .articlebody, #modsummary, .modtext, #profile .description, #description, #articlebrowse, .summary .content, .description .content";

    /// <summary>Selector for the file-page body copy (not the breadcrumb .summary trail).</summary>
    public const string FileDescriptionSelector = "#downloaddescription, #downloadsummary";

    /// <summary>Selector for summary or description container.</summary>
    public const string SummarySelector = ".description, .rubric, p[itemprop='description']";

    // ===== Legacy File Selectors =====

    /// <summary>Selector for files table.</summary>
    public const string FilesTableSelector = "table.filelist, .table.files, #files";

    /// <summary>Selector for individual file rows.</summary>
    public const string FileRowSelector = "tr.file, .row.file, .file, .row.rowcontent";

    /// <summary>Selector for file name.</summary>
    public const string FileNameSelector = "h4 a, h5 a, h3 a, .heading a, .title a, a.title, .name a, h5, h4, .name, .title";

    /// <summary>Selector for file version.</summary>
    public const string FileVersionSelector = ".version, .ver";

    /// <summary>Selector for file size.</summary>
    public const string FileSizeSelector = ".size, .filesize, .filesizes, span.size";

    /// <summary>Selector for file subheading or metadata row.</summary>
    public const string FileSubheadingSelector = ".subheading, span.subheading, .meta, .details, .info, p.summary, .summary";

    /// <summary>Selector for file upload date.</summary>
    public const string FileDateSelector = "time[datetime], .date, .uploaded, time";

    /// <summary>Selector for file category.</summary>
    public const string FileCategorySelector = ".category, .type, span.category";

    /// <summary>Selector for file uploader.</summary>
    public const string FileUploaderSelector = ".uploader, .author, a[href*='/members/'], a[href*='/company/']";

    /// <summary>Selector for file download link (robust).</summary>
    public const string FileDownloadSelector = "a.button, a.buttonlarge, a.download, a.btn, a[href*='/downloads/start/'], a[href*='/addons/start/'], a[href*='/downloads/'], a[href*='/addons/'], .download a, .actions a";

    /// <summary>Selector for file MD5 hash.</summary>
    public const string FileMd5Selector = ".md5, .hash";

    /// <summary>Selector for file comment count.</summary>
    public const string FileCommentCountSelector = ".comments, .commentcount";

    // ===== Videos Section Selectors =====

    /// <summary>Selector for embedded video iframes.</summary>
    public const string VideoSelector = "iframe[src*='youtube'], iframe[src*='youtube-nocookie'], iframe[src*='youtu.be'], iframe[src*='vimeo'], iframe[src*='dailymotion'], iframe[src*='moddb.com/media/iframe'], iframe[src*='moddb.com/media/embed'], iframe[src*='moddb.com/videos/iframe'], iframe[src*='moddb.com/videos/embed']";

    /// <summary>Selector for video gallery containers and items.</summary>
    public const string VideoGallerySelector = "#videobox, #videosbrowse, #mediabrowse, .mediarow, .mediabox";

    /// <summary>Selector for video links.</summary>
    public const string VideoLinkSelector = "a[href*='/videos/'], a[href*='youtube.com/watch'], a[href*='youtu.be/'], a[href*='vimeo.com/']";

    /// <summary>Selector for video thumbnails.</summary>
    public const string VideoThumbnailSelector = ".thumbnail img, .preview img, img";

    /// <summary>Selector for video titles.</summary>
    public const string VideoTitleSelector = ".title, h3, h4, h5, .caption";

    /// <summary>Selector for recommendation and related content sections.</summary>
    public const string RecommendationsSelector = "#recommendations, .recommendations, #related, .related, #similar, .similar, #fansalsoviewed, .fansalsoviewed, .youmayalso, [class*='recommend'], [id*='recommend'], [class*='similar'], [id*='similar']";

    // ===== Images Section Selectors =====

    /// <summary>Selector for image gallery container.</summary>
    public const string ImageGallerySelector = "#imagebox, #mediaimage, #imagebrowse, #mediabrowse, .mediarow";

    /// <summary>
    /// Selector for gallery images only. Deliberately excludes a blanket
    /// <c>img[src*='media.moddb.com']</c> match, which previously pulled game icons, member
    /// avatars, and file-page chrome into the Media tab.
    /// </summary>
    public const string GalleryImageSelector = "#imagebox img, #mediaimage img, #imagebrowse img, #mediabrowse img, .mediarow img, .media .holder img, #downloadsummary img, #downloaddescription img, .preview img, a[href*='/mods/'][href*='/images/'] img";

    /// <summary>Selector for individual images.</summary>
    public const string ImageSelector = "img";

    /// <summary>Sidebar/profile containers whose images are icons and avatars, not gallery media.</summary>
    public const string ImageSidebarSelector = "#modsinfo, #downloadsprofilemenu, #profile, .sidecolumn, aside";

    /// <summary>Selector for image thumbnails.</summary>
    public const string ImageThumbnailSelector = ".thumbnail img, .thumb img";

    /// <summary>Selector for full-size image links.</summary>
    public const string ImageFullSizeSelector = "a[href*='/images/'], a.image";

    /// <summary>Selector for image captions/descriptions.</summary>
    public const string ImageCaptionSelector = ".caption, .description, .alt";

    // ===== Articles Section Selectors =====

    /// <summary>Selector for articles container.</summary>
    public const string ArticlesSelector = ".article, .newsitem, .post";

    /// <summary>Selector for article titles.</summary>
    public const string ArticleTitleSelector = "h3, h4, .title";

    /// <summary>Selector for article dates.</summary>
    public const string ArticleDateSelector = "time[datetime], .date, .published";

    /// <summary>Selector for article authors.</summary>
    public const string ArticleAuthorSelector = ".author, a[href*='/members/']";

    /// <summary>Selector for article content.</summary>
    public const string ArticleContentSelector = ".content, .body, .summary";

    /// <summary>Selector for article links.</summary>
    public const string ArticleLinkSelector = "a[href*='/news/'], a[href*='/articles/']";

    // ===== Reviews Section Selectors =====

    /// <summary>Selector for reviews container.</summary>
    public const string ReviewsSelector = ".review, .rating, .reviews";

    /// <summary>Selector for review authors.</summary>
    public const string ReviewAuthorSelector = ".author, a[href*='/members/']";

    /// <summary>Selector for review ratings.</summary>
    public const string ReviewRatingSelector = ".rating, .score, .stars";

    /// <summary>Selector for review content.</summary>
    public const string ReviewContentSelector = ".content, .body, .text";

    /// <summary>Selector for review dates.</summary>
    public const string ReviewDateSelector = "time[datetime], .date";

    /// <summary>Selector for helpful votes.</summary>
    public const string ReviewHelpfulSelector = ".helpful, .votes, .karma";

    // ===== Comments Section Selectors =====

    /// <summary>Selector for comments container. Do not use #commentform — that is the composer.</summary>
    public const string CommentsSelector = "#commentsbrowse";

    /// <summary>
    /// Selector for posted comment rows. Requires the exact <c>rowcomment</c> class so the
    /// composer rows (<c>rowcommentguest</c>, <c>rowcommentsummary</c>, <c>rowcommentemail</c>)
    /// and <c>#commentform</c> are not treated as comments.
    /// </summary>
    public const string CommentRowSelector = ".row.rowcomment, .rowcomment";

    /// <summary>Selector for comment authors.</summary>
    public const string CommentAuthorSelector = ".author, .username, .heading a, a[href*='/members/']";

    /// <summary>Selector for comment content. Avoids bare <c>p</c> which matches login chrome and CSS blobs.</summary>
    public const string CommentContentSelector = ":scope > .commentbody, .commentbody, p.comment";

    /// <summary>Selector for comment dates.</summary>
    public const string CommentDateSelector = "time[datetime], time, .date, .datetime, span.subheading";

    /// <summary>Selector for comment karma/votes.</summary>
    public const string CommentKarmaSelector = ".karma, .votes, .goodkarma, .badkarma";

    /// <summary>Selector for creator badge.</summary>
    public const string CommentCreatorSelector = ".creator, .badge";

    // ===== Pagination Selectors =====

    /// <summary>Selector for pagination container.</summary>
    public const string PaginationSelector = ".pagination, .pages";

    /// <summary>Selector for pagination links.</summary>
    public const string PaginationLinkSelector = "a[href*='page=']";

    // ===== URL Patterns =====

    /// <summary>Pattern for mods URLs.</summary>
    public const string ModsUrlPattern = "/mods/";

    /// <summary>Pattern for downloads URLs.</summary>
    public const string DownloadsUrlPattern = "/downloads/";

    /// <summary>Pattern for addons URLs.</summary>
    public const string AddonsUrlPattern = "/addons/";

    /// <summary>Pattern for images URLs.</summary>
    public const string ImagesUrlPattern = "/images/";

    /// <summary>Pattern for news/articles URLs.</summary>
    public const string NewsUrlPattern = "/news/";

    /// <summary>Pattern for games URLs.</summary>
    public const string GamesUrlPattern = "/games/";

    // ===== Mod Detail Page Selectors =====

    /// <summary>Selector for the downloads section on mod pages.</summary>
    public const string DownloadsSectionSelector = "#downloads, .downloads, .files";

    /// <summary>Selector for the addons section on mod pages.</summary>
    public const string AddonsSectionSelector = "#addons, .addons";

    /// <summary>Selector for the tabs/navigation on mod pages.</summary>
    public const string TabsSelector = ".tabs, .navigation, nav";

    /// <summary>Selector for individual tab links.</summary>
    public const string TabLinkSelector = "a[href*='/downloads'], a[href*='/addons']";

    // ===== Metadata Keys (Internal/Normalized) =====

    /// <summary>Metadata key for filename.</summary>
    public const string MetadataFilename = "filename";

    /// <summary>Alternative metadata key for filename.</summary>
    public const string MetadataFileNameAlt = "file name";

    /// <summary>Alternative metadata key for file.</summary>
    public const string MetadataFileAlt = "file";

    /// <summary>Metadata key for size.</summary>
    public const string MetadataSize = "size";

    /// <summary>Alternative metadata key for size.</summary>
    public const string MetadataFileSizeAlt = "file size";

    /// <summary>Metadata key for uploader.</summary>
    public const string MetadataUploader = "uploader";

    /// <summary>Alternative metadata key for uploaded by.</summary>
    public const string MetadataUploadedBy = "uploaded by";

    /// <summary>Alternative metadata key for author.</summary>
    public const string MetadataAuthor = "author";

    /// <summary>Metadata key for category.</summary>
    public const string MetadataCategory = "category";

    /// <summary>Alternative metadata key for file category.</summary>
    public const string MetadataFileCategory = "file category";

    /// <summary>Alternative metadata key for type.</summary>
    public const string MetadataType = "type";

    /// <summary>Metadata key for MD5 hash.</summary>
    public const string MetadataMd5Hash = "md5 hash";

    /// <summary>Metadata key for MD5 hash (alternative).</summary>
    public const string MetadataMd5HashAlt = "md5hash";

    /// <summary>Alternative metadata key for MD5 checksum.</summary>
    public const string MetadataMd5Checksum = "md5 checksum";

    /// <summary>Alternative metadata key for MD5.</summary>
    public const string MetadataMd5 = "md5";

    /// <summary>Alternative metadata key for hash.</summary>
    public const string MetadataHash = "hash";

    /// <summary>Alternative metadata key for checksum.</summary>
    public const string MetadataChecksum = "checksum";

    /// <summary>Metadata key for total downloads.</summary>
    public const string MetadataTotalDownloads = "total downloads";

    /// <summary>Alternative metadata key for download count.</summary>
    public const string MetadataDownloadCount = "download count";

    /// <summary>Metadata key for added date.</summary>
    public const string MetadataAdded = "added";

    /// <summary>Metadata key for updated date.</summary>
    public const string MetadataUpdated = "updated";

    // ===== Additional Selectors =====

    /// <summary>Selector for fallback titles (h1, h2, etc).</summary>
    public const string FallbackTitleSelector = "h2 a, h1 a, h2, h1";

    /// <summary>Selector for file detail page title heading outside the global headerbox.</summary>
    public const string FilePageTitleSelector = ".midcolumn h2, .columncenter h2, #downloadsfiles h2, #downloadsinfo h2, #downloads h2, .heading h2, .title h2, h2.title, .midcolumn h3, .heading h3";

    /// <summary>Selector for file detail page preview images.</summary>
    public const string FilePreviewImagesSelector = "#downloadmedia img, #downloadsmedia img, #preview img, #media img, .mediagallery img, .imagebox img, #imagebox img, #downloaddescription img, #downloadsummary img, a[href*='/images/'] img, .previewholder img, .media .holder img";

    /// <summary>Selector for file description container elements.</summary>
    public const string FileDescriptionContainerSelector = "#downloaddescription, #downloadsummary, #description, .description, .articlebody, #profiletotal";

    /// <summary>Regex pattern for extracting parent mod path.</summary>
    public const string ParentModPathRegex = @"(/mods/[^/]+)/(?:downloads|addons)/";
}
