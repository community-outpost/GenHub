using FluentAssertions;
using GenHub.Core.Interfaces.Common;
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
    private readonly Mock<ILocalizationService> _mockLocalization;
    private readonly IniDocumentService _service;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="IniDocumentServiceTests"/> class.
    /// </summary>
    public IniDocumentServiceTests()
    {
        _mockLogger = new Mock<ILogger<IniDocumentService>>();
        _mockLocalization = new Mock<ILocalizationService>();
        _mockLocalization
            .Setup(service => service.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => key);
        _service = new IniDocumentService(_mockLogger.Object, _mockLocalization.Object);
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
    /// Verifies that a block missing End recovers with a diagnostic instead of failing,
    /// so editors can open and repair real-world files such as map overrides.
    /// </summary>
    [Fact]
    public void ParseText_MissingEnd_RecoversBlockAndReportsDiagnostic()
    {
        var result = _service.ParseText("Object MissingEnd\n  Health = 100.0\n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Blocks.Should().ContainSingle();
        result.Data.Blocks[0].Fields.Should().ContainSingle(f => f.Key == "Health");
        result.Data.HasParseErrors.Should().BeTrue();
        result.Data.ParseErrors.Should().ContainSingle().Which.Should().Contain("missing 'End'");
    }

    /// <summary>
    /// Verifies that tab characters are normalized to spaces and parsed successfully.
    /// </summary>
    [Fact]
    public void ParseText_TabCharacter_NormalizesAndParsesSuccessfully()
    {
        var result = _service.ParseText("Object Tabbed\n\tHealth = 100.0\nEnd\n");

        result.Success.Should().BeTrue();
        result.Data!.Blocks.Should().HaveCount(1);
        result.Data.Blocks[0].Fields.Should().ContainSingle(f => f.Key == "Health" && f.Value == "100.0");
    }

    /// <summary>
    /// Verifies that tabs inside quoted values are preserved.
    /// </summary>
    [Fact]
    public void ParseText_TabInsideStringValue_Preserved()
    {
        var result = _service.ParseText("Object Tabbed\n  DisplayName = \"Name\tWithTab\"\nEnd\n");

        result.Success.Should().BeTrue();
        result.Data!.Blocks.Should().HaveCount(1);
        result.Data.Blocks[0].Fields.Should().ContainSingle(f => f.Key == "DisplayName" && f.Value == "\"Name\tWithTab\"");
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
    /// Verifies that a field with a missing key is skipped with a diagnostic instead of failing parsing.
    /// </summary>
    [Fact]
    public void ParseText_MissingKey_SkipsFieldAndReportsDiagnostic()
    {
        var result = _service.ParseText("Object NoKey\n  = 100\nEnd\n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Blocks.Should().ContainSingle();
        result.Data.Blocks[0].Fields.Should().BeEmpty();
        result.Data.HasParseErrors.Should().BeTrue();
        result.Data.ParseErrors.Should().ContainSingle().Which.Should().Contain("missing a key");
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
    /// Verifies that an unexpected End without an open block is ignored with a diagnostic instead of failing parsing.
    /// </summary>
    [Fact]
    public void ParseText_UnexpectedEnd_ReportsDiagnostic()
    {
        var result = _service.ParseText("End\n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Blocks.Should().BeEmpty();
        result.Data.HasParseErrors.Should().BeTrue();
        result.Data.ParseErrors.Should().ContainSingle().Which.Should().Contain("Unexpected 'End'");
    }

    /// <summary>
    /// Verifies that map-style <c>End</c> markers with trailing whitespace close blocks.
    /// </summary>
    [Fact]
    public void ParseText_TrailingSpaceEnd_ParsesSuccessfully()
    {
        var result = _service.ParseText("PlayerTemplate FactionAmerica\nProductionTimeChange = AmericaCommandCenter -80%\nEnd \n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.HasParseErrors.Should().BeFalse();
        result.Data.Blocks.Should().ContainSingle();
        result.Data.Blocks[0].Fields.Should().ContainSingle(f => f.Key == "ProductionTimeChange");
    }

    /// <summary>
    /// Verifies that ragged indentation inside a flat map section never opens modules,
    /// so each section keeps its own <c>End</c> and sibling blocks stay siblings.
    /// </summary>
    [Fact]
    public void ParseText_RaggedIndentFlatSection_StaysFlatWithSiblings()
    {
        var result = _service.ParseText(
            "PlayerTemplate FactionAmerica\n" +
            "ProductionTimeChange = AmericaCommandCenter -80%\n" +
            " ProductionTimeChange = GLAVehicleBombTruck -90%\n" +
            "  ProductionTimeChange = GLAVehicleScudLauncher -90%\n" +
            "End\n" +
            "PlayerTemplate FactionChina\n" +
            "ProductionTimeChange = ChinaCommandCenter -80%\n" +
            "End\n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.HasParseErrors.Should().BeFalse();
        result.Data.Blocks.Should().HaveCount(2);
        result.Data.Blocks[0].Children.Should().BeEmpty();
        result.Data.Blocks[0].Fields.Should().HaveCount(3);
        result.Data.Blocks[1].Name.Should().Be("FactionChina");
    }

    /// <summary>
    /// Verifies that an unclosed map override block recovers with all fields intact and a
    /// repair that re-emits the missing <c>End</c> on save.
    /// </summary>
    [Fact]
    public void ParseText_UnclosedPlayerTemplate_RecoversBlockAndReportsDiagnostic()
    {
        var result = _service.ParseText(
            "PlayerTemplate FactionBossGeneral\n" +
            "ProductionTimeChange = AmericaCommandCenter -80%\n" +
            "ProductionTimeChange = AmericaVehicleDozer -80%\n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Blocks.Should().ContainSingle();
        result.Data.Blocks[0].Name.Should().Be("FactionBossGeneral");
        result.Data.Blocks[0].Fields.Should().HaveCount(2);
        result.Data.HasParseErrors.Should().BeTrue();
        result.Data.ParseErrors.Should().ContainSingle().Which.Should().Contain("missing 'End'");

        var canonical = _service.WriteDocument(result.Data);
        canonical.Should().Contain("End");
        var reparsed = _service.ParseText(canonical);
        reparsed.Success.Should().BeTrue();
        reparsed.Data!.HasParseErrors.Should().BeFalse();
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
    /// Verifies that recovered parse diagnostics surface as validation errors.
    /// </summary>
    [Fact]
    public void ValidateDocument_ParseErrors_SurfaceAsErrors()
    {
        var parsed = _service.ParseText("Object BrokenObject\n  Health = 1.0\n");
        parsed.Success.Should().BeTrue();

        var validation = _service.ValidateDocument(parsed.Data!, "test");

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().ContainSingle()
            .Which.Message.Should().Contain("missing 'End'");
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
        var body = new UTF8Encoding(false).GetBytes("Object BomObject\n  Health = 1.0\nEnd\n");
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(body).ToArray();
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
    /// Verifies that formatting a file with recovered parse errors repairs it by
    /// re-emitting the missing <c>End</c> markers.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_RecoveredParseError_RepairsMissingEnd()
    {
        var filePath = Path.Combine(_tempDirectory, "Broken.ini");
        const string broken = "Object BrokenObject\n  Health = 1.0\n";
        await File.WriteAllTextAsync(filePath, broken);

        var result = await _service.FormatFileAsync(filePath, CancellationToken.None);

        result.Success.Should().BeTrue();
        var formatted = await File.ReadAllTextAsync(filePath);
        formatted.Should().Contain("End");
        var reparsed = _service.ParseText(formatted);
        reparsed.Success.Should().BeTrue();
        reparsed.Data!.HasParseErrors.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a keyless field is reported as discarded content so format
    /// and save paths refuse to serialize the recovered document.
    /// </summary>
    [Fact]
    public void ParseText_KeylessField_ReportsDiscardedContent()
    {
        var result = _service.ParseText("Object Keyless\n  = 100\nEnd\n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.HasParseErrors.Should().BeTrue();
        result.Data.HasDiscardedContent.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that missing End repair does not flag discarded content, so
    /// formatting a repaired document stays available.
    /// </summary>
    [Fact]
    public void ParseText_MissingEnd_KeepsRepairWithoutDiscardFlag()
    {
        var result = _service.ParseText("Object MissingEnd\n  Health = 100.0\n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.HasParseErrors.Should().BeTrue();
        result.Data.HasDiscardedContent.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an unexpected End marker is reported as discarded content.
    /// </summary>
    [Fact]
    public void ParseText_UnexpectedEnd_ReportsDiscardedContent()
    {
        var result = _service.ParseText("End\nObject AfterEnd\n  Health = 1.0\nEnd\n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.HasParseErrors.Should().BeTrue();
        result.Data.HasDiscardedContent.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that formatting refuses to overwrite a file whose recovery
    /// discarded source lines, leaving the original bytes untouched and
    /// reporting the localized refusal message.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_DiscardedContent_RefusesAndPreservesFile()
    {
        var filePath = Path.Combine(_tempDirectory, "Lossy.ini");
        const string lossy = "Object Lossy\n  = 100\nEnd\n";
        await File.WriteAllTextAsync(filePath, lossy);

        var result = await _service.FormatFileAsync(filePath, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FirstError.Should().Be("Tools.IniEditor.Format.RefusedDiscardedContent");
        (await File.ReadAllTextAsync(filePath)).Should().Be(lossy);
    }

    /// <summary>
    /// Verifies that a second top-level block parsed inside an unclosed block of
    /// the same type is reported as ambiguous discarded content, since the lost
    /// sibling boundary would bake in the wrong nesting on save.
    /// </summary>
    [Fact]
    public void ParseText_SameTypeNestedBlock_ReportsDiscardedContent()
    {
        var result = _service.ParseText("Object First\n  Health = 1.0\nObject Second\n  Health = 2.0\nEnd\nEnd\n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.HasParseErrors.Should().BeTrue();
        result.Data.HasDiscardedContent.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that an unclosed block holding a legitimate nested module keeps
    /// its repair available instead of being flagged as discarded content.
    /// </summary>
    [Fact]
    public void ParseText_UnclosedBlockWithNestedModule_KeepsRepairAllowed()
    {
        var result = _service.ParseText("Object Modular\n  WeaponSet\n    PRIMARY = SomeWeapon\n");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.HasParseErrors.Should().BeTrue();
        result.Data.HasDiscardedContent.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that formatting refuses a file whose missing End repair cannot
    /// preserve the original top-level nesting.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_AmbiguousNesting_RefusesAndPreservesFile()
    {
        var filePath = Path.Combine(_tempDirectory, "Ambiguous.ini");
        const string ambiguous = "Object First\n  Health = 1.0\nObject Second\n  Health = 2.0\nEnd\nEnd\n";
        await File.WriteAllTextAsync(filePath, ambiguous);

        var result = await _service.FormatFileAsync(filePath, CancellationToken.None);

        result.Success.Should().BeFalse();
        (await File.ReadAllTextAsync(filePath)).Should().Be(ambiguous);
    }

    /// <summary>
    /// Verifies that a map override RemoveModule directive keeps whitespace syntax
    /// even when the editor cleared its bare marker while editing the value.
    /// </summary>
    [Fact]
    public void WriteDocument_RemoveModuleWithoutBareFlag_PreservesWhitespaceSyntax()
    {
        var parsed = _service.ParseText("Object MapOverride\n  RemoveModule ModuleTag\nEnd\n");
        parsed.Success.Should().BeTrue();
        parsed.Data.Should().NotBeNull();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        var index = block.Fields.FindIndex(f => f.Key == "RemoveModule");
        index.Should().BeGreaterThanOrEqualTo(0);
        block.Fields[index] = block.Fields[index] with { Value = "OtherTag", IsBare = false };

        var written = _service.WriteDocument(parsed.Data);

        written.Should().Contain("RemoveModule OtherTag");
        written.Should().NotContain("RemoveModule =");
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

    /// <summary>
    /// Verifies that map.ini files with ReplaceModule, AddModule, and RemoveModule directives parse properly.
    /// </summary>
    [Fact]
    public void ParseText_MapIniWithModules_ParsesSuccessfully()
    {
        const string mapIniContent =
            "; Map overrides\n" +
            "Object ChinaInfantryAgent\n" +
            "  Side = China\n" +
            "  RemoveModule ModuleTag_01\n" +
            "  ReplaceModule ModuleTag_02\n" +
            "    Draw = W3DModelDraw ModuleTag_02_Override\n" +
            "      DefaultConditionState\n" +
            "        Model = NICFAG_SKN\n" +
            "      End\n" +
            "    End\n" +
            "  End\n" +
            "  AddModule ModuleTag_03\n" +
            "    Model = NICFAG_SKN2\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(mapIniContent);

        result.Success.Should().BeTrue();
        result.Data!.Blocks.Should().HaveCount(1);
        var block = result.Data.Blocks[0];
        block.Fields.Should().Contain(f => f.Key == "RemoveModule" && f.Value == "ModuleTag_01");
        block.Children.Should().ContainSingle(c => c.BlockType == "ReplaceModule");
        var replaceModule = block.Children.First(c => c.BlockType == "ReplaceModule");
        replaceModule.Children.Should().ContainSingle(c => c.BlockType == "Draw");
        var drawModule = replaceModule.Children[0];
        drawModule.Children.Should().ContainSingle(c => c.BlockType == "DefaultConditionState");
        block.Children.Should().ContainSingle(c => c.BlockType == "AddModule");
        var addModule = block.Children.First(c => c.BlockType == "AddModule");
        addModule.Name.Should().Be("ModuleTag_03");
        addModule.Fields.Should().Contain(f => f.Key == "Model" && f.Value == "NICFAG_SKN2");
    }

    /// <summary>
    /// Verifies that tab-separated block headers correctly populate BlockType and Name.
    /// </summary>
    [Fact]
    public void ParseText_TabSeparatedBlockHeader_ParsesBlockTypeAndNameCorrectly()
    {
        const string content =
            "Object\tChinaInfantryAgent\r\n" +
            "  Side\t=\tChina\r\n" +
            "  ReplaceModule\tModuleTag_02\r\n" +
            "    DefaultConditionState\r\n" +
            "    End\r\n" +
            "  End\r\n" +
            "End\r\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.Blocks.Should().ContainSingle();
        var block = result.Data.Blocks[0];
        block.BlockType.Should().Be("Object");
        block.Name.Should().Be("ChinaInfantryAgent");

        block.Children.Should().ContainSingle();
        var child = block.Children[0];
        child.BlockType.Should().Be("ReplaceModule");
        child.Name.Should().Be("ModuleTag_02");
    }

    /// <summary>
    /// Verifies that nested Animation blocks inside a TransitionState parse as
    /// nested blocks instead of cascading End mismatches.
    /// </summary>
    [Fact]
    public void ParseText_TransitionStateAnimationBlocks_ParseAsNestedBlocks()
    {
        const string content =
            "Object AmericaVehicleDozer\n" +
            "  Draw = W3DModelDraw ModuleTag_01\n" +
            "    DefaultConditionState\n" +
            "      Model = AVDOZER_SKN\n" +
            "    End\n" +
            "    TransitionState = TRANS_Opening TRANS_Opened\n" +
            "      Model = AVDOZER_SKN\n" +
            "      Animation = AVDOZER_BLD1\n" +
            "        AnimationName = AVDozer_BLD1.AVDozer_Bone\n" +
            "        AnimationMode = ONCE\n" +
            "      End\n" +
            "    End\n" +
            "    ConditionState = FIRING_A\n" +
            "      Model = AVDOZER_SKN\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var gameObject = result.Data.Blocks.Should().ContainSingle().Subject;
        var draw = gameObject.Children.Should().ContainSingle().Subject;
        draw.Children.Should().HaveCount(3);
        var transition = draw.Children[1];
        transition.DisplayHeader.Should().Be("TransitionState = TRANS_Opening TRANS_Opened");
        var animation = transition.Children.Should().ContainSingle().Subject;
        animation.DisplayHeader.Should().Be("Animation = AVDOZER_BLD1");
        animation.Fields.Select(field => field.Key).Should().Equal("AnimationName", "AnimationMode");
    }

    /// <summary>
    /// Verifies that nested Animation blocks inside an AnimationState parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_AnimationStateAnimationBlocks_ParseAsNestedBlocks()
    {
        const string content =
            "Object AmericaVehicleHumvee\n" +
            "  Draw = W3DTruckDraw ModuleTag_01\n" +
            "    AnimationState = FIRING_A\n" +
            "      Animation = Hmmvee_Turret\n" +
            "        AnimationName = AVHUMVEE_A.AVHUMVEE\n" +
            "        AnimationMode = LOOP\n" +
            "      End\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var state = result.Data.Blocks[0].Children[0].Children.Should().ContainSingle().Subject;
        state.DisplayHeader.Should().Be("AnimationState = FIRING_A");
        var animation = state.Children.Should().ContainSingle().Subject;
        animation.DisplayHeader.Should().Be("Animation = Hmmvee_Turret");
        animation.Fields.Select(field => field.Key).Should().Equal("AnimationName", "AnimationMode");
    }

    /// <summary>
    /// Verifies that field style Animation lines inside a ConditionState stay
    /// fields, matching shipped infantry draw modules.
    /// </summary>
    [Fact]
    public void ParseText_ConditionStateAnimationFields_StayFields()
    {
        const string content =
            "Object AmericanInfantryRanger\n" +
            "  Draw = W3DModelDraw ModuleTag_01\n" +
            "    ConditionState = FIRING_A\n" +
            "      Model = AVRNGR_SKN\n" +
            "      Animation = AVRNGR_AF1A AVRNGR_SKL\n" +
            "      Animation = AVRNGR_AF1B AVRNGR_SKL\n" +
            "      AnimationMode = ONCE\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var state = result.Data.Blocks[0].Children[0].Children.Should().ContainSingle().Subject;
        state.Children.Should().BeEmpty();
        state.Fields.Select(field => field.Key).Should().Equal("Model", "Animation", "Animation", "AnimationMode");
    }

    /// <summary>
    /// Verifies that a lone field style Animation line inside a TransitionState
    /// stays a field and round-trips without injecting an End.
    /// </summary>
    [Fact]
    public void ParseText_TransitionStateAnimationField_StayField()
    {
        const string content =
            "Object AmericaVehicleHumvee\n" +
            "  Draw = W3DModelDraw ModuleTag_01\n" +
            "    TransitionState = TRANS_Stand TRANS_StandInjured\n" +
            "      Animation = Anim\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var state = result.Data.Blocks[0].Children[0].Children.Should().ContainSingle().Subject;
        state.Children.Should().BeEmpty();
        state.Fields.Should().ContainSingle().Which.Key.Should().Be("Animation");
        var canonical = _service.WriteDocument(result.Data);
        canonical.Should().Contain("      Animation = Anim\r\n    End\r\n");
    }

    /// <summary>
    /// Verifies that the Die sub-block inside SlowDeathBehavior parses as a
    /// nested block instead of cascading End mismatches.
    /// </summary>
    [Fact]
    public void ParseText_SlowDeathDieBlock_ParsesAsNestedBlock()
    {
        const string content =
            "Object AmericanInfantryRanger\n" +
            "  Behavior = SlowDeathBehavior ModuleTag_Death\n" +
            "    DeathTypes = ALL\n" +
            "    Die\n" +
            "      Model = AVRNGR_D\n" +
            "    End\n" +
            "  End\n" +
            "  Draw = W3DModelDraw ModuleTag_01\n" +
            "    DefaultConditionState\n" +
            "      Model = AVRNGR_SKN\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var gameObject = result.Data.Blocks.Should().ContainSingle().Subject;
        gameObject.Children.Should().HaveCount(2);
        var behavior = gameObject.Children[0];
        var die = behavior.Children.Should().ContainSingle().Subject;
        die.BlockType.Should().Be("Die");
        die.Fields.Should().ContainSingle().Which.Key.Should().Be("Model");
    }

    /// <summary>
    /// Verifies that weapon damage nugget sub-blocks parse as nested blocks
    /// instead of cascading End mismatches.
    /// </summary>
    [Fact]
    public void ParseText_WeaponDamageNuggets_ParseAsNestedBlocks()
    {
        const string content =
            "Weapon RangerPistolWeapon\n" +
            "  DamageNugget\n" +
            "    Damage = 25.0\n" +
            "    Radius = 0.0\n" +
            "  End\n" +
            "  DOTNugget\n" +
            "    Damage = 5.0\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var weapon = result.Data.Blocks.Should().ContainSingle().Subject;
        weapon.Children.Select(child => child.BlockType).Should().Equal("DamageNugget", "DOTNugget");
    }

    /// <summary>
    /// Verifies that a UnitSpecificFX sub-block inside an object parses as a
    /// nested block instead of closing the object early.
    /// </summary>
    [Fact]
    public void ParseText_ObjectUnitSpecificFXBlock_ParsesAsNestedBlock()
    {
        const string content =
            "Object AmericaInfantryBiohazardTech\n" +
            "  Draw = W3DModelDraw ModuleTag_01\n" +
            "    DefaultConditionState\n" +
            "      Model = AITECH_SKN\n" +
            "    End\n" +
            "  End\n" +
            "  UnitSpecificFX\n" +
            "    CombatDropKillFX = FX_RangerCombatDropKill\n" +
            "  End\n" +
            "  Body = ActiveBody ModuleTag_02\n" +
            "    MaxHealth = 100.0\n" +
            "  End\n" +
            "  Behavior = CleanupHazardUpdate ModuleTag_03\n" +
            "    ScanRate = 1000\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var gameObject = result.Data.Blocks.Should().ContainSingle().Subject;
        gameObject.Children.Select(child => child.BlockType).Should().Equal("Draw", "UnitSpecificFX", "Body", "Behavior");
        gameObject.Children[1].Fields.Should().ContainSingle().Which.Key.Should().Be("CombatDropKillFX");
    }

    /// <summary>
    /// Verifies that object decal sub-blocks parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_ObjectDecalBlocks_ParseAsNestedBlocks()
    {
        const string content =
            "Object AmericaJetSpectreGunship\n" +
            "  AttackAreaDecal\n" +
            "    Texture = SCCSpecTarg\n" +
            "  End\n" +
            "  TargetingReticleDecal\n" +
            "    Texture = SCCSpecTarg\n" +
            "  End\n" +
            "  GridDecalTemplate\n" +
            "    Texture = EXGrid\n" +
            "  End\n" +
            "  DeliveryDecal\n" +
            "    Texture = SCCNuclearMissile_China\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var gameObject = result.Data.Blocks.Should().ContainSingle().Subject;
        gameObject.Children.Select(child => child.BlockType).Should().Equal(
            "AttackAreaDecal", "TargetingReticleDecal", "GridDecalTemplate", "DeliveryDecal");
    }

    /// <summary>
    /// Verifies that effect list sub-blocks parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_FXListSubBlocks_ParseAsNestedBlocks()
    {
        const string content =
            "FXList FX_GIDie\n" +
            "  Sound\n" +
            "    Name = CarMount\n" +
            "  End\n" +
            "  LightPulse\n" +
            "    Radius = 30.0\n" +
            "  End\n" +
            "  ViewShake\n" +
            "    Type = SEVERE\n" +
            "  End\n" +
            "  TerrainScorch\n" +
            "    Radius = 15.0\n" +
            "  End\n" +
            "  Tracer\n" +
            "    Length = 20.0\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var list = result.Data.Blocks.Should().ContainSingle().Subject;
        list.Children.Select(child => child.BlockType).Should().Equal(
            "Sound", "LightPulse", "ViewShake", "TerrainScorch", "Tracer");
    }

    /// <summary>
    /// Verifies that bare emitter lines inside effect lists parse as references, not blocks.
    /// </summary>
    [Fact]
    public void ParseText_FXListParticleSystemReferences_ParseAsFields()
    {
        const string content =
            "FXList FX_GenericTankMuzzle\n" +
            "  ParticleSystem GenericTankMuzzleSmoke\n" +
            "  ParticleSystem GenericTankMuzzleFlash\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var list = result.Data.Blocks.Should().ContainSingle().Subject;
        list.Children.Should().BeEmpty();
        list.Fields.Select(field => field.Value).Should().Equal(
            "GenericTankMuzzleSmoke", "GenericTankMuzzleFlash");
    }

    /// <summary>
    /// Verifies that a particle system definition with fields still opens a nested block.
    /// </summary>
    [Fact]
    public void ParseText_ParticleSystemDefinition_OpensBlock()
    {
        const string content =
            "ParticleSystem GenericTankMuzzleSmoke\n" +
            "  MaxSize = 12.0\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var definition = result.Data.Blocks.Should().ContainSingle().Subject;
        definition.BlockType.Should().Be("ParticleSystem");
        definition.Fields.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that mission sub-blocks inside campaigns parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_CampaignMissionBlocks_ParseAsNestedBlocks()
    {
        const string content =
            "Campaign TRAINING\n" +
            "  FirstMission = Mission01\n" +
            "  Mission Mission01\n" +
            "    Map = Maps\\Training01\\Training01.map\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var campaign = result.Data.Blocks.Should().ContainSingle().Subject;
        var mission = campaign.Children.Should().ContainSingle().Subject;
        mission.BlockType.Should().Be("Mission");
        mission.Name.Should().Be("Mission01");
    }

    /// <summary>
    /// Verifies that numbered challenge persona blocks parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_ChallengePersonaBlocks_ParseAsNestedBlocks()
    {
        const string content =
            "ChallengeGenerals\n" +
            "  GeneralPersona0\n" +
            "    PlayerTemplate = FactionAmericaAirForceGeneral\n" +
            "  End\n" +
            "  GeneralPersona11\n" +
            "    PlayerTemplate = FactionBossGeneral\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var roster = result.Data.Blocks.Should().ContainSingle().Subject;
        roster.Children.Select(child => child.BlockType).Should().Equal("GeneralPersona0", "GeneralPersona11");
    }

    /// <summary>
    /// Verifies that interface radius cursor blocks parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_InGameUICursorBlocks_ParseAsNestedBlocks()
    {
        const string content =
            "InGameUI\n" +
            "  MaxSelectionSize = 0\n" +
            "  SpyDroneRadiusCursor\n" +
            "    Texture = SccSpyDrone_USA\n" +
            "  End\n" +
            "  ArtilleryRadiusCursor\n" +
            "    Texture = SCCArtilleryBarrage_China\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var ui = result.Data.Blocks.Should().ContainSingle().Subject;
        ui.Children.Select(child => child.BlockType).Should().Equal("SpyDroneRadiusCursor", "ArtilleryRadiusCursor");
    }

    /// <summary>
    /// Verifies that AI data sub-blocks parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_AIDataSubBlocks_ParseAsNestedBlocks()
    {
        const string content =
            "AIData\n" +
            "  SideInfo America\n" +
            "    SkillSet1\n" +
            "      Science = SCIENCE_PaladinTank\n" +
            "    End\n" +
            "    SkillSet2\n" +
            "      Science = SCIENCE_Pathfinder\n" +
            "    End\n" +
            "    SkillSet5\n" +
            "      Science = SCIENCE_Overlord\n" +
            "    End\n" +
            "  End\n" +
            "  SkirmishBuildList America\n" +
            "    Structure AmericaCommandCenter\n" +
            "      Rebuilds = 0\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var data = result.Data.Blocks.Should().ContainSingle().Subject;
        data.Children.Select(child => child.BlockType).Should().Equal("SideInfo", "SkirmishBuildList");
        data.Children[0].Children.Select(child => child.BlockType).Should().Equal("SkillSet1", "SkillSet2", "SkillSet5");
        data.Children[1].Children.Should().ContainSingle().Subject.BlockType.Should().Be("Structure");
    }

    /// <summary>
    /// Verifies that side sounds inside EVA events parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_EvaSideSounds_ParseAsNestedBlocks()
    {
        const string content =
            "EvaEvent LowPower\n" +
            "  Priority = 2\n" +
            "  SideSounds\n" +
            "    Side = America\n" +
            "    Sounds = EvaUSA_LowPower\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var evt = result.Data.Blocks.Should().ContainSingle().Subject;
        var sounds = evt.Children.Should().ContainSingle().Subject;
        sounds.BlockType.Should().Be("SideSounds");
        sounds.Fields.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that window sub-blocks inside transitions parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_WindowTransitionBlocks_ParseAsNestedBlocks()
    {
        const string content =
            "WindowTransition MainMenuFade\n" +
            "  Window\n" +
            "    WinName = MainMenu.wnd:MainMenuRuler\n" +
            "    FrameDelay = 0\n" +
            "  End\n" +
            "  FireOnce = YES\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var transition = result.Data.Blocks.Should().ContainSingle().Subject;
        transition.Children.Should().ContainSingle().Subject.BlockType.Should().Be("Window");
    }

    /// <summary>
    /// Verifies that attack sub-blocks inside creation lists parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_ObjectCreationListAttack_ParseAsNestedBlocks()
    {
        const string content =
            "ObjectCreationList OCL_Test\n" +
            "  Attack\n" +
            "    WeaponSlot = PRIMARY\n" +
            "    DeliveryDecal\n" +
            "      Texture = SCCNuclearMissile_China\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var list = result.Data.Blocks.Should().ContainSingle().Subject;
        var attack = list.Children.Should().ContainSingle().Subject;
        attack.BlockType.Should().Be("Attack");
        attack.Children.Should().ContainSingle().Subject.BlockType.Should().Be("DeliveryDecal");
    }

    /// <summary>
    /// Verifies that image parts inside command bar schemes parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_ControlBarImagePart_ParseAsNestedBlock()
    {
        const string content =
            "ControlBarScheme America8x6\n" +
            "  Side = America\n" +
            "  ImagePart\n" +
            "    Position = X:0 Y:408\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var scheme = result.Data.Blocks.Should().ContainSingle().Subject;
        scheme.Children.Should().ContainSingle().Subject.BlockType.Should().Be("ImagePart");
    }

    /// <summary>
    /// Verifies that inheritable module blocks parse as nested blocks.
    /// </summary>
    [Fact]
    public void ParseText_InheritableModuleBlocks_ParseAsNestedBlocks()
    {
        const string content =
            "Object TestObject\n" +
            "  InheritableModule\n" +
            "    Behavior = AutoHealBehavior ModuleTag_Heal\n" +
            "      HealingAmount = 10\n" +
            "    End\n" +
            "  End\n" +
            "  OverrideableByLikeKind\n" +
            "    Behavior = StealthUpdate ModuleTag_Stealth\n" +
            "      StealthDelay = 2000\n" +
            "    End\n" +
            "  End\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var gameObject = result.Data.Blocks.Should().ContainSingle().Subject;
        gameObject.Children.Select(child => child.BlockType).Should().Equal("InheritableModule", "OverrideableByLikeKind");
    }

    /// <summary>
    /// Verifies that parameterized pattern guards reject prefix-only and
    /// suffix-only tokens instead of opening phantom blocks.
    /// </summary>
    [Fact]
    public void ParseText_ParameterizedPatternGuards_StayFields()
    {
        const string content =
            "ChallengeGenerals\n" +
            "  GeneralPersona\n" +
            "  RadiusCursor\n" +
            "End\n";

        var result = _service.ParseText(content);

        result.Success.Should().BeTrue();
        result.Data!.ParseErrors.Should().BeEmpty();
        var roster = result.Data.Blocks.Should().ContainSingle().Subject;
        roster.Children.Should().BeEmpty();
        roster.Fields.Should().HaveCount(2);
    }
}
