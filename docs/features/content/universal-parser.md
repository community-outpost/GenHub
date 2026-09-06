# Universal Web Page Parser Architecture

## Overview

The Universal Web Page Parser is a provider-agnostic architecture for extracting rich content from web pages. It enables content providers like ModDB, AODMaps, and others to parse web pages and extract structured data including articles, videos, images, files, reviews, and comments.

## Architecture

### Core Components

#### 1. Data Models (`GenHub.Core/Models/Parsers/`)

All parser data models are defined in the `GenHub.Core` project for maximum reusability.

##### `PageType` Enum

Defines the different types of pages that can be parsed:

- `Unknown` - Page type could not be determined
- `List` - Gallery or list view (e.g., addons, images)
- `Summary` - News feed or summary page
- `Detail` - Full detail page with all content sections
- `FileDetail` - Specific file download page

##### `SectionType` Enum

Defines the different types of content sections:

- `Article` - News articles or blog posts
- `Video` - Embedded videos (YouTube, Vimeo, etc.)
- `Image` - Images and screenshots
- `File` - Downloadable files
- `Review` - User reviews with ratings
- `Comment` - User comments

##### `GlobalContext` Record

Contains global information about the page:

```csharp
public record GlobalContext(
    string Title,
    string Developer,
    DateTime? ReleaseDate,
    string? GameName = null,
    string? IconUrl = null,
    string? Description = null
);
```

##### `ContentSection` (Abstract Base Class)

Base class for all content sections:

```csharp
public abstract record ContentSection(
    SectionType Type,
    string Title
);
```

##### Specific Content Type Records

**Article** - News articles or blog posts:

```csharp
public record Article(
    string Title,
    string? Author = null,
    DateTime? PublishDate = null,
    string? Content = null,
    string? Url = null
) : ContentSection(SectionType.Article, Title);
```

**Video** - Embedded videos:

```csharp
public record Video(
    string Title,
    string? ThumbnailUrl = null,
    string? EmbedUrl = null,
    string? Platform = null
) : ContentSection(SectionType.Video, Title);
```

**Image** - Images and screenshots:

```csharp
public record Image(
    string Title,
    string? ThumbnailUrl = null,
    string? FullSizeUrl = null,
    string? Description = null
) : ContentSection(SectionType.Image, Title);
```

**File** - Downloadable files:

```csharp
public record File(
    string Name,
    string? Version = null,
    long? SizeBytes = null,
    string? SizeDisplay = null,
    DateTime? UploadDate = null,
    string? Category = null,
    string? Uploader = null,
    string? DownloadUrl = null,
    string? Md5Hash = null,
    int? CommentCount = null
) : ContentSection(SectionType.File, Title);
```

**Review** - User reviews with ratings:

```csharp
public record Review(
    string? Author = null,
    float? Rating = null,
    string? Content = null,
    DateTime? Date = null,
    int? HelpfulVotes = null
) : ContentSection(SectionType.Review, "Review");
```

**Comment** - User comments:

```csharp
public record Comment(
    string? Author = null,
    string? Content = null,
    DateTime? Date = null,
    int? Karma = null,
    bool? IsCreator = null
) : ContentSection(SectionType.Comment, "Comment");
```

##### `ParsedWebPage` Record

The complete result of parsing a web page:

```csharp
public record ParsedWebPage(
    string Url,
    GlobalContext Context,
    List<ContentSection> Sections,
    PageType PageType
);
```

#### 2. Interfaces

##### `IWebPageParser` (`GenHub.Core/Interfaces/Parsers/IWebPageParser.cs`)

Universal parser interface that all provider-specific parsers implement:

```csharp
public interface IWebPageParser
{
    /// <summary>
    /// Gets the unique identifier for this parser.
    /// </summary>
    string ParserId { get; }

    /// <summary>
    /// Determines whether this parser can handle the given URL.
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <returns>True if this parser can handle the URL; otherwise, false.</returns>
    bool CanParse(string url);

    /// <summary>
    /// Parses the web page at the given URL.
    /// </summary>
    /// <param name="url">The URL to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed web page data.</returns>
    Task<ParsedWebPage> ParseAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses the provided HTML content.
    /// </summary>
    /// <param name="url">The URL the HTML was retrieved from.</param>
    /// <param name="html">The HTML content to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed web page data.</returns>
    Task<ParsedWebPage> ParseAsync(string url, string html, CancellationToken cancellationToken = default);
}
```

##### `IPlaywrightService` (`GenHub.Core/Interfaces/Tools/IPlaywrightService.cs`)

Service for managing Playwright browser instances:

```csharp
public interface IPlaywrightService
{
    /// <summary>
    /// Creates a new Playwright page with optional context options.
    /// </summary>
    Task<IPage> CreatePageAsync(BrowserNewContextOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches HTML content from the given URL.
    /// </summary>
    Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches HTML and parses it into an AngleSharp document.
    /// </summary>
    Task<IDocument> FetchAndParseAsync(string url, CancellationToken cancellationToken = default);
}
```

#### 3. Implementation

##### `PlaywrightService` (`GenHub/GenHub/Features/Content/Services/Tools/PlaywrightService.cs`)

Singleton service that manages a shared Playwright browser instance:

- Uses a semaphore to ensure thread-safe initialization
- Creates a single browser instance shared across all requests
- Provides realistic user agent to bypass WAF/Bot protections
- Implements `IAsyncDisposable` for proper cleanup

##### `ModDBPageParser` (`GenHub/GenHub/Features/Content/Services/Parsers/ModDBPageParser.cs`)

Implementation of `IWebPageParser` for ModDB pages:

**Features:**

- Parses three distinct page types: List, Summary, Detail, FileDetail
- Extracts global context from `.headerbox` elements
- Extracts all content sections (articles, videos, images, files, reviews, comments)
- Uses comprehensive CSS selectors defined in `ModDBParserConstants`

**Page Type Detection:**

- **List**: URLs ending in `/addons`, `/images`, or containing `.table .row.rowcontent`
- **Summary**: Pages with `#articlesbrowse` element
- **Detail**: Pages with `.headerbox` but no specific list/summary indicators
- **FileDetail**: Pages with `#downloadsinfo` element

**Content Extraction:**

- **Files**: Extracts from `.table .row.file` or `tr.file` elements
- **Videos**: Extracts from `iframe` elements with YouTube/Vimeo URLs
- **Images**: Extracts from `.mediarow`, `.screenshot`, or `.imagebox` elements
- **Articles**: Extracts from `.article`, `.newsitem`, or `.post` elements
- **Reviews**: Extracts from `.review` elements with rating information
- **Comments**: Extracts from `.comment` elements with karma/creator badges

##### `ModDBParserConstants` (`GenHub/GenHub.Core/Constants/ModDBParserConstants.cs`)

Contains all CSS selectors for ModDB page parsing:

- Global Context selectors
- Page Type Detection selectors
- Content section selectors (Files, Videos, Images, Articles, Reviews, Comments)
- Pagination selectors
- URL pattern constants

#### 4. Integration

##### `ModDBResolver` (`GenHub/GenHub/Features/Content/Services/ContentResolvers/ModDBResolver.cs`)

Updated to use the universal parser:

```csharp
public class ModDBResolver(
    HttpClient httpClient,
    ModDBManifestFactory manifestFactory,
    IWebPageParser webPageParser,  // Injected via DI
    ILogger<ModDBResolver> logger) : IContentResolver
{
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        // Parse the web page
        var parsedPage = await _webPageParser.ParseAsync(
            discoveredItem.SourceUrl,
            cancellationToken);

        // Store parsed page in search result for UI display
        discoveredItem.SetData(parsedPage);

        // Extract primary download URL
        var primaryDownloadUrl = ExtractPrimaryDownloadUrl(parsedPage);

        // Convert to MapDetails for manifest factory
        var mapDetails = ConvertToMapDetails(parsedPage, discoveredItem, primaryDownloadUrl);

        // Create manifest
        var manifest = await _manifestFactory.CreateManifestAsync(
            mapDetails,
            discoveredItem.SourceUrl);

        return OperationResult<ContentManifest>.CreateSuccess(manifest);
    }
}
```

##### `ContentDetailViewModel` (`GenHub/GenHub/Features/Downloads/ViewModels/ContentDetailViewModel.cs`)

Updated to display rich content from parsed pages:

```csharp
public partial class ContentDetailViewModel : ObservableObject
{
    [ObservableProperty]
    private ParsedWebPage? _parsedPage;

    public ObservableCollection<Article> Articles =>
        ParsedPage?.Sections.OfType<Article>().ToObservableCollection() ?? new();

    public ObservableCollection<Video> Videos =>
        ParsedPage?.Sections.OfType<Video>().ToObservableCollection() ?? new();

    public ObservableCollection<Image> Images =>
        ParsedPage?.Sections.OfType<Image>().ToObservableCollection() ?? new();

    public ObservableCollection<File> Files =>
        ParsedPage?.Sections.OfType<File>().ToObservableCollection() ?? new();

    public ObservableCollection<Review> Reviews =>
        ParsedPage?.Sections.OfType<Review>().ToObservableCollection() ?? new();

    public ObservableCollection<Comment> Comments =>
        ParsedPage?.Sections.OfType<Comment>().ToObservableCollection() ?? new();
}
```

##### Dependency Injection (`GenHub/GenHub/Infrastructure/DependencyInjection/ContentPipelineModule.cs`)

```csharp
private static void AddModDBPipeline(IServiceCollection services)
{
    // Register Playwright service as singleton
    services.AddSingleton<IPlaywrightService, PlaywrightService>();

    // Register ModDB page parser
    services.AddTransient<IWebPageParser, ModDBPageParser>();

    // ... other ModDB services
}
```

## Usage

### Creating a New Parser

To create a parser for a new provider (e.g., AODMaps):

1. **Create constants file** for CSS selectors:

   ```csharp
   // GenHub/GenHub.Core/Constants/AODMapsParserConstants.cs
   public static class AODMapsParserConstants
   {
       public static class GlobalContext
       {
           public const string TitleSelector = "h1.title";
           public const string AuthorSelector = ".author";
           // ...
       }
   }
   ```

2. **Implement IWebPageParser**:

   ```csharp
   public class AODMapsPageParser : IWebPageParser
   {
       public string ParserId => "AODMaps";

       private readonly IPlaywrightService _playwrightService;
       private readonly ILogger<AODMapsPageParser> _logger;

       public bool CanParse(string url) =>
           url.Contains("aodmaps.com", StringComparison.OrdinalIgnoreCase);

       public async Task<ParsedWebPage> ParseAsync(string url, CancellationToken cancellationToken = default)
       {
           var document = await _playwrightService.FetchAndParseAsync(url, cancellationToken);
           // Parse and return ParsedWebPage
       }
   }
   ```

3. **Register in DI**:

   ```csharp
   private static void AddAODMapsPipeline(IServiceCollection services)
   {
       services.AddTransient<IWebPageParser, AODMapsPageParser>();
       // ...
   }
   ```

### Using Parsed Content in UI

The parsed content is automatically stored in `ContentSearchResult.Data` and can be accessed in the ViewModel:

```csharp
// In ContentDetailViewModel
private void LoadRichContent()
{
    var parsedPage = _searchResult.GetData<ParsedWebPage>();
    if (parsedPage == null) return;

    ParsedPage = parsedPage;
}

// Bind to UI
<ItemsControl ItemsSource="{Binding Articles}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding Title}" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

## Benefits

1. **Provider-Agnostic**: The same interface works for any web content provider
2. **Reusable**: Data models and interfaces are in `GenHub.Core` for maximum reuse
3. **Extensible**: Easy to add new content types or providers
4. **Testable**: Parsers can be tested with mock HTML content
5. **Efficient**: Shared Playwright browser instance reduces resource usage
6. **Rich Content**: Extracts comprehensive content beyond basic metadata

## Future Enhancements

- Add caching for parsed pages to reduce redundant requests
- Implement pagination support for list pages
- Add support for more content types (e.g., polls, forums)
- Create parser registry for automatic parser selection
- Add unit tests for all parser implementations
