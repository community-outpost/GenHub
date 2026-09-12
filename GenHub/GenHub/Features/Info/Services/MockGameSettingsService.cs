using System.Collections.Generic;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Results;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Mock game settings service.
/// </summary>
public class MockGameSettingsService : IGameSettingsService
{
    /// <inheritdoc/>
    public string GetOptionsFilePath(GameType gameType) => $"C:\\Users\\Demo\\Documents\\{gameType} Data\\Options.ini";

    /// <inheritdoc/>
    public Task<OperationResult<GeneralsOnlineSettings>> LoadGeneralsOnlineSettingsAsync()
    {
        return Task.FromResult(OperationResult<GeneralsOnlineSettings>.CreateSuccess(new GeneralsOnlineSettings
        {
             ShowFps = true,
             Render = { FpsLimit = 144 },
             AutoLogin = true,
        }));
    }

    /// <inheritdoc/>
    public Task<OperationResult<IniOptions>> LoadOptionsAsync(GameType gameType)
    {
        var options = new IniOptions();
        options.Video.ResolutionWidth = 1920;
        options.Video.ResolutionHeight = 1080;
        options.Video.UseShadowVolumes = true;
        options.Audio.AudioEnabled = true;

        // Mock TSH settings
        options.AdditionalSections["TheSuperHackers"] = new Dictionary<string, string>
        {
            ["ShowMoneyPerMinute"] = "yes",
            ["RenderFpsFontSize"] = "14",
        };

        return Task.FromResult(OperationResult<IniOptions>.CreateSuccess(options));
    }

    /// <inheritdoc/>
    public Task<OperationResult<TheSuperHackersSettings>> LoadTheSuperHackersSettingsAsync(GameType gameType)
    {
         return Task.FromResult(OperationResult<TheSuperHackersSettings>.CreateSuccess(new TheSuperHackersSettings()));
    }

    /// <inheritdoc/>
    public bool OptionsFileExists(GameType gameType) => true;

    /// <inheritdoc/>
    public Task<OperationResult<bool>> SaveGeneralsOnlineSettingsAsync(GeneralsOnlineSettings settings)
    {
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> SaveOptionsAsync(GameType gameType, IniOptions options)
    {
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> SaveTheSuperHackersSettingsAsync(GameType gameType, TheSuperHackersSettings settings)
    {
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }
}
