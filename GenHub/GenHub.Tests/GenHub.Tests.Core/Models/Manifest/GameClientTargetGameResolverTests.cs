using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Unit tests for <see cref="GameClientTargetGameResolver"/>.
/// </summary>
public sealed class GameClientTargetGameResolverTests : IDisposable
{
    private readonly string _payload = Path.Combine(Path.GetTempPath(), "GenHub_TargetGame_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientTargetGameResolverTests"/> class.
    /// </summary>
    public GameClientTargetGameResolverTests()
    {
        Directory.CreateDirectory(_payload);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_payload))
        {
            Directory.Delete(_payload, recursive: true);
        }
    }

    /// <summary>
    /// An entry binary with Zero Hour markers resolves Zero Hour.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveFromEntryBinaryAsync_ZeroHourMarkers_ReturnsZeroHourAsync()
    {
        WriteEntry("client", withMarkers: true);
        var manifest = new ContentManifest
        {
            Id = "1.0.test.targetgame.zh",
            ContentType = ContentType.GameClient,
            EntryPoint = "client",
        };

        var result = await GameClientTargetGameResolver.ResolveFromEntryBinaryAsync(manifest, _payload, CancellationToken.None);

        Assert.Equal(GameType.ZeroHour, result);
    }

    /// <summary>
    /// An entry binary with the Generals token resolves Generals.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveFromEntryBinaryAsync_GeneralsToken_ReturnsGeneralsAsync()
    {
        WriteEntry("client", withMarkers: false, withGeneralsToken: true);
        var manifest = new ContentManifest
        {
            Id = "1.0.test.targetgame.generals",
            ContentType = ContentType.GameClient,
            EntryPoint = "client",
        };

        var result = await GameClientTargetGameResolver.ResolveFromEntryBinaryAsync(manifest, _payload, CancellationToken.None);

        Assert.Equal(GameType.Generals, result);
    }

    /// <summary>
    /// A marker-less entry binary yields no override.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveFromEntryBinaryAsync_WithoutMarkers_ReturnsNullAsync()
    {
        WriteEntry("client", withMarkers: false);
        var manifest = new ContentManifest
        {
            Id = "1.0.test.targetgame.plain",
            ContentType = ContentType.GameClient,
            EntryPoint = "client",
        };

        var result = await GameClientTargetGameResolver.ResolveFromEntryBinaryAsync(manifest, _payload, CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>
    /// Non-clients and missing entries yield no override.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveFromEntryBinaryAsync_NonClientOrMissing_ReturnsNullAsync()
    {
        var mod = new ContentManifest
        {
            Id = "1.0.test.targetgame.mod",
            ContentType = ContentType.Mod,
            EntryPoint = "client",
        };
        var missing = new ContentManifest
        {
            Id = "1.0.test.targetgame.missing",
            ContentType = ContentType.GameClient,
            EntryPoint = "absent",
        };

        Assert.Null(await GameClientTargetGameResolver.ResolveFromEntryBinaryAsync(mod, _payload, CancellationToken.None));
        Assert.Null(await GameClientTargetGameResolver.ResolveFromEntryBinaryAsync(missing, _payload, CancellationToken.None));
    }

    private void WriteEntry(string name, bool withMarkers, bool withGeneralsToken = false)
    {
        var header = new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 0x02, 0x01, 0x01, 0x00 };
        using var stream = File.Create(Path.Combine(_payload, name));
        stream.Write(header, 0, header.Length);
        if (withMarkers)
        {
            var title = Encoding.ASCII.GetBytes(GameBinaryConstants.ZeroHourTitle);
            var menu = Encoding.ASCII.GetBytes(" " + GameBinaryConstants.ChallengeMenuMarker);
            stream.Write(title, 0, title.Length);
            stream.Write(menu, 0, menu.Length);
        }

        if (withGeneralsToken)
        {
            var token = Encoding.ASCII.GetBytes(" " + GameBinaryConstants.GeneralsEngineMarker);
            stream.Write(token, 0, token.Length);
        }
    }
}
