using FluentAssertions;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Content.Services.CommunityOutpost;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Unit tests for <see cref="BigFilePacker"/> byte-for-byte reproducibility.
/// </summary>
public sealed class BigFilePackerTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="BigFilePackerTests"/> class.
    /// </summary>
    public BigFilePackerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_BigPackerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    /// <summary>
    /// Verifies that packing an archive with a non-alphabetically ordered manifest preserves entry order.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PackAsync_WithNonAlphabeticalManifest_PreservesEntryOrderExactly()
    {
        // Arrange: release archives are not alphabetically ordered (for example the
        // community patch lists CommandButton subfolders before CommandButton.ini).
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(Path.Combine(sourceDir, "Data", "INI"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "INI", "a.ini"), "aaa");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "INI", "b.ini"), "b");

        var manifest = new BigArchiveManifest
        {
            BigFileName = "order.big",
            TrailerHex = "0000000000000000",
            EntryOrder = new List<string> { @"Data\INI\b.ini", @"Data\INI\a.ini" },
        };
        var target = Path.Combine(_tempDirectory, "order.big");

        // Act
        await BigFilePacker.PackAsync(sourceDir, target, null, manifest);

        // Assert
        ReadEntryNames(target).Should().Equal(@"Data\INI\b.ini", @"Data\INI\a.ini");
    }

    /// <summary>
    /// Verifies that packing an archive with a subset of manifest entries ignores the header size override.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PackAsync_WithSubsetOfManifestEntries_IgnoresHeaderSizeOverride()
    {
        // Arrange: header quirks belong to the full release archive only.
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(Path.Combine(sourceDir, "Data"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "a.ini"), "aaa");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "b.ini"), "b");

        var manifest = new BigArchiveManifest
        {
            BigFileName = "subset.big",
            TrailerHex = "0000000000000000",
            HeaderSizeOverride = 9999,
            EntryOrder = new List<string> { @"Data\a.ini", @"Data\b.ini", @"Data\c.ini" },
        };
        var target = Path.Combine(_tempDirectory, "subset.big");

        // Act
        await BigFilePacker.PackAsync(sourceDir, target, null, manifest);

        // Assert
        var (headerSize, firstOffset) = ReadHeaderSizes(target);
        headerSize.Should().Be(firstOffset, "subset archives must report their real header size");
        headerSize.Should().NotBe(9999);
    }

    /// <summary>
    /// Verifies that packing an archive with the full manifest set applies the header size override.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PackAsync_WithFullManifestSet_AppliesHeaderSizeOverride()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(Path.Combine(sourceDir, "Data"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "a.ini"), "aaa");

        var manifest = new BigArchiveManifest
        {
            BigFileName = "full.big",
            TrailerHex = "0000000000000000",
            HeaderSizeOverride = 1234,
            EntryOrder = new List<string> { @"Data\a.ini" },
        };
        var target = Path.Combine(_tempDirectory, "full.big");

        // Act
        await BigFilePacker.PackAsync(sourceDir, target, null, manifest);

        // Assert
        var (headerSize, _) = ReadHeaderSizes(target);
        headerSize.Should().Be(1234);
    }

    /// <summary>
    /// Verifies that packing the same files twice produces identical byte output.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PackAsync_WhenPackedTwice_ProducesIdenticalBytes()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(Path.Combine(sourceDir, "Data", "INI", "Object"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "INI", "Weapon.ini"), "weapon");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "INI", "AIData.ini"), "ai");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "INI", "Object", "Tank.ini"), "tank");

        var manifest = new BigArchiveManifest
        {
            BigFileName = "stable.big",
            TrailerHex = "0000000000000000",
            EntryOrder = new List<string> { @"Data\INI\Weapon.ini", @"Data\INI\AIData.ini", @"Data\INI\Object\Tank.ini" },
        };

        // Act
        var first = Path.Combine(_tempDirectory, "first.big");
        var second = Path.Combine(_tempDirectory, "second.big");
        await BigFilePacker.PackAsync(sourceDir, first, null, manifest);
        await BigFilePacker.PackAsync(sourceDir, second, null, manifest);

        // Assert
        (await File.ReadAllBytesAsync(first)).Should().Equal(await File.ReadAllBytesAsync(second));
    }

    private static IReadOnlyList<string> ReadEntryNames(string bigPath)
    {
        using var stream = File.OpenRead(bigPath);
        using var reader = new BinaryReader(stream);
        reader.ReadBytes(4);
        reader.ReadUInt32();
        var count = ReadBigEndianUInt32(reader);
        reader.ReadBytes(4);

        var names = new List<string>();
        for (var i = 0; i < count; i++)
        {
            ReadBigEndianUInt32(reader);
            ReadBigEndianUInt32(reader);
            var nameBytes = new List<byte>();
            byte b;
            while ((b = reader.ReadByte()) != 0)
            {
                nameBytes.Add(b);
            }

            names.Add(System.Text.Encoding.ASCII.GetString(nameBytes.ToArray()));
        }

        return names;
    }

    private static (uint HeaderSize, uint FirstOffset) ReadHeaderSizes(string bigPath)
    {
        using var stream = File.OpenRead(bigPath);
        using var reader = new BinaryReader(stream);
        reader.ReadBytes(4);
        reader.ReadUInt32();
        reader.ReadBytes(4);
        var headerSize = ReadBigEndianUInt32(reader);
        var firstOffset = ReadBigEndianUInt32(reader);
        return (headerSize, firstOffset);
    }

    private static uint ReadBigEndianUInt32(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToUInt32(bytes, 0);
    }
}
