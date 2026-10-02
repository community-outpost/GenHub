using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Features.GameSettings;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameSettings;

/// <summary>
/// Tests for <see cref="LanNicknameService"/>.
/// </summary>
public class LanNicknameServiceTests : IDisposable
{
    private readonly string _dataDirectory;
    private readonly Mock<IGamePathProvider> _pathProviderMock = new();
    private readonly LanNicknameService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanNicknameServiceTests"/> class.
    /// </summary>
    public LanNicknameServiceTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), $"genhub-nickname-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDirectory);
        _pathProviderMock.Setup(p => p.GetOptionsDirectory(It.IsAny<GameType>())).Returns(_dataDirectory);
        _service = new LanNicknameService(Mock.Of<ILogger<LanNicknameService>>(), _pathProviderMock.Object);
    }

    /// <summary>
    /// Should resolve Network.ini beside Options.ini.
    /// </summary>
    [Fact]
    public void GetNetworkFilePath_ShouldResolveNetworkIniInOptionsDirectory()
    {
        var path = _service.GetNetworkFilePath(GameType.ZeroHour);

        Assert.Equal(Path.Combine(_dataDirectory, "Network.ini"), path);
    }

    /// <summary>
    /// A missing file means no stored nickname, not an error.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadNicknameAsync_WhenFileMissing_ShouldReturnEmptyAsync()
    {
        var result = await _service.LoadNicknameAsync(GameType.ZeroHour);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Data);
    }

    /// <summary>
    /// A blank nickname must fail without creating the file.
    /// </summary>
    /// <param name="nickname">The blank nickname.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveNicknameAsync_WithBlankNickname_ShouldFailWithoutTouchingFileAsync(string nickname)
    {
        var result = await _service.SaveNicknameAsync(GameType.ZeroHour, nickname);

        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorNicknameEmpty, result.Errors);
        Assert.False(File.Exists(Path.Combine(_dataDirectory, GameSettingsConstants.Network.FileName)));
    }

    /// <summary>
    /// Saved nicknames round-trip through the game's quoted-printable encoding.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveNicknameAsync_ThenLoad_ShouldRoundTripAsync()
    {
        var save = await _service.SaveNicknameAsync(GameType.ZeroHour, "Commander 寿");

        Assert.True(save.Success);
        var load = await _service.LoadNicknameAsync(GameType.ZeroHour);
        Assert.True(load.Success);
        Assert.Equal("Commander 寿", load.Data);
        var raw = await File.ReadAllTextAsync(Path.Combine(_dataDirectory, GameSettingsConstants.Network.FileName));
        Assert.Contains(LanNicknameCodec.Encode("Commander 寿"), raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// Existing keys such as color and map preferences must survive a nickname save.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveNicknameAsync_WithExistingFile_ShouldPreserveOtherKeysAsync()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, GameSettingsConstants.Network.FileName),
            "Color = 3\nUserName = bw_00x_00\nMap = _5F_00map_00\n");

        var save = await _service.SaveNicknameAsync(GameType.ZeroHour, "Ace");

        Assert.True(save.Success);
        var content = await File.ReadAllTextAsync(Path.Combine(_dataDirectory, GameSettingsConstants.Network.FileName));
        Assert.Contains("Color = 3", content, StringComparison.Ordinal);
        Assert.Contains("Map = _5F_00map_00", content, StringComparison.Ordinal);
        Assert.DoesNotContain("bw_00x_00", content, StringComparison.Ordinal);
        var load = await _service.LoadNicknameAsync(GameType.ZeroHour);
        Assert.Equal("Ace", load.Data);
    }

    /// <summary>
    /// Overlong nicknames are truncated to the game's limit before saving.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveNicknameAsync_WithOverlongNickname_ShouldTruncateAsync()
    {
        var save = await _service.SaveNicknameAsync(GameType.ZeroHour, new string('B', OnlineConstants.MaxNicknameLength + 5));

        Assert.True(save.Success);
        var load = await _service.LoadNicknameAsync(GameType.ZeroHour);
        Assert.Equal(new string('B', OnlineConstants.MaxNicknameLength), load.Data);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes test resources.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }
}
