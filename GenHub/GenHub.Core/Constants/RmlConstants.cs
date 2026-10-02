namespace GenHub.Core.Constants;

/// <summary>
/// Constants for RmlUi interface documents (.rml) and style sheets (.rcss) used by
/// RmlUi-enabled game clients. Screens live in a UI folder next to the game data and
/// mirror the legacy window definition (.wnd) screens, which stay the data model.
/// </summary>
public static class RmlConstants
{
    /// <summary>
    /// File level constants for RmlUi documents and style sheets.
    /// </summary>
    public static class File
    {
        /// <summary>File extension for RmlUi documents.</summary>
        public const string Extension = ".rml";

        /// <summary>File extension for RmlUi style sheets.</summary>
        public const string RcssExtension = ".rcss";

        /// <summary>File pattern matching RmlUi documents.</summary>
        public const string Pattern = "*.rml";

        /// <summary>File pattern matching RmlUi style sheets.</summary>
        public const string RcssPattern = "*.rcss";

        /// <summary>Directory holding the interface screens of a game or mod.</summary>
        public const string UiDirectoryName = "UI";

        /// <summary>Shared style sheet imported by every interface screen.</summary>
        public const string SharedStyleSheetFileName = "common.rcss";

        /// <summary>Link type identifying style sheet references in document heads.</summary>
        public const string StyleSheetLinkType = "text/rcss";
    }

    /// <summary>
    /// Document structure element names.
    /// </summary>
    public static class Document
    {
        /// <summary>Root element of every RmlUi document.</summary>
        public const string Root = "rml";

        /// <summary>Head element holding links, titles, and embedded styles.</summary>
        public const string Head = "head";

        /// <summary>Body element holding the visible interface.</summary>
        public const string Body = "body";

        /// <summary>Document title element.</summary>
        public const string Title = "title";

        /// <summary>External resource reference element.</summary>
        public const string Link = "link";

        /// <summary>Embedded style sheet element.</summary>
        public const string Style = "style";

        /// <summary>Document metadata element.</summary>
        public const string Meta = "meta";

        /// <summary>Script element, preserved but not executed by the game client.</summary>
        public const string Script = "script";
    }

    /// <summary>
    /// Interface element names supported by RmlUi documents.
    /// </summary>
    public static class Elements
    {
        /// <summary>Generic block container.</summary>
        public const string Div = "div";

        /// <summary>Paragraph of text.</summary>
        public const string Paragraph = "p";

        /// <summary>Generic inline container.</summary>
        public const string Span = "span";

        /// <summary>Top-level heading.</summary>
        public const string Heading1 = "h1";

        /// <summary>Second-level heading.</summary>
        public const string Heading2 = "h2";

        /// <summary>Third-level heading.</summary>
        public const string Heading3 = "h3";

        /// <summary>Fourth-level heading.</summary>
        public const string Heading4 = "h4";

        /// <summary>Image element.</summary>
        public const string Image = "img";

        /// <summary>Form input element.</summary>
        public const string Input = "input";

        /// <summary>Drop-down selection element.</summary>
        public const string Select = "select";

        /// <summary>Single selection option.</summary>
        public const string Option = "option";

        /// <summary>Group of selection options.</summary>
        public const string OptionGroup = "optgroup";

        /// <summary>Multi-line text entry element.</summary>
        public const string TextArea = "textarea";

        /// <summary>Push button element.</summary>
        public const string Button = "button";

        /// <summary>Input label element.</summary>
        public const string Label = "label";

        /// <summary>Form container element.</summary>
        public const string Form = "form";

        /// <summary>Table element.</summary>
        public const string Table = "table";

        /// <summary>Table head section.</summary>
        public const string TableHead = "thead";

        /// <summary>Table body section.</summary>
        public const string TableBody = "tbody";

        /// <summary>Table foot section.</summary>
        public const string TableFoot = "tfoot";

        /// <summary>Table row element.</summary>
        public const string TableRow = "tr";

        /// <summary>Table cell element.</summary>
        public const string TableCell = "td";

        /// <summary>Table header cell element.</summary>
        public const string TableHeaderCell = "th";

        /// <summary>Unordered list element.</summary>
        public const string UnorderedList = "ul";

        /// <summary>Ordered list element.</summary>
        public const string OrderedList = "ol";

        /// <summary>List item element.</summary>
        public const string ListItem = "li";

        /// <summary>Hyperlink element.</summary>
        public const string Anchor = "a";

        /// <summary>Line break element.</summary>
        public const string Break = "br";

        /// <summary>Horizontal rule element.</summary>
        public const string HorizontalRule = "hr";

        /// <summary>Preformatted text element.</summary>
        public const string Preformatted = "pre";

        /// <summary>Inline code element.</summary>
        public const string Code = "code";

        /// <summary>Emphasized inline text element.</summary>
        public const string Emphasis = "em";

        /// <summary>Strong inline text element.</summary>
        public const string Strong = "strong";

        /// <summary>Progress bar element.</summary>
        public const string Progress = "progress";

        /// <summary>Range slider element.</summary>
        public const string Range = "range";

        /// <summary>Tab set container element.</summary>
        public const string TabSet = "tabset";

        /// <summary>Single tab element.</summary>
        public const string Tab = "tab";

        /// <summary>Tab panel element.</summary>
        public const string Panel = "panel";

        /// <summary>Handle element used by draggable panels.</summary>
        public const string Handle = "handle";

        /// <summary>Custom map preview element provided by the game client.</summary>
        public const string MapPreview = "mappreview";

        /// <summary>Custom scrolling log element provided by the game client.</summary>
        public const string ScrollLog = "scrolllog";

        /// <summary>
        /// Void elements that never have children or closing tags.
        /// </summary>
        public static readonly string[] Void =
        [
            Break, HorizontalRule, Image, Input,
            Document.Link, Document.Meta,
        ];
    }

    /// <summary>
    /// Standard element attribute names.
    /// </summary>
    public static class Attributes
    {
        /// <summary>Unique element identifier.</summary>
        public const string Id = "id";

        /// <summary>Space-separated style class names.</summary>
        public const string Class = "class";

        /// <summary>Inline style declarations.</summary>
        public const string Style = "style";

        /// <summary>Image or media source path.</summary>
        public const string Src = "src";

        /// <summary>Link target path.</summary>
        public const string Href = "href";

        /// <summary>External resource media query.</summary>
        public const string Media = "media";

        /// <summary>External resource relationship.</summary>
        public const string Rel = "rel";

        /// <summary>Input control type.</summary>
        public const string Type = "type";

        /// <summary>Control value.</summary>
        public const string Value = "value";

        /// <summary>Control name.</summary>
        public const string Name = "name";

        /// <summary>Advisory title text.</summary>
        public const string Title = "title";

        /// <summary>Alternative text for images.</summary>
        public const string Alt = "alt";

        /// <summary>Preferred element width.</summary>
        public const string Width = "width";

        /// <summary>Preferred element height.</summary>
        public const string Height = "height";

        /// <summary>Disabled state flag.</summary>
        public const string Disabled = "disabled";

        /// <summary>Checked state flag.</summary>
        public const string Checked = "checked";

        /// <summary>Selected state flag.</summary>
        public const string Selected = "selected";

        /// <summary>Read-only state flag.</summary>
        public const string ReadOnly = "readonly";

        /// <summary>Multiple selection flag.</summary>
        public const string Multiple = "multiple";

        /// <summary>Visible option count for selection lists.</summary>
        public const string Size = "size";

        /// <summary>Visible row count for text areas.</summary>
        public const string Rows = "rows";

        /// <summary>Visible column count for text areas.</summary>
        public const string Cols = "cols";

        /// <summary>Maximum input length.</summary>
        public const string MaxLength = "maxlength";

        /// <summary>Input placeholder text.</summary>
        public const string Placeholder = "placeholder";

        /// <summary>Keyboard tab order index.</summary>
        public const string TabIndex = "tabindex";

        /// <summary>Text direction hint for bidirectional documents.</summary>
        public const string Dir = "dir";

        /// <summary>Minimum value of a range or progress control.</summary>
        public const string Min = "min";

        /// <summary>Maximum value of a range or progress control.</summary>
        public const string Max = "max";

        /// <summary>Column span of a table cell.</summary>
        public const string ColSpan = "colspan";

        /// <summary>Row span of a table cell.</summary>
        public const string RowSpan = "rowspan";
    }

    /// <summary>
    /// Data binding attribute names evaluated against the screen data model.
    /// </summary>
    public static class DataAttributes
    {
        /// <summary>Repeats an element once per item of a container.</summary>
        public const string For = "data-for";

        /// <summary>Conditionally includes an element.</summary>
        public const string If = "data-if";

        /// <summary>Conditionally includes an element when a previous condition failed.</summary>
        public const string ElseIf = "data-else-if";

        /// <summary>Includes an element when previous conditions failed.</summary>
        public const string Else = "data-else";

        /// <summary>Two-way value binding for form controls.</summary>
        public const string Value = "data-value";

        /// <summary>Prefix for event handler bindings such as data-event-click.</summary>
        public const string EventPrefix = "data-event-";

        /// <summary>Prefix for attribute bindings such as data-attr-disabled.</summary>
        public const string AttrPrefix = "data-attr-";

        /// <summary>Prefix for class toggle bindings such as data-class-open.</summary>
        public const string ClassPrefix = "data-class-";

        /// <summary>Prefix for visibility bindings such as data-visible.</summary>
        public const string Visible = "data-visible";

        /// <summary>Click event binding.</summary>
        public const string EventClick = "data-event-click";

        /// <summary>Change event binding.</summary>
        public const string EventChange = "data-event-change";

        /// <summary>Mouse over event binding.</summary>
        public const string EventMouseOver = "data-event-mouseover";

        /// <summary>Mouse out event binding.</summary>
        public const string EventMouseOut = "data-event-mouseout";

        /// <summary>Focus event binding.</summary>
        public const string EventFocus = "data-event-focus";

        /// <summary>Blur event binding.</summary>
        public const string EventBlur = "data-event-blur";

        /// <summary>Submit event binding.</summary>
        public const string EventSubmit = "data-event-submit";
    }

    /// <summary>
    /// Units of measurement accepted by style sheet lengths.
    /// </summary>
    public static class Units
    {
        /// <summary>Density-independent pixel, the preferred interface unit.</summary>
        public const string Dp = "dp";

        /// <summary>Physical pixel.</summary>
        public const string Px = "px";

        /// <summary>Percentage of the containing block.</summary>
        public const string Percent = "%";

        /// <summary>Point unit.</summary>
        public const string Pt = "pt";

        /// <summary>Multiple of the current font size.</summary>
        public const string Em = "em";

        /// <summary>Multiple of the root font size.</summary>
        public const string Rem = "rem";
    }

    /// <summary>
    /// Well-known style sheet property names offered by the style editor.
    /// </summary>
    public static class StyleProperties
    {
        /// <summary>Element display mode.</summary>
        public const string Display = "display";

        /// <summary>Element positioning scheme.</summary>
        public const string Position = "position";

        /// <summary>Offset from the top edge.</summary>
        public const string Top = "top";

        /// <summary>Offset from the right edge.</summary>
        public const string Right = "right";

        /// <summary>Offset from the bottom edge.</summary>
        public const string Bottom = "bottom";

        /// <summary>Offset from the left edge.</summary>
        public const string Left = "left";

        /// <summary>Preferred element width.</summary>
        public const string Width = "width";

        /// <summary>Preferred element height.</summary>
        public const string Height = "height";

        /// <summary>Minimum element width.</summary>
        public const string MinWidth = "min-width";

        /// <summary>Minimum element height.</summary>
        public const string MinHeight = "min-height";

        /// <summary>Maximum element width.</summary>
        public const string MaxWidth = "max-width";

        /// <summary>Maximum element height.</summary>
        public const string MaxHeight = "max-height";

        /// <summary>Outer spacing shorthand.</summary>
        public const string Margin = "margin";

        /// <summary>Top outer spacing.</summary>
        public const string MarginTop = "margin-top";

        /// <summary>Right outer spacing.</summary>
        public const string MarginRight = "margin-right";

        /// <summary>Bottom outer spacing.</summary>
        public const string MarginBottom = "margin-bottom";

        /// <summary>Left outer spacing.</summary>
        public const string MarginLeft = "margin-left";

        /// <summary>Inner spacing shorthand.</summary>
        public const string Padding = "padding";

        /// <summary>Top inner spacing.</summary>
        public const string PaddingTop = "padding-top";

        /// <summary>Right inner spacing.</summary>
        public const string PaddingRight = "padding-right";

        /// <summary>Bottom inner spacing.</summary>
        public const string PaddingBottom = "padding-bottom";

        /// <summary>Left inner spacing.</summary>
        public const string PaddingLeft = "padding-left";

        /// <summary>Text color.</summary>
        public const string Color = "color";

        /// <summary>Background shorthand.</summary>
        public const string Background = "background";

        /// <summary>Background color.</summary>
        public const string BackgroundColor = "background-color";

        /// <summary>Background image path.</summary>
        public const string BackgroundImage = "background-image";

        /// <summary>Background image sizing.</summary>
        public const string BackgroundSize = "background-size";

        /// <summary>Background image repetition.</summary>
        public const string BackgroundRepeat = "background-repeat";

        /// <summary>Background image position.</summary>
        public const string BackgroundPosition = "background-position";

        /// <summary>Border shorthand.</summary>
        public const string Border = "border";

        /// <summary>Border width shorthand.</summary>
        public const string BorderWidth = "border-width";

        /// <summary>Border style shorthand.</summary>
        public const string BorderStyle = "border-style";

        /// <summary>Border color shorthand.</summary>
        public const string BorderColor = "border-color";

        /// <summary>Corner radius shorthand.</summary>
        public const string BorderRadius = "border-radius";

        /// <summary>Font family list.</summary>
        public const string FontFamily = "font-family";

        /// <summary>Font size.</summary>
        public const string FontSize = "font-size";

        /// <summary>Font weight.</summary>
        public const string FontWeight = "font-weight";

        /// <summary>Font style.</summary>
        public const string FontStyle = "font-style";

        /// <summary>Text decoration.</summary>
        public const string TextDecoration = "text-decoration";

        /// <summary>Text transformation.</summary>
        public const string TextTransform = "text-transform";

        /// <summary>Horizontal text alignment.</summary>
        public const string TextAlign = "text-align";

        /// <summary>Vertical text alignment.</summary>
        public const string VerticalAlign = "vertical-align";

        /// <summary>Line height.</summary>
        public const string LineHeight = "line-height";

        /// <summary>White space handling.</summary>
        public const string WhiteSpace = "white-space";

        /// <summary>Flex container direction.</summary>
        public const string FlexDirection = "flex-direction";

        /// <summary>Flex line wrapping.</summary>
        public const string FlexWrap = "flex-wrap";

        /// <summary>Main axis content alignment.</summary>
        public const string JustifyContent = "justify-content";

        /// <summary>Cross axis item alignment.</summary>
        public const string AlignItems = "align-items";

        /// <summary>Cross axis content alignment.</summary>
        public const string AlignContent = "align-content";

        /// <summary>Flex item alignment override.</summary>
        public const string AlignSelf = "align-self";

        /// <summary>Flex item grow, shrink, and basis shorthand.</summary>
        public const string Flex = "flex";

        /// <summary>Flex item growth factor.</summary>
        public const string FlexGrow = "flex-grow";

        /// <summary>Row and column gap shorthand.</summary>
        public const string Gap = "gap";

        /// <summary>Overflow handling shorthand.</summary>
        public const string Overflow = "overflow";

        /// <summary>Horizontal overflow handling.</summary>
        public const string OverflowX = "overflow-x";

        /// <summary>Vertical overflow handling.</summary>
        public const string OverflowY = "overflow-y";

        /// <summary>Element opacity.</summary>
        public const string Opacity = "opacity";

        /// <summary>Element visibility.</summary>
        public const string Visibility = "visibility";

        /// <summary>Stacking order.</summary>
        public const string ZIndex = "z-index";

        /// <summary>Pointer cursor shape.</summary>
        public const string Cursor = "cursor";

        /// <summary>Image fitting inside replaced elements.</summary>
        public const string ObjectFit = "object-fit";

        /// <summary>Decorator chain applied by the game client.</summary>
        public const string Decorator = "decorator";

        /// <summary>Font effect chain applied by the game client.</summary>
        public const string FontEffect = "font-effect";

        /// <summary>Perspective for 3D transforms.</summary>
        public const string Perspective = "perspective";

        /// <summary>Transform chain.</summary>
        public const string Transform = "transform";

        /// <summary>Transition shorthand.</summary>
        public const string Transition = "transition";

        /// <summary>Animation shorthand.</summary>
        public const string Animation = "animation";
    }

    /// <summary>
    /// Input control types.
    /// </summary>
    public static class InputTypes
    {
        /// <summary>Single-line text entry.</summary>
        public const string Text = "text";

        /// <summary>Masked password entry.</summary>
        public const string Password = "password";

        /// <summary>Toggle check box.</summary>
        public const string Checkbox = "checkbox";

        /// <summary>Grouped radio button.</summary>
        public const string Radio = "radio";

        /// <summary>Generic push button.</summary>
        public const string Button = "button";

        /// <summary>Form submit button.</summary>
        public const string Submit = "submit";

        /// <summary>Form reset button.</summary>
        public const string Reset = "reset";

        /// <summary>Range slider.</summary>
        public const string Range = "range";

        /// <summary>Hidden form value.</summary>
        public const string Hidden = "hidden";

        /// <summary>File picker button.</summary>
        public const string File = "file";
    }

    /// <summary>
    /// Well-known style sheet value keywords.
    /// </summary>
    public static class Values
    {
        /// <summary>Automatic sizing keyword.</summary>
        public const string Auto = "auto";

        /// <summary>Hidden display and visibility keyword.</summary>
        public const string Hidden = "hidden";

        /// <summary>Collapsed visibility keyword.</summary>
        public const string Collapse = "collapse";

        /// <summary>Disabled display keyword.</summary>
        public const string None = "none";

        /// <summary>Horizontal flex direction.</summary>
        public const string Row = "row";

        /// <summary>Reversed horizontal flex direction.</summary>
        public const string RowReverse = "row-reverse";

        /// <summary>Single-line flex wrapping.</summary>
        public const string NoWrap = "nowrap";

        /// <summary>Clipped overflow keyword.</summary>
        public const string OverflowHidden = "hidden";

        /// <summary>Scrolling overflow keyword.</summary>
        public const string Scroll = "scroll";

        /// <summary>Automatic overflow keyword.</summary>
        public const string OverflowAuto = "auto";

        /// <summary>Absolute positioning keyword.</summary>
        public const string Absolute = "absolute";

        /// <summary>Pointer cursor keyword.</summary>
        public const string Pointer = "pointer";

        /// <summary>Text cursor keyword.</summary>
        public const string Text = "text";

        /// <summary>Bold font weight keyword.</summary>
        public const string Bold = "bold";

        /// <summary>Bolder font weight keyword.</summary>
        public const string Bolder = "bolder";

        /// <summary>Italic font style keyword.</summary>
        public const string Italic = "italic";

        /// <summary>Oblique font style keyword.</summary>
        public const string Oblique = "oblique";

        /// <summary>Underlined text decoration keyword.</summary>
        public const string Underline = "underline";

        /// <summary>Struck-through text decoration keyword.</summary>
        public const string LineThrough = "line-through";

        /// <summary>Centered text alignment keyword.</summary>
        public const string Center = "center";

        /// <summary>Right text alignment keyword.</summary>
        public const string Right = "right";

        /// <summary>End text alignment keyword.</summary>
        public const string End = "end";

        /// <summary>Justified text alignment keyword.</summary>
        public const string Justify = "justify";

        /// <summary>Uppercase text transform keyword.</summary>
        public const string Uppercase = "uppercase";

        /// <summary>Lowercase text transform keyword.</summary>
        public const string Lowercase = "lowercase";

        /// <summary>Capitalized text transform keyword.</summary>
        public const string Capitalize = "capitalize";

        /// <summary>Relative larger font size keyword.</summary>
        public const string Larger = "larger";

        /// <summary>Relative smaller font size keyword.</summary>
        public const string Smaller = "smaller";

        /// <summary>Extra extra small font size keyword.</summary>
        public const string XxSmall = "xx-small";

        /// <summary>Extra small font size keyword.</summary>
        public const string XSmall = "x-small";

        /// <summary>Small font size keyword.</summary>
        public const string Small = "small";

        /// <summary>Medium font size keyword.</summary>
        public const string Medium = "medium";

        /// <summary>Large font size keyword.</summary>
        public const string Large = "large";

        /// <summary>Extra large font size keyword.</summary>
        public const string XLarge = "x-large";

        /// <summary>Extra extra large font size keyword.</summary>
        public const string XxLarge = "xx-large";

        /// <summary>Contained object fit keyword.</summary>
        public const string Contain = "contain";

        /// <summary>Covering object fit keyword.</summary>
        public const string Cover = "cover";

        /// <summary>Downscaling object fit keyword.</summary>
        public const string ScaleDown = "scale-down";
    }

    /// <summary>
    /// Command line flags controlling RmlUi screens in the game client.
    /// </summary>
    public static class LaunchFlags
    {
        /// <summary>Enables the in-game RmlUi debugger.</summary>
        public const string Debug = "-rmldebug";

        /// <summary>Forces the named screen back to its legacy window definition.</summary>
        public const string ForceWindow = "-rmlwnd";

        /// <summary>Ignores interface overrides shipped inside mod archives.</summary>
        public const string IgnoreModWindows = "-rmlignoremodwnd";
    }

    /// <summary>
    /// Editor canvas and history limits.
    /// </summary>
    public static class Editor
    {
        /// <summary>Minimum canvas width in device-independent pixels.</summary>
        public const double MinCanvasWidth = 800.0;

        /// <summary>Minimum canvas height in device-independent pixels.</summary>
        public const double MinCanvasHeight = 600.0;

        /// <summary>Default preview width in device-independent pixels.</summary>
        public const double DefaultPreviewWidth = 1024.0;

        /// <summary>Padding around the preview surface in device-independent pixels.</summary>
        public const double CanvasPadding = 24.0;

        /// <summary>Maximum canvas zoom factor.</summary>
        public const double MaxZoom = 4.0;

        /// <summary>Maximum undo history entries kept per document.</summary>
        public const int MaxUndoHistory = 200;

        /// <summary>Maximum art library rows shown in the assets tab.</summary>
        public const int MaxArtLibraryRows = 200;

        /// <summary>Maximum preview dimension in device-independent pixels, clamping absurd style lengths.</summary>
        public const double MaxPreviewDimension = 4096.0;

        /// <summary>Maximum preview font size in device-independent pixels.</summary>
        public const double MaxPreviewFontSize = 256.0;
    }

    /// <summary>
    /// URI scheme prefixes treated as external references that the editor never resolves locally.
    /// </summary>
    public static class ExternalSchemes
    {
        /// <summary>Plain HTTP reference prefix.</summary>
        public const string HttpPrefix = "http://";

        /// <summary>Secure HTTP reference prefix.</summary>
        public const string HttpsPrefix = "https://";

        /// <summary>Embedded data URI prefix.</summary>
        public const string DataPrefix = "data:";
    }

    /// <summary>
    /// Whitespace preservation markers. The xml prefix is stripped by XML parsing,
    /// so the attribute is stored and matched by its local name.
    /// </summary>
    public static class Whitespace
    {
        /// <summary>Local name of the xml:space whitespace handling attribute.</summary>
        public const string SpaceAttribute = "space";

        /// <summary>Attribute value requesting verbatim whitespace preservation.</summary>
        public const string Preserve = "preserve";
    }

    /// <summary>
    /// Well-known style class names used by seeded content.
    /// </summary>
    public static class CssClasses
    {
        /// <summary>Root panel class of a new screen.</summary>
        public const string Screen = "screen";
    }

    /// <summary>
    /// Style sheet function prefixes.
    /// </summary>
    public static class CssFunctions
    {
        /// <summary>Resource URL function prefix.</summary>
        public const string UrlPrefix = "url(";
    }
}
