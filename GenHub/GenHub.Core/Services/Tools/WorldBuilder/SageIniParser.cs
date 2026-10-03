// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Typed SAGE INI grammar core, ported from INI::readLine, the block dispatch loop, and
/// ThingFactory::parseObjectDefinition: line processing, block dispatch, End handling,
/// field tables with wildcard terminal entries, module sub-blocks, and Object-family
/// inheritance. Sits beside the IniEditor text round-trip model; it never modifies source.
/// Two deviations are deliberate: #define/#include preprocessing (the engine has none;
/// a GenHub extension for shared fragments) and tolerant per-file failure reporting.
/// </summary>
public sealed class SageIniParser(ILogger<SageIniParser> logger)
{
    private sealed record SourceLine(IReadOnlyList<string> Tokens, string DisplayText, string SourceFile, int LineNumber);

    private sealed class ParseSession(SageIniParseOptions options)
    {
        public SageIniParseOptions Options { get; } = options;

        public List<SourceLine> Lines { get; } = [];

        public int Index { get; set; }

        public Dictionary<string, IReadOnlyList<string>> Defines { get; } = new(StringComparer.Ordinal);

        public List<SageIniBlock> Blocks { get; } = [];

        public Dictionary<string, SageIniBlock> Templates { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, SageIniBlock> MergedByKey { get; } = new(StringComparer.Ordinal);

        public List<SageIniDiagnostic> Diagnostics { get; } = [];

        public List<SageIniSkippedBlock> SkippedBlocks { get; } = [];

        public List<SageIniSkippedBlock> UnrecognizedBlocks { get; } = [];

        public HashSet<string> TabsReported { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int DroppedDiagnostics { get; set; }

        public string? BlockError { get; set; }
    }

    /// <summary>
    /// Merges an incoming block over an existing block with INI_LOAD_OVERWRITE semantics:
    /// fields merge last-wins per key, sub-blocks append, and the incoming source position wins.
    /// </summary>
    /// <param name="existing">The earlier block.</param>
    /// <param name="incoming">The later block.</param>
    /// <returns>The merged block.</returns>
    public static SageIniBlock MergeOverride(SageIniBlock existing, SageIniBlock incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);
        var fields = MergeFields(existing.Fields, incoming.Fields);
        var subBlocks = existing.SubBlocks.Concat(incoming.SubBlocks).ToList();
        return existing with
        {
            ParentName = incoming.ParentName ?? existing.ParentName,
            Fields = fields,
            SubBlocks = subBlocks,
            SourceFile = incoming.SourceFile,
            LineNumber = incoming.LineNumber,
        };
    }

    /// <summary>
    /// Parses one SAGE INI text unit with include expansion, define substitution, block
    /// dispatch, and inheritance. Strict mode fails the file on the first unknown block,
    /// missing End, or unfinishable block like INI::load; tolerant mode skips failing
    /// blocks to End and continues like INI::loadWB.
    /// </summary>
    /// <param name="text">The entry file text.</param>
    /// <param name="sourceName">The entry file name used in diagnostics.</param>
    /// <param name="options">The block table, failure mode, known parents, and include reader.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed document, or a failure describing the file-fatal line.</returns>
    public async Task<OperationResult<SageIniDocument>> ParseAsync(
        string text,
        string sourceName,
        SageIniParseOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(options);
        var started = Stopwatch.GetTimestamp();
        var session = new ParseSession(options);
        var failure = await ExpandIncludesAsync(session, text, sourceName, [sourceName], cancellationToken).ConfigureAwait(false);
        failure ??= ParseBlocks(session, cancellationToken);

        if (failure is not null)
        {
            logger.LogDebug("SAGE INI parse failed for {Source}: {Reason}", sourceName, failure);
            return OperationResult<SageIniDocument>.CreateFailure(failure, Stopwatch.GetElapsedTime(started));
        }

        var document = BuildDocument(session, sourceName);
        return OperationResult<SageIniDocument>.CreateSuccess(document, Stopwatch.GetElapsedTime(started));
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        return StripBom(text).Split('\n');
    }

    private static string StripBom(string text)
    {
        if (text.StartsWith('\uFEFF'))
        {
            return text[1..];
        }

        if (text.StartsWith("\u00EF\u00BB\u00BF", StringComparison.Ordinal))
        {
            return text[3..];
        }

        return text;
    }

    private static List<string> Tokenize(string processed)
    {
        return processed.Split(SageIniConstants.Grammar.TokenSeparators, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static bool IsEndToken(string token)
    {
        return token.Equals(SageIniConstants.Grammar.EndToken, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsObjectFamily(string token)
    {
        return token.Equals(SageIniConstants.Inheritance.Object, StringComparison.Ordinal)
            || token.Equals(SageIniConstants.Inheritance.ObjectReskin, StringComparison.Ordinal)
            || token.Equals(SageIniConstants.Inheritance.ObjectExtend, StringComparison.Ordinal)
            || token.Equals(SageIniConstants.Inheritance.ChildObject, StringComparison.Ordinal);
    }

    private static bool RequiresParent(string token)
    {
        return token.Equals(SageIniConstants.Inheritance.ObjectReskin, StringComparison.Ordinal)
            || token.Equals(SageIniConstants.Inheritance.ObjectExtend, StringComparison.Ordinal)
            || token.Equals(SageIniConstants.Inheritance.ChildObject, StringComparison.Ordinal);
    }

    private static SageIniFieldTable DefaultFieldTable(string token)
    {
        if (token.Equals(SageIniConstants.Inheritance.ObjectReskin, StringComparison.Ordinal))
        {
            return new SageIniFieldTable(
                SageIniConstants.ReskinFields.All,
                HasWildcard: false,
                new HashSet<string>([SageIniConstants.ModuleOpeners.Draw], StringComparer.Ordinal));
        }

        if (IsObjectFamily(token))
        {
            return new SageIniFieldTable(
                new HashSet<string>(StringComparer.Ordinal),
                HasWildcard: true,
                SageIniConstants.ModuleOpeners.All,
                NestedScopeOpeners: SageIniConstants.ObjectNestedScopes.All);
        }

        if (token.Equals(SageIniConstants.BlockTokens.FXList, StringComparison.Ordinal))
        {
            return new SageIniFieldTable(
                new HashSet<string>(StringComparer.Ordinal),
                HasWildcard: true,
                new HashSet<string>(StringComparer.Ordinal),
                NestedScopeOpeners: SageIniConstants.FxListScopes.All);
        }

        if (token.Equals(SageIniConstants.BlockTokens.ObjectCreationList, StringComparison.Ordinal))
        {
            return new SageIniFieldTable(
                new HashSet<string>(StringComparer.Ordinal),
                HasWildcard: true,
                new HashSet<string>(StringComparer.Ordinal),
                NestedScopeOpeners: SageIniConstants.ObjectCreationListScopes.All);
        }

        return new SageIniFieldTable(
            new HashSet<string>(StringComparer.Ordinal),
            HasWildcard: true,
            new HashSet<string>(StringComparer.Ordinal));
    }

    private static SageIniFieldTable ResolveFieldTable(ParseSession session, string token)
    {
        if (session.Options.FieldTables?.TryGetValue(token, out var custom) == true)
        {
            return custom;
        }

        return DefaultFieldTable(token);
    }

    private static IReadOnlySet<string> ResolveBlockTable(SageIniParseOptions options)
    {
        return options.BlockTable ?? SageIniConstants.BlockTables.Full;
    }

    private static List<SageIniField> MergeFields(IReadOnlyList<SageIniField> existing, IReadOnlyList<SageIniField> incoming)
    {
        var merged = new Dictionary<string, SageIniField>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var field in existing.Concat(incoming))
        {
            if (!merged.ContainsKey(field.Key))
            {
                order.Add(field.Key);
            }

            merged[field.Key] = field;
        }

        return order.Select(key => merged[key]).ToList();
    }

    private static string MergeKey(string token, string name)
    {
        return string.Concat(token, "\n", name);
    }

    private static void AddDiagnostic(ParseSession session, SageIniDiagnosticLevel level, string file, int line, string message)
    {
        if (session.Diagnostics.Count >= SageIniConstants.Grammar.MaxDiagnosticsPerFile)
        {
            session.DroppedDiagnostics++;
            return;
        }

        session.Diagnostics.Add(new SageIniDiagnostic(level, file, line, message));
    }

    private static SageIniDocument BuildDocument(ParseSession session, string sourceName)
    {
        var diagnostics = session.Diagnostics.ToList();
        if (session.DroppedDiagnostics > 0)
        {
            diagnostics.Add(new SageIniDiagnostic(
                SageIniDiagnosticLevel.Warning,
                sourceName,
                0,
                $"{session.DroppedDiagnostics} further diagnostics were truncated."));
        }

        return new SageIniDocument(sourceName, session.Blocks, diagnostics, session.SkippedBlocks, session.UnrecognizedBlocks);
    }

    private static string? ParseBlocks(ParseSession session, CancellationToken cancellationToken)
    {
        SeedTemplates(session);
        var table = ResolveBlockTable(session.Options);
        while (session.Index < session.Lines.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = session.Lines[session.Index];
            if (line.Tokens.Count == 0)
            {
                session.Index++;
                continue;
            }

            var token = line.Tokens[0];
            if (!table.Contains(token))
            {
                var failure = HandleUnknownBlock(session, line, token);
                if (failure is not null)
                {
                    return failure;
                }

                continue;
            }

            session.BlockError = null;
            if (!TryParseBlock(session, line, cancellationToken))
            {
                var failure = HandleBlockFailure(session, line);
                if (failure is not null)
                {
                    return failure;
                }
            }
        }

        return null;
    }

    private static void SeedTemplates(ParseSession session)
    {
        if (session.Options.KnownBlocks is null)
        {
            return;
        }

        foreach (var (name, block) in session.Options.KnownBlocks)
        {
            if (IsObjectFamily(block.BlockToken) && !string.IsNullOrEmpty(name))
            {
                session.Templates[name] = block;
            }
        }
    }

    private static string? HandleUnknownBlock(ParseSession session, SourceLine line, string token)
    {
        if (!session.Options.TolerateBlockFailures)
        {
            return $"Unknown block '{token}' in {line.SourceFile} line {line.LineNumber}.";
        }

        session.UnrecognizedBlocks.Add(new SageIniSkippedBlock(line.DisplayText, line.SourceFile, line.LineNumber, $"Token '{token}' is not in the block table."));
        session.Index++;
        if (IsEndToken(token))
        {
            return null;
        }

        SkipToEnd(session);
        return null;
    }

    private static string? HandleBlockFailure(ParseSession session, SourceLine line)
    {
        var reason = session.BlockError ?? "The block could not finish.";
        if (!session.Options.TolerateBlockFailures)
        {
            return $"{reason} in {line.SourceFile} line {line.LineNumber}.";
        }

        session.SkippedBlocks.Add(new SageIniSkippedBlock(line.DisplayText, line.SourceFile, line.LineNumber, reason));
        SkipToEnd(session);
        return null;
    }

    private static void SkipToEnd(ParseSession session)
    {
        while (session.Index < session.Lines.Count)
        {
            var line = session.Lines[session.Index];
            session.Index++;
            if (line.Tokens.Count > 0 && IsEndToken(line.Tokens[0]))
            {
                return;
            }
        }
    }

    private static bool TryParseBlock(ParseSession session, SourceLine header, CancellationToken cancellationToken)
    {
        var token = header.Tokens[0];
        if (!TryResolveHeader(session, header, token, out var name, out var parentName))
        {
            return false;
        }

        SageIniBlock? parent = null;
        if (parentName is not null && !session.Templates.TryGetValue(parentName, out parent))
        {
            session.BlockError = $"{token} block '{name}' must come after its parent '{parentName}'";
            return false;
        }

        session.Index++;
        var table = ResolveFieldTable(session, token);
        var fields = new List<SageIniField>();
        var subBlocks = new List<SageIniSubBlock>();
        if (!TryParseBlockBody(session, header, token, table, fields, subBlocks, cancellationToken))
        {
            return false;
        }

        var block = new SageIniBlock(token, name, parentName, fields, subBlocks, header.SourceFile, header.LineNumber);
        if (parent is not null)
        {
            block = ApplyInheritance(block, parent);
        }

        StoreBlock(session, block);
        return true;
    }

    private static bool TryResolveHeader(ParseSession session, SourceLine header, string token, out string name, out string? parentName)
    {
        name = string.Empty;
        parentName = null;
        if (RequiresParent(token))
        {
            if (header.Tokens.Count < 3)
            {
                session.BlockError = $"{token} block is missing its name or parent name";
                return false;
            }

            name = header.Tokens[1];
            parentName = header.Tokens[2];
            return true;
        }

        if (token.Equals(SageIniConstants.Inheritance.Object, StringComparison.Ordinal))
        {
            if (header.Tokens.Count < 2)
            {
                session.BlockError = "Object block is missing its name";
                return false;
            }

            name = header.Tokens[1];
            return true;
        }

        name = header.Tokens.Count > 1 ? header.Tokens[1] : string.Empty;
        return true;
    }

    private static bool TryParseBlockBody(
        ParseSession session,
        SourceLine header,
        string token,
        SageIniFieldTable table,
        List<SageIniField> fields,
        List<SageIniSubBlock> subBlocks,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session.Index >= session.Lines.Count)
            {
                session.BlockError = $"{token} block '{header.DisplayText}' is missing its End token";
                return false;
            }

            var outcome = ParseBodyLine(session, token, table, fields, subBlocks, cancellationToken);
            if (outcome == true)
            {
                return true;
            }

            if (outcome == false)
            {
                return false;
            }
        }
    }

    private static bool? ParseBodyLine(
        ParseSession session,
        string token,
        SageIniFieldTable table,
        List<SageIniField> fields,
        List<SageIniSubBlock> subBlocks,
        CancellationToken cancellationToken)
    {
        var line = session.Lines[session.Index];
        session.Index++;
        if (line.Tokens.Count == 0)
        {
            return null;
        }

        if (IsEndToken(line.Tokens[0]))
        {
            return true;
        }

        if (table.SubBlockOpeners.Contains(line.Tokens[0]))
        {
            return TryParseSubBlock(session, line, subBlocks, cancellationToken) ? null : false;
        }

        if (table.NestedScopeOpeners is not null && table.NestedScopeOpeners.Contains(line.Tokens[0]))
        {
            return TryParseBareScope(session, line, subBlocks, cancellationToken) ? null : false;
        }

        AddField(session, table, token, line, fields);
        return null;
    }

    private static void AddField(ParseSession session, SageIniFieldTable table, string token, SourceLine line, List<SageIniField> fields)
    {
        var key = line.Tokens[0];
        if (!table.HasWildcard && !table.KnownFields.Contains(key))
        {
            AddDiagnostic(session, SageIniDiagnosticLevel.Info, line.SourceFile, line.LineNumber, $"Field '{key}' is not legal in {token} blocks and was dropped.");
            return;
        }

        fields.Add(new SageIniField(key, line.Tokens.Skip(1).ToList()));
    }

    private static bool TryParseSubBlock(ParseSession session, SourceLine opener, List<SageIniSubBlock> subBlocks, CancellationToken cancellationToken)
    {
        if (opener.Tokens.Count < 3)
        {
            session.BlockError = $"Module '{opener.Tokens[0]}' is missing its type or tag";
            return false;
        }

        var fields = new List<SageIniField>();
        var nestedDepth = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session.Index >= session.Lines.Count)
            {
                session.BlockError = $"Module '{opener.DisplayText}' is missing its End token";
                return false;
            }

            var line = session.Lines[session.Index];
            session.Index++;
            if (line.Tokens.Count == 0)
            {
                continue;
            }

            if (IsEndToken(line.Tokens[0]))
            {
                if (nestedDepth > 0)
                {
                    nestedDepth--;
                    continue;
                }

                subBlocks.Add(new SageIniSubBlock(opener.Tokens[0], opener.Tokens[1], opener.Tokens[2], fields));
                return true;
            }

            if (SageIniConstants.ModuleNestedScopes.All.Contains(line.Tokens[0]))
            {
                nestedDepth++;
            }

            fields.Add(new SageIniField(line.Tokens[0], line.Tokens.Skip(1).ToList()));
        }
    }

    private static bool TryParseBareScope(ParseSession session, SourceLine opener, List<SageIniSubBlock> subBlocks, CancellationToken cancellationToken)
    {
        var fields = new List<SageIniField>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session.Index >= session.Lines.Count)
            {
                session.BlockError = $"Scope '{opener.DisplayText}' is missing its End token";
                return false;
            }

            var line = session.Lines[session.Index];
            session.Index++;
            if (line.Tokens.Count == 0)
            {
                continue;
            }

            if (IsEndToken(line.Tokens[0]))
            {
                subBlocks.Add(new SageIniSubBlock(opener.Tokens[0], string.Empty, string.Empty, fields));
                return true;
            }

            fields.Add(new SageIniField(line.Tokens[0], line.Tokens.Skip(1).ToList()));
        }
    }

    private static SageIniBlock ApplyInheritance(SageIniBlock block, SageIniBlock parent)
    {
        var fields = MergeFields(parent.Fields, block.Fields);
        var subBlocks = MergeInheritedSubBlocks(block, parent);
        return block with { Fields = fields, SubBlocks = subBlocks };
    }

    private static List<SageIniSubBlock> MergeInheritedSubBlocks(SageIniBlock block, SageIniBlock parent)
    {
        var inherited = parent.SubBlocks.AsEnumerable();
        if (block.BlockToken.Equals(SageIniConstants.Inheritance.ObjectReskin, StringComparison.Ordinal)
            && block.SubBlocks.Any(s => s.Key.Equals(SageIniConstants.ModuleOpeners.Draw, StringComparison.Ordinal)))
        {
            inherited = inherited.Where(s => !s.Key.Equals(SageIniConstants.ModuleOpeners.Draw, StringComparison.Ordinal));
        }

        return inherited.Concat(block.SubBlocks).ToList();
    }

    private static void StoreBlock(ParseSession session, SageIniBlock block)
    {
        var key = MergeKey(block.BlockToken, block.Name);
        if (session.MergedByKey.TryGetValue(key, out var existing))
        {
            block = MergeOverride(existing, block);
            var slot = session.Blocks.FindIndex(b => b.BlockToken.Equals(block.BlockToken, StringComparison.Ordinal) && b.Name.Equals(block.Name, StringComparison.OrdinalIgnoreCase));
            if (slot >= 0)
            {
                session.Blocks[slot] = block;
            }
        }
        else
        {
            session.Blocks.Add(block);
        }

        session.MergedByKey[key] = block;
        if (IsObjectFamily(block.BlockToken) && !string.IsNullOrEmpty(block.Name))
        {
            session.Templates[block.Name] = block;
        }
    }

    private static async Task<string?> ExpandIncludesAsync(
        ParseSession session,
        string text,
        string fileName,
        List<string> chain,
        CancellationToken cancellationToken)
    {
        var physical = SplitLines(text);
        for (var number = 0; number < physical.Count; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processed = ProcessLine(session, physical[number].TrimEnd('\r'), fileName, number + 1);
            var tokens = Tokenize(processed);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (!tokens[0].StartsWith(SageIniConstants.Directives.Introducer))
            {
                session.Lines.Add(new SourceLine(ExpandDefines(session, tokens), processed.Trim(), fileName, number + 1));
                continue;
            }

            var failure = await HandleDirectiveAsync(session, tokens, fileName, chain, cancellationToken).ConfigureAwait(false);
            if (failure is not null)
            {
                return failure;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExpandDefines(ParseSession session, List<string> tokens)
    {
        if (session.Defines.Count == 0)
        {
            return tokens;
        }

        var expanded = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            if (session.Defines.TryGetValue(token, out var replacement))
            {
                expanded.AddRange(replacement);
            }
            else
            {
                expanded.Add(token);
            }
        }

        return expanded;
    }

    private static async Task<string?> HandleDirectiveAsync(
        ParseSession session,
        List<string> tokens,
        string fileName,
        List<string> chain,
        CancellationToken cancellationToken)
    {
        var directive = tokens[0];
        if (directive.Equals(SageIniConstants.Directives.Define, StringComparison.Ordinal))
        {
            return HandleDefine(session, tokens, fileName);
        }

        if (directive.Equals(SageIniConstants.Directives.Include, StringComparison.Ordinal))
        {
            return await HandleIncludeAsync(session, tokens, fileName, chain, cancellationToken).ConfigureAwait(false);
        }

        return DirectiveFailure(session, fileName, $"Unknown directive '{directive}'.");
    }

    private static string? HandleDefine(ParseSession session, List<string> tokens, string fileName)
    {
        if (tokens.Count < 2)
        {
            return DirectiveFailure(session, fileName, "#define is missing its name.");
        }

        session.Defines[tokens[1]] = tokens.Skip(2).ToList();
        return null;
    }

    private static async Task<string?> HandleIncludeAsync(
        ParseSession session,
        List<string> tokens,
        string fileName,
        List<string> chain,
        CancellationToken cancellationToken)
    {
        if (tokens.Count < 2)
        {
            return DirectiveFailure(session, fileName, "#include is missing its path.");
        }

        var target = string.Join(" ", tokens.Skip(1)).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(target))
        {
            return DirectiveFailure(session, fileName, "#include is missing its path.");
        }

        if (session.Options.IncludeReader is null)
        {
            return DirectiveFailure(session, fileName, $"#include '{target}' cannot expand without an include reader.");
        }

        if (chain.Count >= SageIniConstants.Grammar.MaxIncludeDepth)
        {
            return DirectiveFailure(session, fileName, $"#include '{target}' exceeds the maximum include depth.");
        }

        var read = await session.Options.IncludeReader(fileName, target, cancellationToken).ConfigureAwait(false);
        if (!read.Success)
        {
            return DirectiveFailure(session, fileName, $"#include '{target}' could not be read: {read.FirstError}.");
        }

        var resolved = read.Data.ResolvedPath;
        if (chain.Contains(resolved, StringComparer.OrdinalIgnoreCase))
        {
            return DirectiveFailure(session, fileName, $"#include '{target}' forms a cycle.");
        }

        chain.Add(resolved);
        var failure = await ExpandIncludesAsync(session, read.Data.Text, resolved, chain, cancellationToken).ConfigureAwait(false);
        chain.RemoveAt(chain.Count - 1);
        return failure;
    }

    private static string? DirectiveFailure(ParseSession session, string fileName, string message)
    {
        if (session.Options.TolerateBlockFailures)
        {
            session.Diagnostics.Add(new SageIniDiagnostic(SageIniDiagnosticLevel.Error, fileName, 0, message));
            return null;
        }

        return $"{message} in {fileName}.";
    }

    private static string ProcessLine(ParseSession session, string raw, string fileName, int lineNumber)
    {
        var comment = raw.IndexOf(SageIniConstants.Grammar.Comment);
        var content = comment >= 0 ? raw[..comment] : raw;
        if (content.Length > SageIniConstants.Grammar.MaxCharsPerLine)
        {
            AddDiagnostic(session, SageIniDiagnosticLevel.Warning, fileName, lineNumber, $"Line exceeds {SageIniConstants.Grammar.MaxCharsPerLine} characters.");
        }

        if (content.Contains('\t') && session.TabsReported.Add(fileName))
        {
            AddDiagnostic(session, SageIniDiagnosticLevel.Warning, fileName, lineNumber, "Tab characters are not allowed in INI files.");
        }

        if (content.All(c => c >= 32))
        {
            return content;
        }

        return string.Concat(content.Select(c => c < 32 ? ' ' : c));
    }
}
