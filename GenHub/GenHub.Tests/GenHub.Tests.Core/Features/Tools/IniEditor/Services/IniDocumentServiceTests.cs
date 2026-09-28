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
            "    Behavior = AutoHealBehavior ModuleTag_03\n" +
            "      HealingAmount = 2\n" +
            "    End\n" +
            "  End\n" +
            "End\n" +
            "\n" +
            "Object AmericaVehicleHumvee\n" +
            "  BuildCost = 700\n" +
            "End\n";

        var result = _service.ParseText(mapIniContent);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Blocks.Should().HaveCount(2);

        var chinaInfantry = result.Data.Blocks[0];
        chinaInfantry.Name.Should().Be("ChinaInfantryAgent");
        chinaInfantry.Fields.Should().Contain(f => f.Key == "RemoveModule" && f.Value == "ModuleTag_01" && f.IsBare);

        chinaInfantry.Children.Should().HaveCount(2);
        var replaceMod = chinaInfantry.Children[0];
        replaceMod.BlockType.Should().Be("ReplaceModule");
        replaceMod.Name.Should().Be("ModuleTag_02");

        var draw = replaceMod.Children[0];
        draw.BlockType.Should().Be("Draw");

        var defaultCondition = draw.Children[0];
        defaultCondition.BlockType.Should().Be("DefaultConditionState");
        defaultCondition.Fields.Should().Contain(f => f.Key == "Model" && f.Value == "NICFAG_SKN");

        var addMod = chinaInfantry.Children[1];
        addMod.BlockType.Should().Be("AddModule");
        addMod.Name.Should().Be("ModuleTag_03");

        var humvee = result.Data.Blocks[1];
        humvee.Name.Should().Be("AmericaVehicleHumvee");
        humvee.Fields.Should().Contain(f => f.Key == "BuildCost" && f.Value == "700");

        var canonical = _service.WriteDocument(result.Data);
        var reparsed = _service.ParseText(canonical);
        reparsed.Success.Should().BeTrue();
        reparsed.Data!.Blocks.Should().HaveCount(2);
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
            "End\r\n\r\n");
    }
}
