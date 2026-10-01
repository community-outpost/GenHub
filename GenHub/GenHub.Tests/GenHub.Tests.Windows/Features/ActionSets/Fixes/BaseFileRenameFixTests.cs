using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Fixes;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Windows.Features.ActionSets.Fixes;

/// <summary>
/// Verifies that <see cref="BaseFileRenameFix"/> implementations never lose a pre-existing backup and that
/// apply followed by undo restores the original directory contents exactly.
/// </summary>
public sealed class BaseFileRenameFixTests : IDisposable
{
    private const string UserBackupSuffix = ".bak";
    private const string OriginalContent = "original dll bytes";
    private const string DifferentContent = "an older dll the user kept";

    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseFileRenameFixTests"/> class.
    /// </summary>
    public BaseFileRenameFixTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"GenHub_FileRenameFixTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Gets the identifiers of the rename fixes under test.
    /// </summary>
    public static TheoryData<string> FixIds => new() { nameof(BrowserEngineFix), nameof(DbgHelpFix) };

    /// <summary>
    /// Gets the pre-apply states as (fix, has original, user backup content or null).
    /// </summary>
    /// <remarks>
    /// A <c>.bak</c> with no target is the state earlier builds left after apply, so undo restores it. The legacy undo tests cover it.
    /// </remarks>
    public static TheoryData<string, bool, string?> InitialStates
    {
        get
        {
            var data = new TheoryData<string, bool, string?>();
            foreach (var fixId in new[] { nameof(BrowserEngineFix), nameof(DbgHelpFix) })
            {
                data.Add(fixId, false, null);
                data.Add(fixId, true, null);
                data.Add(fixId, true, OriginalContent);
                data.Add(fixId, true, DifferentContent);
            }

            return data;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup.
        }
    }

    /// <summary>
    /// Verifies apply disables the target, keeps any user backup untouched, and undo restores every file byte for byte.
    /// </summary>
    /// <param name="fixId">The fix under test.</param>
    /// <param name="hasOriginal">Whether the target file exists before apply.</param>
    /// <param name="userBackupContent">The content of a pre-existing user backup, or null when there is none.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(InitialStates))]
    public async Task ApplyThenUndo_RestoresOriginalStateExactlyAsync(string fixId, bool hasOriginal, string? userBackupContent)
    {
        var (fix, target, _) = CreateFix(fixId);
        var installation = CreateInstallation(out var generalsDir, out var zeroHourDir);
        foreach (var dir in new[] { generalsDir, zeroHourDir })
        {
            Seed(dir, target, hasOriginal, userBackupContent);
        }

        var before = Snapshot();

        var applyResult = await fix.ApplyAsync(installation);

        applyResult.Success.Should().BeTrue(string.Join(Environment.NewLine, applyResult.Details));
        (await fix.IsAppliedAsync(installation)).Should().BeTrue();
        foreach (var dir in new[] { generalsDir, zeroHourDir })
        {
            File.Exists(Path.Combine(dir, target)).Should().BeFalse();
            AssertUserBackupUnchanged(dir, target, userBackupContent);
        }

        var undoResult = await fix.UndoAsync(installation);

        undoResult.Success.Should().BeTrue(string.Join(Environment.NewLine, undoResult.Details));
        Snapshot().Should().BeEquivalentTo(before);
    }

    /// <summary>
    /// Verifies undo is idempotent: a second undo succeeds and changes nothing.
    /// </summary>
    /// <param name="fixId">The fix under test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(FixIds))]
    public async Task Undo_RunTwice_IsSafeAndLeavesOriginalStateAsync(string fixId)
    {
        var (fix, target, _) = CreateFix(fixId);
        var installation = CreateInstallation(out var generalsDir, out var zeroHourDir);
        Seed(generalsDir, target, hasOriginal: true, DifferentContent);
        Seed(zeroHourDir, target, hasOriginal: true, userBackupContent: null);
        var before = Snapshot();

        (await fix.ApplyAsync(installation)).Success.Should().BeTrue();
        (await fix.UndoAsync(installation)).Success.Should().BeTrue();
        var second = await fix.UndoAsync(installation);

        second.Success.Should().BeTrue(string.Join(Environment.NewLine, second.Details));
        Snapshot().Should().BeEquivalentTo(before);
    }

    /// <summary>
    /// Verifies a combined Generals and Zero Hour directory is processed once and restored exactly.
    /// </summary>
    /// <param name="fixId">The fix under test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(FixIds))]
    public async Task ApplyThenUndo_CombinedDirectory_RestoresOriginalStateAsync(string fixId)
    {
        var (fix, target, _) = CreateFix(fixId);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "Combined")).FullName;
        var installation = new GameInstallation(dir, GameInstallationType.Retail)
        {
            HasGenerals = true,
            GeneralsPath = dir,
            HasZeroHour = true,
            ZeroHourPath = dir + Path.DirectorySeparatorChar,
        };
        Seed(dir, target, hasOriginal: true, DifferentContent);
        var before = Snapshot();

        var apply = await fix.ApplyAsync(installation);
        var undo = await fix.UndoAsync(installation);

        apply.Success.Should().BeTrue(string.Join(Environment.NewLine, apply.Details));
        undo.Success.Should().BeTrue(string.Join(Environment.NewLine, undo.Details));
        apply.Details.Count(d => d.Contains(dir, StringComparison.Ordinal)).Should().Be(1);
        undo.Details.Count(d => d.Contains(dir, StringComparison.Ordinal)).Should().Be(1);
        Snapshot().Should().BeEquivalentTo(before);
    }

    /// <summary>
    /// Verifies undo restores a target that an earlier build renamed to the legacy <c>.bak</c> name, byte for byte.
    /// </summary>
    /// <param name="fixId">The fix under test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(FixIds))]
    public async Task Undo_WhenOnlyLegacyBackupExists_RestoresTargetAsync(string fixId)
    {
        var (fix, target, _) = CreateFix(fixId);
        var installation = CreateInstallation(out var generalsDir, out var zeroHourDir);
        foreach (var dir in new[] { generalsDir, zeroHourDir })
        {
            Seed(dir, target, hasOriginal: false, OriginalContent);
        }

        var legacyHash = HashFile(Path.Combine(generalsDir, target + UserBackupSuffix));
        (await fix.IsAppliedAsync(installation)).Should().BeTrue();

        var undo = await fix.UndoAsync(installation);
        var second = await fix.UndoAsync(installation);

        undo.Success.Should().BeTrue(string.Join(Environment.NewLine, undo.Details));
        second.Success.Should().BeTrue(string.Join(Environment.NewLine, second.Details));
        foreach (var dir in new[] { generalsDir, zeroHourDir })
        {
            HashFile(Path.Combine(dir, target)).Should().Be(legacyHash);
            File.Exists(Path.Combine(dir, target + UserBackupSuffix)).Should().BeFalse();
        }

        (await fix.IsAppliedAsync(installation)).Should().BeFalse();
    }

    /// <summary>
    /// Verifies undo never touches a legacy <c>.bak</c> while the target file is present.
    /// </summary>
    /// <param name="fixId">The fix under test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(FixIds))]
    public async Task Undo_WhenTargetAndLegacyBackupExist_LeavesBothUntouchedAsync(string fixId)
    {
        var (fix, target, _) = CreateFix(fixId);
        var installation = CreateInstallation(out var generalsDir, out var zeroHourDir);
        foreach (var dir in new[] { generalsDir, zeroHourDir })
        {
            Seed(dir, target, hasOriginal: true, DifferentContent);
        }

        var before = Snapshot();

        var undo = await fix.UndoAsync(installation);

        undo.Success.Should().BeTrue(string.Join(Environment.NewLine, undo.Details));
        Snapshot().Should().BeEquivalentTo(before);
    }

    /// <summary>
    /// Verifies apply never overwrites an existing GenHub backup whose content differs from the target.
    /// </summary>
    /// <param name="fixId">The fix under test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(FixIds))]
    public async Task Apply_WhenDifferentGenHubBackupExists_KeepsBothFilesAndFailsAsync(string fixId)
    {
        var (fix, target, genHubBackup) = CreateFix(fixId);
        var installation = CreateInstallation(out var generalsDir, out var zeroHourDir);
        foreach (var dir in new[] { generalsDir, zeroHourDir })
        {
            File.WriteAllText(Path.Combine(dir, target), OriginalContent);
            File.WriteAllText(Path.Combine(dir, genHubBackup), DifferentContent);
        }

        var before = Snapshot();

        var result = await fix.ApplyAsync(installation);

        result.Success.Should().BeFalse();
        result.Details.Should().Contain(d => d.Contains(genHubBackup, StringComparison.Ordinal));
        Snapshot().Should().BeEquivalentTo(before);
    }

    /// <summary>
    /// Verifies apply disables the target when an identical GenHub backup already holds its bytes, and undo brings it back.
    /// </summary>
    /// <param name="fixId">The fix under test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(FixIds))]
    public async Task Apply_WhenIdenticalGenHubBackupExists_DisablesTargetAndUndoRestoresItAsync(string fixId)
    {
        var (fix, target, genHubBackup) = CreateFix(fixId);
        var installation = CreateInstallation(out var generalsDir, out _);
        installation.HasZeroHour = false;
        File.WriteAllText(Path.Combine(generalsDir, target), OriginalContent);
        File.WriteAllText(Path.Combine(generalsDir, genHubBackup), OriginalContent);
        var originalHash = HashFile(Path.Combine(generalsDir, target));

        var apply = await fix.ApplyAsync(installation);

        apply.Success.Should().BeTrue(string.Join(Environment.NewLine, apply.Details));
        File.Exists(Path.Combine(generalsDir, target)).Should().BeFalse();
        HashFile(Path.Combine(generalsDir, genHubBackup)).Should().Be(originalHash);

        var undo = await fix.UndoAsync(installation);

        undo.Success.Should().BeTrue(string.Join(Environment.NewLine, undo.Details));
        HashFile(Path.Combine(generalsDir, target)).Should().Be(originalHash);
    }

    /// <summary>
    /// Verifies undo keeps both files when the target was replaced after apply with different content.
    /// </summary>
    /// <param name="fixId">The fix under test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(FixIds))]
    public async Task Undo_WhenTargetReappearedWithDifferentContent_KeepsBothFilesAndFailsAsync(string fixId)
    {
        var (fix, target, _) = CreateFix(fixId);
        var installation = CreateInstallation(out var generalsDir, out _);
        installation.HasZeroHour = false;
        File.WriteAllText(Path.Combine(generalsDir, target), OriginalContent);
        (await fix.ApplyAsync(installation)).Success.Should().BeTrue();
        File.WriteAllText(Path.Combine(generalsDir, target), DifferentContent);
        var before = Snapshot();

        var undo = await fix.UndoAsync(installation);

        undo.Success.Should().BeFalse();
        Snapshot().Should().BeEquivalentTo(before);
    }

    /// <summary>
    /// Verifies undo discards only a redundant GenHub backup when the target reappeared with identical content.
    /// </summary>
    /// <param name="fixId">The fix under test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(FixIds))]
    public async Task Undo_WhenTargetReappearedWithIdenticalContent_RemovesRedundantBackupAsync(string fixId)
    {
        var (fix, target, genHubBackup) = CreateFix(fixId);
        var installation = CreateInstallation(out var generalsDir, out _);
        installation.HasZeroHour = false;
        Seed(generalsDir, target, hasOriginal: true, DifferentContent);
        var before = Snapshot();
        (await fix.ApplyAsync(installation)).Success.Should().BeTrue();
        File.Copy(Path.Combine(generalsDir, genHubBackup), Path.Combine(generalsDir, target));

        var undo = await fix.UndoAsync(installation);

        undo.Success.Should().BeTrue(string.Join(Environment.NewLine, undo.Details));
        Snapshot().Should().BeEquivalentTo(before);
    }

    private static (BaseFileRenameFix Fix, string Target, string GenHubBackup) CreateFix(string fixId) => fixId switch
    {
        nameof(BrowserEngineFix) => (
            new BrowserEngineFix(NullLogger<BrowserEngineFix>.Instance),
            GameClientConstants.BrowserEngineDll,
            GameClientConstants.BrowserEngineDllBak),
        nameof(DbgHelpFix) => (
            new DbgHelpFix(NullLogger<DbgHelpFix>.Instance),
            GameClientConstants.DbgHelpDll,
            GameClientConstants.DbgHelpDllBak),
        _ => throw new ArgumentOutOfRangeException(nameof(fixId), fixId, null),
    };

    private static void Seed(string dir, string target, bool hasOriginal, string? userBackupContent)
    {
        if (hasOriginal)
        {
            File.WriteAllText(Path.Combine(dir, target), OriginalContent);
        }

        if (userBackupContent != null)
        {
            File.WriteAllText(Path.Combine(dir, target + UserBackupSuffix), userBackupContent);
        }
    }

    private static void AssertUserBackupUnchanged(string dir, string target, string? userBackupContent)
    {
        if (userBackupContent != null)
        {
            File.ReadAllText(Path.Combine(dir, target + UserBackupSuffix)).Should().Be(userBackupContent);
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private GameInstallation CreateInstallation(out string generalsDir, out string zeroHourDir)
    {
        generalsDir = Directory.CreateDirectory(Path.Combine(_root, "Generals")).FullName;
        zeroHourDir = Directory.CreateDirectory(Path.Combine(_root, "ZeroHour")).FullName;
        return new GameInstallation(_root, GameInstallationType.Retail)
        {
            HasGenerals = true,
            GeneralsPath = generalsDir,
            HasZeroHour = true,
            ZeroHourPath = zeroHourDir,
        };
    }

    private Dictionary<string, string> Snapshot() => Directory
        .EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), HashFile, StringComparer.Ordinal);
}
