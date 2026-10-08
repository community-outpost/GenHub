using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Imports and exports .teams team exchange files.
/// Export mirrors teamExport: only the selected owner's non-default teams.
/// Import mirrors teamImport: ownership retarget plus unique-name validation.
/// </summary>
public sealed class TeamExchangeService(ILogger<TeamExchangeService> logger) : ITeamExchangeService
{
    /// <inheritdoc />
    public async Task<OperationResult<int>> ExportAsync(
        string teamsPath,
        IReadOnlyList<MapTeamEntry> teams,
        string ownerName,
        IReadOnlySet<string> defaultTeamNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamsPath);
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(ownerName);
        ArgumentNullException.ThrowIfNull(defaultTeamNames);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exported = teams
                .Where(t => string.Equals(t.Properties.GetString(WorldBuilderConstants.DictKeys.TeamOwner), ownerName, StringComparison.OrdinalIgnoreCase))
                .Where(t => !defaultTeamNames.Contains(t.Properties.GetString(WorldBuilderConstants.DictKeys.TeamName)))
                .ToList();
            var writer = new MapChunkWriter();
            MapObjectCodec.WriteTeams(writer, exported);
            await File.WriteAllBytesAsync(teamsPath, writer.ToFileBytes(), cancellationToken).ConfigureAwait(false);
            return OperationResult<int>.CreateSuccess(exported.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to export teams to {TeamsPath}.", teamsPath);
            return OperationResult<int>.CreateFailure($"Failed to export teams: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<MapTeamEntry>>> ImportAsync(
        string teamsPath,
        string targetOwner,
        IReadOnlySet<string> existingNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamsPath);
        ArgumentNullException.ThrowIfNull(targetOwner);
        ArgumentNullException.ThrowIfNull(existingNames);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(teamsPath))
            {
                return OperationResult<IReadOnlyList<MapTeamEntry>>.CreateFailure("Team file not found.");
            }

            var bytes = await File.ReadAllBytesAsync(teamsPath, cancellationToken).ConfigureAwait(false);
            var parsed = MapChunkReader.TryParse(bytes);
            if (!parsed.Success || parsed.Data == null)
            {
                return OperationResult<IReadOnlyList<MapTeamEntry>>.CreateFailure(parsed.FirstError ?? "Team parsing failed.");
            }

            var node = parsed.Data.TopLevel.FirstOrDefault(n => n.Label == WorldBuilderConstants.Chunks.ScriptTeams);
            if (node == null)
            {
                return OperationResult<IReadOnlyList<MapTeamEntry>>.CreateFailure("No team data found.");
            }

            var used = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            var imported = new List<MapTeamEntry>();
            foreach (var team in MapObjectCodec.ReadTeams(parsed.Data, node))
            {
                var name = team.Properties.GetString(WorldBuilderConstants.DictKeys.TeamName);
                var unique = UniqueName(name, used);
                used.Add(unique);
                team.Properties.Set(new MapDictValue(
                    WorldBuilderConstants.DictKeys.TeamName,
                    WorldBuilderConstants.DictValueType.AsciiString,
                    StringValue: unique));
                team.Properties.Set(new MapDictValue(
                    WorldBuilderConstants.DictKeys.TeamOwner,
                    WorldBuilderConstants.DictValueType.AsciiString,
                    StringValue: targetOwner));
                imported.Add(team);
            }

            return OperationResult<IReadOnlyList<MapTeamEntry>>.CreateSuccess(imported);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning(ex, "Failed to import teams from {TeamsPath}.", teamsPath);
            return OperationResult<IReadOnlyList<MapTeamEntry>>.CreateFailure($"Failed to import teams: {ex.Message}");
        }
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        if (!used.Contains(name))
        {
            return name;
        }

        var counter = 1;
        while (used.Contains($"{name}_{counter}"))
        {
            counter++;
        }

        return $"{name}_{counter}";
    }
}
