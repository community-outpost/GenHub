using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameInstallations;

/// <summary>
/// Provides persistence for manually registered game installations.
/// </summary>
public sealed class ManualInstallationStorage : IManualInstallationStorage
{
    private const string ManualInstallationsFileName = "manual_installations.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IAppConfiguration _appConfig;
    private readonly ILogger<ManualInstallationStorage> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _storagePath;
    private List<ManualInstallationDto>? _cachedInstallations;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManualInstallationStorage"/> class.
    /// </summary>
    /// <param name="appConfig">The application configuration.</param>
    /// <param name="logger">The logger.</param>
    public ManualInstallationStorage(
        IAppConfiguration appConfig,
        ILogger<ManualInstallationStorage> logger)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storagePath = Path.Combine(
            _appConfig.GetConfiguredDataPath(),
            ManualInstallationsFileName);
    }

    /// <summary>
    /// Gets all persisted manual installations.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of manual installations.</returns>
    public async Task<List<GameInstallation>> LoadManualInstallationsAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedInstallations != null)
            {
                return _cachedInstallations.Select(dto => dto.ToGameInstallation()).ToList();
            }

            if (!File.Exists(_storagePath))
            {
                _logger.LogInformation(
                    "[ManualInstallationStorage] No manual installations file found at {Path}",
                    _storagePath);
                _cachedInstallations = new List<ManualInstallationDto>();
                return new List<GameInstallation>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_storagePath, cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogWarning(
                        "[ManualInstallationStorage] Manual installations file is empty at {Path}",
                        _storagePath);
                    _cachedInstallations = new List<ManualInstallationDto>();
                    return new List<GameInstallation>();
                }

                var dtos = JsonSerializer.Deserialize<List<ManualInstallationDto>>(json, JsonOptions)
                    ?? new List<ManualInstallationDto>();

                _cachedInstallations = dtos;
                var installations = dtos.Select(dto => dto.ToGameInstallation()).ToList();

                _logger.LogInformation(
                    "[ManualInstallationStorage] Loaded {Count} manual installations from {Path}",
                    installations.Count,
                    _storagePath);

                return installations;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[ManualInstallationStorage] Failed to load manual installations from {Path}",
                    _storagePath);
                _cachedInstallations = new List<ManualInstallationDto>();
                return new List<GameInstallation>();
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Saves a manual installation to persistent storage.
    /// </summary>
    /// <param name="installation">The installation to save.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SaveManualInstallationAsync(GameInstallation installation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            // Load existing installations
            var dtos = _cachedInstallations ?? new List<ManualInstallationDto>();

            // Update existing or add new
            var existingIndex = dtos.FindIndex(dto => dto.Id == installation.Id);
            if (existingIndex >= 0)
            {
                dtos[existingIndex] = ManualInstallationDto.FromGameInstallation(installation);
                _logger.LogInformation(
                    "[ManualInstallationStorage] Updated manual installation {Id} in storage",
                    installation.Id);
            }
            else
            {
                dtos.Add(ManualInstallationDto.FromGameInstallation(installation));
                _logger.LogInformation(
                    "[ManualInstallationStorage] Added manual installation {Id} to storage",
                    installation.Id);
            }

            _cachedInstallations = dtos;

            // Save to file
            await SaveToFileAsync(dtos, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Removes a manual installation from persistent storage.
    /// </summary>
    /// <param name="installationId">The installation ID to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RemoveManualInstallationAsync(string installationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installationId))
        {
            return;
        }

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedInstallations == null)
            {
                return;
            }

            var removed = _cachedInstallations.RemoveAll(dto => dto.Id == installationId);
            if (removed > 0)
            {
                _logger.LogInformation(
                    "[ManualInstallationStorage] Removed manual installation {Id} from storage",
                    installationId);
                await SaveToFileAsync(_cachedInstallations, cancellationToken);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Clears all manual installations from persistent storage.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            _cachedInstallations = new List<ManualInstallationDto>();

            if (File.Exists(_storagePath))
            {
                File.Delete(_storagePath);
                _logger.LogInformation(
                    "[ManualInstallationStorage] Cleared all manual installations from {Path}",
                    _storagePath);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task SaveToFileAsync(List<ManualInstallationDto> dtos, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(dtos, JsonOptions);
            await File.WriteAllTextAsync(_storagePath, json, cancellationToken);

            _logger.LogDebug(
                "[ManualInstallationStorage] Saved {Count} manual installations to {Path}",
                dtos.Count,
                _storagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[ManualInstallationStorage] Failed to save manual installations to {Path}",
                _storagePath);
            throw;
        }
    }

    /// <summary>
    /// Data transfer object for serializing manual installations.
    /// </summary>
    private sealed class ManualInstallationDto
    {
        public string Id { get; set; } = string.Empty;

        public string InstallationPath { get; set; } = string.Empty;

        public string InstallationType { get; set; } = string.Empty;

        public bool HasGenerals { get; set; }

        public string GeneralsPath { get; set; } = string.Empty;

        public bool HasZeroHour { get; set; }

        public string ZeroHourPath { get; set; } = string.Empty;

        public DateTime DetectedAt { get; set; }

        public static ManualInstallationDto FromGameInstallation(GameInstallation installation)
        {
            return new ManualInstallationDto
            {
                Id = installation.Id,
                InstallationPath = installation.InstallationPath,
                InstallationType = installation.InstallationType.ToString(),
                HasGenerals = installation.HasGenerals,
                GeneralsPath = installation.GeneralsPath,
                HasZeroHour = installation.HasZeroHour,
                ZeroHourPath = installation.ZeroHourPath,
                DetectedAt = installation.DetectedAt,
            };
        }

        public GameInstallation ToGameInstallation()
        {
            return new GameInstallation(InstallationPath, ParseInstallationType())
            {
                Id = Id,
                HasGenerals = HasGenerals,
                GeneralsPath = GeneralsPath,
                HasZeroHour = HasZeroHour,
                ZeroHourPath = ZeroHourPath,
                DetectedAt = DetectedAt,
            };
        }

        private GameInstallationType ParseInstallationType()
        {
            if (Enum.TryParse<GameInstallationType>(InstallationType, out var type))
            {
                return type;
            }

            return GameInstallationType.Retail;
        }
    }
}
