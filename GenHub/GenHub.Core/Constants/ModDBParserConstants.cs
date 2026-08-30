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
    public const string MainDownloadButtonSelector = "a.download, a.downloadarea, .downloadbutton a, a[href*='/downloads/start/']";

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
    public const string FullDescriptionSelector = "#articlebrowse, .summary .content, .description .content, .modtext";

    /// <summary>Selector for truncated summary.</summary>
    public const string SummarySelector = ".summary p, .description p";

    // ===== Legacy File Selectors =====

    /// <summary>Selector for files table.</summary>
    public const string FilesTableSelector = "table.filelist, .table.files, #files";

    /// <summary>Selector for individual file rows.</summary>
    public const string FileRowSelector = "tr.file, .row.file, .file";

    /// <summary>Selector for file name.</summary>
    public const string FileNameSelector = "h5, h4, .name, .title";

    /// <summary>Selector for file version.</summary>
    public const string FileVersionSelector = ".version, .ver";

    /// <summary>Selector for file size.</summary>
    public const string FileSizeSelector = ".size, .filesize";

    /// <summary>Selector for file upload date.</summary>
    public const string FileDateSelector = "time[datetime], .date, .uploaded";

    /// <summary>Selector for file category.</summary>
    public const string FileCategorySelector = ".category, .type";

    /// <summary>Selector for file uploader.</summary>
    public const string FileUploaderSelector = ".uploader, .author, a[href*='/members/']";

    /// <summary>Selector for file download link (robust).</summary>
    public const string FileDownloadSelector = "a.button.download, a[href*='/downloads/start/'], .download a";

    /// <summary>Selector for file MD5 hash.</summary>
    public const string FileMd5Selector = ".md5, .hash";

    /// <summary>Selector for file comment count.</summary>
    public const string FileCommentCountSelector = ".comments, .commentcount";

    // ===== Videos Section Selectors =====

    /// <summary>Selector for embedded video iframes.</summary>
    public const string VideoSelector = "iframe[src*='youtube'], iframe[src*='vimeo'], iframe[src*='youtu.be']";

    /// <summary>Selector for video thumbnails.</summary>
    public const string VideoThumbnailSelector = ".thumbnail img, .preview img";

    /// <summary>Selector for video titles.</summary>
    public const string VideoTitleSelector = ".title, h3, h4";

    // ===== Images Section Selectors =====

    /// <summary>Selector for image gallery container.</summary>
    public const string ImageGallerySelector = ".mediarow, .screenshot, .imagebox, .gallery";

    /// <summary>Selector for individual images.</summary>
    public const string ImageSelector = "img";

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

    /// <summary>Selector for comments container.</summary>
    public const string CommentsSelector = ".comment, .post, .comments";

    /// <summary>Selector for individual comment rows.</summary>
    public const string CommentRowSelector = ".comment, .post";

    /// <summary>Selector for comment authors.</summary>
    public const string CommentAuthorSelector = ".author, .username, a[href*='/members/']";

    /// <summary>Selector for comment content.</summary>
    public const string CommentContentSelector = ".content, .body, .text";

    /// <summary>Selector for comment dates.</summary>
    public const string CommentDateSelector = "time[datetime], .date";

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
}
