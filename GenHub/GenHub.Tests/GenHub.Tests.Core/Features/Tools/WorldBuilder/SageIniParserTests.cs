// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Grammar unit tests for <see cref="SageIniParser"/>: comments, End handling, block
/// dispatch, #include/#define, field tables with wildcard entries, module sub-blocks,
/// and Object-family inheritance. All inputs are inline fixtures; no game install is used.
/// </summary>
public sealed class SageIniParserTests
{
    /// <summary>
    /// Tests that a semicolon truncates the line, even inside a would-be quoted value, like the engine.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_CommentTruncatesLine_EngineExactAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank ; trailing comment\nDisplayName = Foo;Bar\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks.Should().ContainSingle();
        parsed.Data.Blocks[0].Fields.Should().ContainSingle().Which.Values.Should().BeEquivalentTo("Foo");
    }

    /// <summary>
    /// Tests that End closes a block case-insensitively, like the engine stricmp.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_EndCaseInsensitive_ClosesBlockAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank\nBuildCost = 100\nend\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks.Should().ContainSingle();
    }

    /// <summary>
    /// Tests that block tokens are matched case-sensitively, like the engine strcmp.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_LowercaseBlockToken_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "object Tank\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("Unknown block 'object'");
    }

    /// <summary>
    /// Tests that tabs are reported but tolerated as separators.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_TabInLine_WarnsAndParsesAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object\tTank\nBuildCost\t=\t100\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks.Should().ContainSingle().Which.Name.Should().Be("Tank");
        parsed.Data.Diagnostics.Should().ContainSingle(d => d.Level == SageIniDiagnosticLevel.Warning && d.Message.Contains("Tab"));
    }

    /// <summary>
    /// Tests that control characters below 32 become spaces.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ControlChars_NormalizedToSpacesAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank\nBuildCost =\u0007100\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks[0].Fields.Should().ContainSingle().Which.Values.Should().BeEquivalentTo("100");
    }

    /// <summary>
    /// Tests that extra header tokens beyond name and parent are ignored.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ExtraHeaderTokens_IgnoredAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank Extra Tokens Here\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks.Should().ContainSingle().Which.Name.Should().Be("Tank");
    }

    /// <summary>
    /// Tests that a nameless singleton block such as GameData parses with an empty name.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_NamelessBlock_EmptyNameAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "GameData\nMapHeightScale = 1\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks.Should().ContainSingle().Which.Name.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that a missing End fails the file in strict mode.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_MissingEnd_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank\nBuildCost = 100\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("missing its End token");
    }

    /// <summary>
    /// Tests that a stray top-level End is an unknown block in strict mode.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_StrayEnd_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank\nEnd\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("Unknown block 'End'");
    }

    /// <summary>
    /// Tests that an Object block without a name fails in strict mode.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ObjectWithoutName_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("missing its name");
    }

    /// <summary>
    /// Tests that an ObjectReskin block without a parent name fails in strict mode.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ReskinWithoutParent_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "ObjectReskin Lone\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("missing its name or parent name");
    }

    /// <summary>
    /// Tests that unknown fields are kept raw under the wildcard terminal entry.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_UnknownField_WildcardKeepsRawAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank\nFutureField = 1 2 3\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks[0].Fields.Should().ContainSingle(f => f.Key == "FutureField")
            .Which.Values.Should().BeEquivalentTo("1", "2", "3");
    }

    /// <summary>
    /// Tests that a strict field table without a wildcard drops unknown fields with a diagnostic.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_UnknownField_StrictTableDropsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var tables = new Dictionary<string, SageIniFieldTable>
        {
            ["Science"] = new SageIniFieldTable(
                new HashSet<string>(["Name"], StringComparer.Ordinal),
                HasWildcard: false,
                new HashSet<string>(StringComparer.Ordinal)),
        };
        var text = "Science S\nName = Good\nBogus = 1\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions(FieldTables: tables));

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks[0].Fields.Should().ContainSingle(f => f.Key == "Name");
        parsed.Data.Diagnostics.Should().ContainSingle(d => d.Message.Contains("Bogus"));
    }

    /// <summary>
    /// Tests that a Draw module consumes its own End without closing the block.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ModuleSubBlock_NestsEndsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank\nDraw = W3DModelDraw ModuleTag_01\nModel = tank.w3d\nEnd\nBuildCost = 100\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        block.SubBlocks.Should().ContainSingle();
        block.SubBlocks[0].Key.Should().Be("Draw");
        block.SubBlocks[0].ModuleType.Should().Be("W3DModelDraw");
        block.SubBlocks[0].Tag.Should().Be("ModuleTag_01");
        block.SubBlocks[0].Fields.Should().ContainSingle(f => f.Key == "Model");
        block.Fields.Should().ContainSingle(f => f.Key == "BuildCost");
    }

    /// <summary>
    /// Tests that a module without its type or tag fails in strict mode.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ModuleWithoutTag_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank\nDraw = W3DModelDraw\nEnd\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("missing its type or tag");
    }

    /// <summary>
    /// Tests that a module without its own End fails in strict mode.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ModuleWithoutEnd_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank\nDraw = W3DModelDraw ModuleTag_01\nModel = tank.w3d\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("missing its End token");
    }

    /// <summary>
    /// Tests that duplicate blocks merge with last-wins field values.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_DuplicateBlocks_MergeLastWinsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank\nBuildCost = 100\nKindOf = A\nEnd\nObject Tank\nBuildCost = 200\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        block.Fields.Should().ContainSingle(f => f.Key == "BuildCost").Which.Values.Should().BeEquivalentTo("200");
        block.Fields.Should().ContainSingle(f => f.Key == "KindOf");
    }

    /// <summary>
    /// Tests that a reskin copies its parent and that non-visual fields are dropped like the engine.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_Reskin_CopiesParentDropsNonVisualAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Base\nBuildCost = 100\nKindOf = A\nEnd\nObjectReskin Skin Base\nBuildCost = 999\nGeometryHeight = 9\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        var skin = parsed.Data!.Blocks.Should().ContainSingle(b => b.Name == "Skin").Subject;
        skin.ParentName.Should().Be("Base");
        skin.Fields.Should().ContainSingle(f => f.Key == "BuildCost").Which.Values.Should().BeEquivalentTo("100");
        skin.Fields.Should().ContainSingle(f => f.Key == "KindOf");
        skin.Fields.Should().ContainSingle(f => f.Key == "GeometryHeight").Which.Values.Should().BeEquivalentTo("9");
        parsed.Data.Diagnostics.Should().Contain(d => d.Message.Contains("BuildCost"));
    }

    /// <summary>
    /// Tests that a reskin whose parent is missing fails in strict mode.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ReskinMissingParent_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "ObjectReskin Skin Ghost\nGeometryHeight = 9\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("must come after its parent 'Ghost'");
    }

    /// <summary>
    /// Tests that a reskin declared before its parent fails, like the engine ordering rule.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ReskinBeforeParent_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "ObjectReskin Skin Base\nGeometryHeight = 9\nEnd\nObject Base\nBuildCost = 100\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("must come after its parent");
    }

    /// <summary>
    /// Tests that an extend copies its parent and that own fields win.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_Extend_CopiesParentOwnWinsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var parent = new SageIniBlock("Object", "Base", null, [new SageIniField("BuildCost", ["100"]), new SageIniField("KindOf", ["A"])], [], "seed.ini", 1);
        var options = new SageIniParseOptions(KnownBlocks: new Dictionary<string, SageIniBlock> { ["Base"] = parent });
        var text = "ObjectExtend Plus Base\nBuildCost = 200\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", options);

        // Assert
        parsed.Success.Should().BeTrue();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        block.Fields.Should().ContainSingle(f => f.Key == "BuildCost").Which.Values.Should().BeEquivalentTo("200");
        block.Fields.Should().ContainSingle(f => f.Key == "KindOf");
    }

    /// <summary>
    /// Tests that a ChildObject inherits with extend semantics.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ChildObject_InheritsLikeExtendAsync()
    {
        // Arrange
        var sut = CreateSut();
        var parent = new SageIniBlock("Object", "Base", null, [new SageIniField("BuildCost", ["100"])], [], "seed.ini", 1);
        var options = new SageIniParseOptions(KnownBlocks: new Dictionary<string, SageIniBlock> { ["Base"] = parent });
        var text = "ChildObject Kid Base\nKindOf = B\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", options);

        // Assert
        parsed.Success.Should().BeTrue();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        block.ParentName.Should().Be("Base");
        block.Fields.Should().ContainSingle(f => f.Key == "BuildCost");
        block.Fields.Should().ContainSingle(f => f.Key == "KindOf");
    }

    /// <summary>
    /// Tests that a reskin Draw module replaces the parent Draw modules.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ReskinDraw_ReplacesParentDrawAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Base\nDraw = OldDraw TagOld\nEnd\nBehavior = OldBehavior TagB\nEnd\nEnd\nObjectReskin Skin Base\nDraw = NewDraw TagNew\nEnd\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        var skin = parsed.Data!.Blocks.Should().ContainSingle(b => b.Name == "Skin").Subject;
        skin.SubBlocks.Should().ContainSingle(s => s.Key == "Draw").Which.ModuleType.Should().Be("NewDraw");
        skin.SubBlocks.Should().ContainSingle(s => s.Key == "Behavior");
    }

    /// <summary>
    /// Tests that #define substitutes whole tokens on subsequent lines.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_Define_SubstitutesTokensAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "#define FAST 100 200\nObject Tank\nBuildCost = FAST\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks[0].Fields.Should().ContainSingle().Which.Values.Should().BeEquivalentTo("100", "200");
    }

    /// <summary>
    /// Tests that redefining a macro overwrites the previous value.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_DefineRedefined_LastWinsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "#define X 1\n#define X 2\nObject Tank\nBuildCost = X\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks[0].Fields.Should().ContainSingle().Which.Values.Should().BeEquivalentTo("2");
    }

    /// <summary>
    /// Tests that macro expansion is a single pass without recursive re-expansion.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_DefineSinglePass_NoRecursionAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "#define A B\n#define B C\nObject Tank\nKindOf = A\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks[0].Fields.Should().ContainSingle().Which.Values.Should().BeEquivalentTo("B");
    }

    /// <summary>
    /// Tests that #include splices another file inline.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_Include_SplicesInlineAsync()
    {
        // Arrange
        var sut = CreateSut();
        var reader = CreateReader(new Dictionary<string, string> { ["extra.ini"] = "Object Included\nBuildCost = 5\nEnd\n" });
        var options = new SageIniParseOptions(IncludeReader: reader);
        var text = "Object First\nEnd\n#include \"extra.ini\"\nObject Last\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "entry.ini", options);

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks.Select(b => b.Name).Should().BeEquivalentTo("First", "Included", "Last");
        parsed.Data.Blocks.Should().ContainSingle(b => b.Name == "Included").Which.SourceFile.Should().Be("extra.ini");
    }

    /// <summary>
    /// Tests that a missing include fails the file in strict mode.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_IncludeMissing_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var reader = CreateReader(new Dictionary<string, string>());
        var options = new SageIniParseOptions(IncludeReader: reader);
        var text = "#include \"ghost.ini\"\nObject Tank\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "entry.ini", options);

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("ghost.ini");
    }

    /// <summary>
    /// Tests that an include cycle fails the file in strict mode.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_IncludeCycle_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var reader = CreateReader(new Dictionary<string, string>
        {
            ["b.ini"] = "#include \"entry.ini\"\n",
            ["entry.ini"] = "Object FromCycle\nEnd\n",
        });
        var options = new SageIniParseOptions(IncludeReader: reader);
        var text = "#include \"b.ini\"\nObject Tank\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "entry.ini", options);

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("cycle");
    }

    /// <summary>
    /// Tests that deep include chains are rejected past the maximum depth.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_IncludeTooDeep_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var files = new Dictionary<string, string>();
        for (var i = 0; i < SageIniConstants.Grammar.MaxIncludeDepth + 2; i++)
        {
            files[$"f{i}.ini"] = $"#include \"f{i + 1}.ini\"\n";
        }

        files[$"f{SageIniConstants.Grammar.MaxIncludeDepth + 2}.ini"] = "Object Deep\nEnd\n";
        var options = new SageIniParseOptions(IncludeReader: CreateReader(files));

        // Act
        var parsed = await sut.ParseAsync("#include \"f0.ini\"\n", "entry.ini", options);

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("maximum include depth");
    }

    /// <summary>
    /// Tests that tolerated mode skips an unfinishable block and continues with the rest.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_TolerantMode_SkipsUnfinishableContinuesAsync()
    {
        // Arrange
        var sut = CreateSut();
        var options = new SageIniParseOptions(TolerateBlockFailures: true);
        var text = "Object Good\nEnd\nObjectReskin Orphan Ghost\nGeometryHeight = 1\nEnd\nObject After\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", options);

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks.Select(b => b.Name).Should().BeEquivalentTo("Good", "After");
        parsed.Data.SkippedBlocks.Should().ContainSingle().Which.HeaderLine.Should().Contain("ObjectReskin Orphan Ghost");
    }

    /// <summary>
    /// Tests that tolerated mode records unrecognized blocks and continues.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_TolerantMode_RecordsUnrecognizedAsync()
    {
        // Arrange
        var sut = CreateSut();
        var options = new SageIniParseOptions(BlockTable: SageIniConstants.BlockTables.WorldBuilder, TolerateBlockFailures: true);
        var text = "WaterSet Lake\nEnd\nObject Tank\nEnd\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", options);

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks.Should().ContainSingle().Which.Name.Should().Be("Tank");
        parsed.Data.UnrecognizedBlocks.Should().ContainSingle().Which.HeaderLine.Should().Contain("WaterSet Lake");
    }

    /// <summary>
    /// Tests that cancellation is observed cooperatively.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_Cancelled_ThrowsOperationCanceledExceptionAsync()
    {
        // Arrange
        var sut = CreateSut();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var act = () => sut.ParseAsync("Object Tank\nEnd\n", "test.ini", new SageIniParseOptions(), cancelled.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that a genuine ZH Draw module with one-line alias directives keeps the whole
    /// Object block together: alias lines own no End and must not disturb module boundaries.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_DrawWithAliasLines_ParsesWholeObjectAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text =
            "Object GLATank\n" +
            "RadarPriority = 5\n" +
            "Draw = W3DModelDraw ModuleTag_01\n" +
            "DefaultConditionState\n" +
            "Model = GLATank\n" +
            "End\n" +
            "ConditionState = REALLYDAMAGED\n" +
            "Model = GLATank_D\n" +
            "End\n" +
            "AliasConditionState = REALLYDAMAGED DAMAGED\n" +
            "TransitionState = FIRING_A FIRING_B\n" +
            "Animation = GLATank_Fire\n" +
            "End\n" +
            "End\n" +
            "Behavior = PhysicsBehavior ModuleTag_Physics\n" +
            "Mass = 1.0\n" +
            "End\n" +
            "End\n" +
            "Object GLAJeep\n" +
            "End\n";
        var options = new SageIniParseOptions(TolerateBlockFailures: true);

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", options);

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks.Select(b => b.Name).Should().BeEquivalentTo("GLATank", "GLAJeep");
        var block = parsed.Data.Blocks[0];
        block.Fields.Should().Contain(f => f.Key == "RadarPriority");
        var draw = block.SubBlocks.Should().Contain(s => s.Key == "Draw").Subject;
        draw.Fields.Should().Contain(f => f.Key == "AliasConditionState");
        draw.Fields.First(f => f.Key == "Model").Values.Should().Contain("GLATank");
        block.SubBlocks.Should().Contain(s => s.Key == "Behavior");
    }

    /// <summary>
    /// Tests that later-SAGE nested scopes in mod files are tolerated: their End lines
    /// must not close the enclosing Draw module.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_LaterSageNestedScopes_ToleratedAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text =
            "Object ModdedTank\n" +
            "Draw = W3DModelDraw ModuleTag_01\n" +
            "ConditionState = NONE\n" +
            "ModelConditionState = USER_1\n" +
            "Model = ModdedTank_D1\n" +
            "End\n" +
            "AnimationState = FIRING\n" +
            "Animation = ModdedTank_Fire\n" +
            "End\n" +
            "End\n" +
            "End\n" +
            "End\n";
        var options = new SageIniParseOptions(TolerateBlockFailures: true);

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", options);

        // Assert
        parsed.Success.Should().BeTrue();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        block.SubBlocks.Should().Contain(s => s.Key == "Draw");
        block.SubBlocks.Should().HaveCount(1);
    }

    /// <summary>
    /// Tests that a stray top-level End is skipped as a single line without eating neighbors.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_TolerantStrayEnd_SkipsLineAndKeepsNeighborsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object A\nEnd\nEnd\nObject B\nEnd\n";
        var options = new SageIniParseOptions(TolerateBlockFailures: true);

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", options);

        // Assert
        parsed.Success.Should().BeTrue();
        parsed.Data!.Blocks.Select(b => b.Name).Should().BeEquivalentTo("A", "B");
        parsed.Data.UnrecognizedBlocks.Should().ContainSingle().Which.HeaderLine.Should().Be("End");
    }

    /// <summary>
    /// Tests that FXList Sound and ParticleSystem nuggets parse as nested scopes, like real game FXList files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_TolerantFxListNuggets_ParsesSubBlocksAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text =
            "FXList FXT_Gatling\n" +
            "Sound\n" +
            "Name = GatlingFire\n" +
            "End\n" +
            "ParticleSystem\n" +
            "Name = ExMuzzleGatTank\n" +
            "Offset = X:0.0 Y:2.0 Z:1.0\n" +
            "End\n" +
            "End\n";
        var options = new SageIniParseOptions(TolerateBlockFailures: true);

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", options);

        // Assert
        parsed.Success.Should().BeTrue();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        block.SubBlocks.Select(s => s.Key).Should().BeEquivalentTo("Sound", "ParticleSystem");
    }

    /// <summary>
    /// Tests that ObjectCreationList CreateObject and ApplyRandomForce nuggets parse as nested scopes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_TolerantObjectCreationListNuggets_ParsesSubBlocksAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text =
            "ObjectCreationList OCL_A10Strike\n" +
            "CreateObject\n" +
            "ObjectNames = A10Thunderbolt\n" +
            "Count = 1\n" +
            "End\n" +
            "ApplyRandomForce\n" +
            "MagnitudeMin = 10.0\n" +
            "MagnitudeMax = 20.0\n" +
            "End\n" +
            "End\n";
        var options = new SageIniParseOptions(TolerateBlockFailures: true);

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", options);

        // Assert
        parsed.Success.Should().BeTrue();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        block.SubBlocks.Select(s => s.Key).Should().BeEquivalentTo("CreateObject", "ApplyRandomForce");
    }

    private static SageIniParser CreateSut()
    {
        return new SageIniParser(NullLogger<SageIniParser>.Instance);
    }

    private static Func<string, string, CancellationToken, Task<OperationResult<(string ResolvedPath, string Text)>>> CreateReader(Dictionary<string, string> files)
    {
        return (includingFile, includePath, cancellationToken) =>
        {
            var key = includePath.Trim().Trim('"');
            var result = files.TryGetValue(key, out var text)
                ? OperationResult<(string ResolvedPath, string Text)>.CreateSuccess((key, text))
                : OperationResult<(string ResolvedPath, string Text)>.CreateFailure($"Include '{key}' was not found.");
            return Task.FromResult(result);
        };
    }
}
