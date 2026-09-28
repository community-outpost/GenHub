using FluentAssertions;
using GenHub.Core.Models.Validation;
using GenHub.Features.Tools.IniEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.IniEditor.Services;

/// <summary>
/// Unit tests for <see cref="IniDocumentService"/>.
/// </summary>
public sealed class IniDocumentServiceTests : IDisposable
{
    private const string SampleDocument =
        "; Generals object definition\n" +
        "Object AmericaVehicleHumvee\n" +
        "  DisplayName = OBJECT:Humvee\n" +
        "  Side = USA\n" +
        "  BuildCost = 800\n" +
        "  Health = 300.0\n" +
        "  WeaponSet\n" +
        "    Conditions = None\n" +
        "    PRIMARY = HumveeMissileWeapon\n" +
        "  End\n" +
        "End\n" +
        "\n" +
        "Weapon HumveeMissileWeapon\n" +
        "  PrimaryDamage = 50.0\n" +
        "  DamageType = EXPLOSION\n" +
        "End\n";

    private readonly Mock<ILogger<IniDocumentService>> _mockLogger;
    private readonly IniDocumentService _service;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="IniDocumentServiceTests"/> class.
    /// </summary>
    public IniDocumentServiceTests()
    {
        _mockLogger = new Mock<ILogger<IniDocumentService>>();
        _service = new IniDocumentService(_mockLogger.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that a representative object and weapon document parses with nesting and comments.
    /// </summary>
    [Fact]
    public void ParseText_ValidDocument_ReturnsBlocksWithChildren()
    {
        var result = _service.ParseText(SampleDocument);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Blocks.Should().HaveCount(2);
        var gameObject = result.Data.Blocks[0];
        gameObject.BlockType.Should().Be("Object");
        gameObject.Name.Should().Be("AmericaVehicleHumvee");
        gameObject.Fields.Should().HaveCount(4);
        gameObject.Children.Should().HaveCount(1);
        gameObject.Children[0].BlockType.Should().Be("WeaponSet");
        result.Data.Blocks[1].BlockType.Should().Be("Weapon");
    }

    /// <summary>
    /// Verifies that a block missing End fails parsing.
    /// </summary>
    [Fact]
    public void ParseText_MissingEnd_ReturnsFailure()
    {
        var result = _service.ParseText("Object MissingEnd\n  Health = 100.0\n");

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("missing 'End'");
    }

    /// <summary>
    /// Verifies that tab characters are rejected following engine INI rules.
    /// </summary>
    [Fact]
    public void ParseText_TabCharacter_ReturnsFailure()
    {
        var result = _service.ParseText("Object Tabbed\n\tHealth = 100.0\nEnd\n");

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Tab");
    }

    /// <summary>
    /// Verifies that tabs inside comments do not fail parsing.
    /// </summary>
    [Fact]
    public void ParseText_TabInsideComment_ParsesSuccessfully()
    {
        var result = _service.ParseText("Object Tabbed ;\tnote with tab\n  Health = 100.0\nEnd\n");

        result.Success.Should().BeTrue();
        result.Data!.Blocks.Should().HaveCount(1);
    }

    /// <summary>
    /// Verifies that a field with a missing key fails parsing instead of becoming a block.
    /// </summary>
    [Fact]
    public void ParseText_MissingKey_ReturnsFailure()
    {
        var result = _service.ParseText("Object NoKey\n  = 100\nEnd\n");

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("missing a key");
    }

    /// <summary>
    /// Verifies that file-scope settings outside of any block parse as global fields,
    /// matching shipped flat files such as <c>GameLODPresets.ini</c>.
    /// </summary>
    [Fact]
    public void ParseText_TopLevelFields_ParseAsGlobalFields()
    {
        const string content =
            "; LOD presets\n" +
            "ReallyLowMHz = 600\n" +
            "LODPreset = LOW P3 1400 GF3 128\n" +
            "LODPreset = HIGH P4 2000 GF4 512\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        var document = result.Data!;
        document.Blocks.Should().BeEmpty();
        document.HeaderComments.Should().ContainSingle().Which.Text.Should().Be("LOD presets");
        document.GlobalFields.Select(field => field.Key).Should().Equal("ReallyLowMHz", "LODPreset", "LODPreset");
        document.GlobalFields[0].Value.Should().Be("600");

        var canonical = _service.WriteDocument(document);
        canonical.Should().Be(
            "; LOD presets\r\n" +
            "ReallyLowMHz = 600\r\n" +
            "LODPreset = LOW P3 1400 GF3 128\r\n" +
            "LODPreset = HIGH P4 2000 GF4 512\r\n");

        var reparsed = _service.ParseText(canonical);
        reparsed.Success.Should().BeTrue();
        _service.WriteDocument(reparsed.Data!).Should().Be(canonical);
    }

    /// <summary>
    /// Verifies that an unexpected End without an open block fails parsing.
    /// </summary>
    [Fact]
    public void ParseText_UnexpectedEnd_ReturnsFailure()
    {
        var result = _service.ParseText("End\n");

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Unexpected 'End'");
    }

    /// <summary>
    /// Verifies that semicolon comments are stripped from values and do not affect parsing.
    /// </summary>
    [Fact]
    public void ParseText_InlineComments_AreStripped()
    {
        var result = _service.ParseText("Object Commented ; trailing comment\n  Health = 100.0 ; hit points\nEnd\n");

        result.Success.Should().BeTrue();
        result.Data!.Blocks[0].Fields[0].Value.Should().Be("100.0");
    }

    /// <summary>
    /// Verifies that header, leading, and trailing comments enter the document model.
    /// </summary>
    [Fact]
    public void ParseText_Comments_ArePreservedInModel()
    {
        var result = _service.ParseText(SampleDocument);

        result.Success.Should().BeTrue();
        var document = result.Data!;
        document.HeaderComments.Should().ContainSingle().Which.Text.Should().Be("Generals object definition");
        document.HeaderComments[0].IsDirective.Should().BeFalse();
        document.Blocks[0].LeadingComments.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that leading and inline comments round-trip through the writer.
    /// </summary>
    [Fact]
    public void WriteDocument_Comments_RoundTrip()
    {
        const string content =
            "; file header\n" +
            "Object Commented ; header note\n" +
            "  ; leading field note\n" +
            "  Health = 100.0 ; hit points\n" +
            "  ; note before end\n" +
            "End\n" +
            "; file trailer\n";

        var parsed = _service.ParseText(content);
        parsed.Success.Should().BeTrue();

        var canonical = _service.WriteDocument(parsed.Data!);

        canonical.Should().Contain("; file header");
        canonical.Should().Contain("Object Commented ; header note");
        canonical.Should().Contain("; leading field note");
        canonical.Should().Contain("Health = 100.0 ; hit points");
        canonical.Should().Contain("; note before end");
        canonical.Should().Contain("; file trailer");

        var reparsed = _service.ParseText(canonical);
        reparsed.Success.Should().BeTrue();
        _service.WriteDocument(reparsed.Data!).Should().Be(canonical);
    }

    /// <summary>
    /// Verifies that writing a parsed document round-trips through the parser.
    /// </summary>
    [Fact]
    public void WriteDocument_ParsedDocument_RoundTrips()
    {
        var parsed = _service.ParseText(SampleDocument);
        parsed.Success.Should().BeTrue();

        var canonical = _service.WriteDocument(parsed.Data!);
        var reparsed = _service.ParseText(canonical);

        reparsed.Success.Should().BeTrue();
        reparsed.Data!.Blocks.Should().HaveCount(2);
        reparsed.Data.Blocks[0].Fields.Should().HaveCount(4);
    }

    /// <summary>
    /// Verifies that validation reports duplicate blocks as warnings without failing.
    /// </summary>
    [Fact]
    public void ValidateDocument_DuplicateBlocks_ReportsWarning()
    {
        var parsed = _service.ParseText("Object Same\n  Health = 1.0\nEnd\nObject Same\n  Health = 2.0\nEnd\n");
        parsed.Success.Should().BeTrue();

        var validation = _service.ValidateDocument(parsed.Data!, "test");

        validation.Issues.Should().Contain(issue => issue.Severity == ValidationSeverity.Warning);
    }

    /// <summary>
    /// Verifies that validating a missing file reports a missing file issue.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateFileAsync_MissingFile_ReportsMissingFile()
    {
        var missing = Path.Combine(_tempDirectory, "Missing.ini");

        var validation = await _service.ValidateFileAsync(missing, CancellationToken.None);

        validation.IsValid.Should().BeFalse();
        validation.MissingFilesCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies that formatting a file rewrites its exact bytes in canonical form.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_ValidFile_RewritesCanonically()
    {
        var filePath = Path.Combine(_tempDirectory, "GameData.ini");
        const string messy =
            "; messy header\n" +
            "Object   MessyObject\n" +
            "Health=300.0\n" +
            "   Side   =   USA\n" +
            "End\n";
        await File.WriteAllTextAsync(filePath, messy);

        var result = await _service.FormatFileAsync(filePath, CancellationToken.None);

        result.Success.Should().BeTrue();
        var formatted = await File.ReadAllTextAsync(filePath);
        formatted.Should().NotBe(messy);
        formatted.Should().Be(
            "; messy header\r\n" +
            "Object MessyObject\r\n" +
            "  Health = 300.0\r\n" +
            "  Side = USA\r\n" +
            "End\r\n" +
            "\r\n");
        var reparsed = _service.ParseText(formatted);
        reparsed.Success.Should().BeTrue();
        _service.WriteDocument(reparsed.Data!).Should().Be(formatted);
    }

    /// <summary>
    /// Verifies that formatting preserves comments instead of stripping them.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_FileWithComments_PreservesComments()
    {
        var filePath = Path.Combine(_tempDirectory, "Commented.ini");
        await File.WriteAllTextAsync(filePath, SampleDocument);

        var result = await _service.FormatFileAsync(filePath, CancellationToken.None);

        result.Success.Should().BeTrue();
        var formatted = await File.ReadAllTextAsync(filePath);
        formatted.Should().Contain("; Generals object definition");
    }

    /// <summary>
    /// Verifies that a UTF-8 BOM is stripped instead of corrupting the first block.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileAsync_Utf8Bom_StripsBom()
    {
        var filePath = Path.Combine(_tempDirectory, "Bom.ini");
        var bytes = new UTF8Encoding(true).GetBytes("Object BomObject\n  Health = 1.0\nEnd\n");
        await File.WriteAllBytesAsync(filePath, bytes);

        var result = await _service.ParseFileAsync(filePath, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Blocks.Should().ContainSingle();
        result.Data.Blocks[0].BlockType.Should().Be("Object");
        result.Data.Blocks[0].Name.Should().Be("BomObject");
    }

    /// <summary>
    /// Verifies that ANSI encoded files decode instead of being rejected.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileAsync_AnsiEncoding_DecodesText()
    {
        var filePath = Path.Combine(_tempDirectory, "Ansi.ini");
        var bytes = Encoding.Latin1.GetBytes("; caf\xE9 comment\nObject Caf\xE9\n  DisplayName = Caf\xE9\nEnd\n");
        await File.WriteAllBytesAsync(filePath, bytes);

        var result = await _service.ParseFileAsync(filePath, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Blocks.Should().ContainSingle();
        result.Data.Blocks[0].Fields[0].Value.Should().Be("Caf\xE9");
    }

    /// <summary>
    /// Verifies that a pre-cancelled format throws and leaves no temp file behind.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_Canceled_ThrowsAndLeavesNoTempFile()
    {
        var filePath = Path.Combine(_tempDirectory, "Cancelled.ini");
        await File.WriteAllTextAsync(filePath, SampleDocument);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var act = () => _service.FormatFileAsync(filePath, canceled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(_tempDirectory).Should().ContainSingle();
        (await File.ReadAllTextAsync(filePath)).Should().Be(SampleDocument);
    }

    /// <summary>
    /// Verifies that formatting a file with parse errors fails without touching the original bytes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_ParseFailure_LeavesOriginalBytesIntact()
    {
        var filePath = Path.Combine(_tempDirectory, "Broken.ini");
        const string broken = "Object BrokenObject\n  Health = 1.0\n";
        await File.WriteAllTextAsync(filePath, broken);

        var result = await _service.FormatFileAsync(filePath, CancellationToken.None);

        result.Success.Should().BeFalse();
        (await File.ReadAllTextAsync(filePath)).Should().Be(broken);
    }

    /// <summary>
    /// Verifies that validating a file with parse errors reports a corrupted file issue.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateFileAsync_ParseError_ReportsCorruptedFile()
    {
        var filePath = Path.Combine(_tempDirectory, "Broken.ini");
        await File.WriteAllTextAsync(filePath, "Object BrokenObject\n  Health = 1.0\n");

        var validation = await _service.ValidateFileAsync(filePath, CancellationToken.None);

        validation.IsValid.Should().BeFalse();
        validation.CorruptedFilesCount.Should().Be(1);
        validation.Issues.Should().OnlyContain(issue => issue.IssueType == ValidationIssueType.CorruptedFile);
    }

    /// <summary>
    /// Verifies that formatting a UTF-8 BOM file preserves the byte order mark.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_Utf8Bom_PreservesBom()
    {
        var filePath = Path.Combine(_tempDirectory, "BomFormat.ini");
        var bytes = new UTF8Encoding(true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("Object BomObject\n  Health = 1.0\nEnd\n"))
            .ToArray();
        await File.WriteAllBytesAsync(filePath, bytes);

        var result = await _service.FormatFileAsync(filePath, CancellationToken.None);

        result.Success.Should().BeTrue();
        var formatted = await File.ReadAllBytesAsync(filePath);
        formatted.Take(3).Should().Equal((byte)0xEF, (byte)0xBB, (byte)0xBF);
    }

    /// <summary>
    /// Verifies that formatting an ANSI file keeps its single byte encoding.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_AnsiEncoding_PreservesEncoding()
    {
        var filePath = Path.Combine(_tempDirectory, "AnsiFormat.ini");
        var bytes = Encoding.Latin1.GetBytes("; caf\xE9 comment\nObject Caf\xE9\n  DisplayName = Caf\xE9\nEnd\n");
        await File.WriteAllBytesAsync(filePath, bytes);

        var result = await _service.FormatFileAsync(filePath, CancellationToken.None);

        result.Success.Should().BeTrue();
        var formatted = await File.ReadAllBytesAsync(filePath);
        formatted.Should().Contain((byte)0xE9);
        formatted.Should().NotContain((byte)0xC3);
    }

    /// <summary>
    /// Verifies that parsing a missing file fails with a not found error.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileAsync_MissingFile_ReturnsFailure()
    {
        var result = await _service.ParseFileAsync(Path.Combine(_tempDirectory, "Missing.ini"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("not found");
    }

    /// <summary>
    /// Verifies that a realistic object with body, behavior, and draw modules parses
    /// with module sub-blocks instead of reporting fields outside of a block.
    /// Alias lines stack as fields of the draw module following engine semantics:
    /// they never open their own sub-blocks.
    /// </summary>
    [Fact]
    public void ParseText_ObjectWithModules_ParsesModuleSubBlocks()
    {
        const string content =
            "Object AmericaVehicleHumvee\n" +
            "  DisplayName = OBJECT:Humvee\n" +
            "  Body = ActiveBody ModuleTag_01\n" +
            "    MaxHealth = 300.0\n" +
            "  End\n" +
            "  Behavior = PhysicsBehavior ModuleTag_02\n" +
            "  End\n" +
            "  Draw = W3DModelDraw ModuleTag_03\n" +
            "    ConditionState = NONE\n" +
            "      Model = AVHUMVEE\n" +
            "    End\n" +
            "    AliasConditionState = NONE DAMAGED\n" +
            "    AliasConditionState = NONE REALLYDAMAGED\n" +
            "    ConditionState = DAMAGED\n" +
            "      Model = AVHUMVEE_D\n" +
            "    End\n" +
            "    TransitionState = TRANS_Stand TRANS_StandInjured\n" +
            "      Animation = Anim\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        var gameObject = result.Data!.Blocks.Should().ContainSingle().Subject;
        gameObject.Fields.Should().ContainSingle();
        gameObject.Children.Should().HaveCount(3);
        var draw = gameObject.Children[2];
        draw.BlockType.Should().Be("Draw");
        draw.AssignmentValue.Should().Be("W3DModelDraw ModuleTag_03");
        draw.DisplayHeader.Should().Be("Draw = W3DModelDraw ModuleTag_03");
        draw.Children.Should().HaveCount(3);
        draw.Children[0].DisplayHeader.Should().Be("ConditionState = NONE");
        draw.Children[1].DisplayHeader.Should().Be("ConditionState = DAMAGED");
        draw.Children[2].DisplayHeader.Should().Be("TransitionState = TRANS_Stand TRANS_StandInjured");
        draw.Fields.Should().HaveCount(2);
        draw.Fields.Should().OnlyContain(field => field.Key == "AliasConditionState");
        draw.Fields[0].Value.Should().Be("NONE DAMAGED");
        draw.Fields[1].Value.Should().Be("NONE REALLYDAMAGED");
    }

    /// <summary>
    /// Verifies that trailing alias lines inside a condition state parse as fields
    /// of that state, matching shipped building draw modules.
    /// </summary>
    [Fact]
    public void ParseText_TrailingAliases_ParseAsStateFields()
    {
        const string content =
            "Object Lazr_AmericaBarracks\n" +
            "  Draw = W3DModelDraw ModuleTag_01\n" +
            "    ConditionState = SOLD NIGHT\n" +
            "      Model = ABBARRACKS\n" +
            "      AliasConditionState = SOLD NIGHT SNOW\n" +
            "      AliasConditionState = SOLD NIGHT SNOW DAMAGED\n" +
            "      OkToChangeModelColor = YES\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        var state = result.Data!.Blocks[0].Children[0].Children.Should().ContainSingle().Subject;
        state.DisplayHeader.Should().Be("ConditionState = SOLD NIGHT");
        state.Fields.Select(field => field.Key).Should().Equal("Model", "AliasConditionState", "AliasConditionState", "OkToChangeModelColor");
    }

    /// <summary>
    /// Verifies that bare valueless entries parse as bare fields and round-trip verbatim.
    /// </summary>
    [Fact]
    public void ParseText_BlankEntries_ParseAsBareFields()
    {
        const string content =
            "Object CreditsPage\n" +
            "  Text = CREDITS:Foo\n" +
            "  Blank\n" +
            "  Text = CREDITS:Bar\n" +
            "  Blank\n" +
            "  Blank\n" +
            "End\n";

        var parsed = _service.ParseText(content);

        parsed.Success.Should().BeTrue();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        block.Children.Should().BeEmpty();
        block.Fields.Select(field => field.Key).Should().Equal("Text", "Blank", "Text", "Blank", "Blank");
        block.Fields.Where(field => field.Key == "Blank").Should().OnlyContain(field => field.IsBare);

        var canonical = _service.WriteDocument(parsed.Data!);
        canonical.Should().Contain("\r\n  Blank\r\n");
        canonical.Should().NotContain("Blank = ");

        var reparsed = _service.ParseText(canonical);
        reparsed.Success.Should().BeTrue();
        _service.WriteDocument(reparsed.Data!).Should().Be(canonical);
    }

    /// <summary>
    /// Verifies that an empty module closed immediately still closes the module, not the parent.
    /// </summary>
    [Fact]
    public void ParseText_EmptyModule_ClosesModuleOnly()
    {
        var result = _service.ParseText("Object Foo\n  Behavior = PhysicsBehavior Tag\n  End\n  Health = 10.0\nEnd\n");

        result.Success.Should().BeTrue();
        var gameObject = result.Data!.Blocks.Should().ContainSingle().Subject;
        gameObject.Children.Should().ContainSingle();
        gameObject.Fields.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that module keys open sub-blocks even in files without indentation.
    /// </summary>
    [Fact]
    public void ParseText_FlatFile_ModulesOpenViaSchemaKeys()
    {
        const string content =
            "Object Foo\n" +
            "DisplayName = X\n" +
            "Draw = W3DModelDraw Tag\n" +
            "ConditionState = NONE\n" +
            "Model = AVFoo\n" +
            "End\n" +
            "End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        var draw = result.Data!.Blocks[0].Children.Should().ContainSingle().Subject;
        draw.Children.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that preprocessor directives are preserved verbatim through a round-trip.
    /// </summary>
    [Fact]
    public void WriteDocument_Directives_RoundTripVerbatim()
    {
        const string content =
            "#include \"Common.ini\"\n" +
            "Object Foo\n" +
            "  Health = 10.0\n" +
            "End\n";

        var parsed = _service.ParseText(content);
        parsed.Success.Should().BeTrue();
        parsed.Data!.HeaderComments.Should().ContainSingle().Which.IsDirective.Should().BeTrue();

        var canonical = _service.WriteDocument(parsed.Data!);
        canonical.Should().Contain("#include \"Common.ini\"");
        canonical.Should().NotContain("; #include");
    }

    /// <summary>
    /// Verifies that module headers survive a write and reparse cycle.
    /// </summary>
    [Fact]
    public void WriteDocument_ModuleBlocks_RoundTrip()
    {
        const string content =
            "Object Foo\n" +
            "  Draw = W3DModelDraw Tag\n" +
            "    ConditionState = NONE\n" +
            "      Model = AVFoo\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var parsed = _service.ParseText(content);
        parsed.Success.Should().BeTrue();

        var canonical = _service.WriteDocument(parsed.Data!);
        canonical.Should().Contain("Draw = W3DModelDraw Tag");
        canonical.Should().Contain("ConditionState = NONE");

        var reparsed = _service.ParseText(canonical);
        reparsed.Success.Should().BeTrue();
        _service.WriteDocument(reparsed.Data!).Should().Be(canonical);
    }

    /// <summary>
    /// Verifies that same-named blocks under different parents do not report duplicates.
    /// </summary>
    [Fact]
    public void ValidateDocument_SameNameInDifferentParents_NoDuplicateWarning()
    {
        const string content =
            "Object Foo\n" +
            "  Draw = W3DModelDraw TagA\n" +
            "    ConditionState = NONE\n" +
            "      Model = A\n" +
            "    End\n" +
            "  End\n" +
            "  Draw = W3DModelDraw TagB\n" +
            "    ConditionState = NONE\n" +
            "      Model = B\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var parsed = _service.ParseText(content);
        parsed.Success.Should().BeTrue();

        var validation = _service.ValidateDocument(parsed.Data!, "test");

        validation.Issues.Should().NotContain(issue => issue.Message.Contains("Duplicate"));
    }

    /// <summary>
    /// Verifies that weapon set condition and slot lines remain plain fields.
    /// </summary>
    [Fact]
    public void ParseText_WeaponSetConditions_RemainFields()
    {
        const string content =
            "WeaponSet MySet\n" +
            "  Conditions = None PLAYER_UPGRADE\n" +
            "  Weapon = PRIMARY WeaponA\n" +
            "  Weapon = SECONDARY WeaponB\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        var set = result.Data!.Blocks.Should().ContainSingle().Subject;
        set.Fields.Should().HaveCount(3);
        set.Children.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that whitespace-separated key/value lines inside blocks are parsed as fields.
    /// </summary>
    [Fact]
    public void ParseText_WhitespaceSeparatedFields_ParsedAsFields()
    {
        const string content =
            "ControlBarScheme Retail\n" +
            "  ScreenHeight 768\n" +
            "  Side America\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.Blocks.Should().ContainSingle();
        var block = result.Data.Blocks[0];
        block.Fields.Should().HaveCount(2);
        block.Fields[0].Key.Should().Be("ScreenHeight");
        block.Fields[0].Value.Should().Be("768");
        block.Fields[1].Key.Should().Be("Side");
        block.Fields[1].Value.Should().Be("America");
    }

    /// <summary>
    /// Verifies that scanning block headers handles nameless nested blocks without desynchronizing.
    /// </summary>
    [Fact]
    public void ScanBlockHeaders_WithNamelessNestedBlocks_ReturnsOnlyTopLevelHeaders()
    {
        const string docWithNestedBlocks = """
            ; Generals object definition
            Object AmericaVehicleHumvee
              DisplayName = OBJECT:Humvee
              Side = USA
              BuildCost = 800
              Health = 300.0
              WeaponSet
                Conditions = None
                PRIMARY = HumveeMissileWeapon
              End
              ArmorSet
                Conditions = None
                Armor = HumveeArmor
              End
            End

            Weapon HumveeMissileWeapon
              PrimaryDamage = 50.0
              DamageType = EXPLOSION
            End
            """;

        var headers = IniReferenceService.ScanBlockHeaders(docWithNestedBlocks);

        headers.Should().HaveCount(2);
        headers[0].Should().Be(("Object", "AmericaVehicleHumvee"));
        headers[1].Should().Be(("Weapon", "HumveeMissileWeapon"));
    }
}
