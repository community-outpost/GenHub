using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Mock user settings service.
/// </summary>
public class MockUserSettingsService : IUserSettingsService
{
    private readonly UserSettings _settings = new();

    /// <summary>
    /// Loads the settings.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public static Task LoadAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public UserSettings Get() => _settings;

    /// <inheritdoc/>
    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public void Update(Action<UserSettings> updateAction) => updateAction(_settings);

    /// <inheritdoc/>
    public Task<bool> TryUpdateAndSaveAsync(Func<UserSettings, bool> applyChanges)
    {
        applyChanges(_settings);
        return Task.FromResult(true);
    }
}
