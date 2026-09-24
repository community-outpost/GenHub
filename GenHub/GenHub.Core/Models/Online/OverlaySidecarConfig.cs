using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Online.Tun;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace GenHub.Core.Models.Online;

/// <summary>
/// Parsed configuration for the overlay sidecar process.
/// </summary>
/// <param name="InterfaceName">The TUN interface name to attach.</param>
/// <param name="OverlayIp">The overlay IPv4 address assigned to this member.</param>
/// <param name="PrefixLength">The overlay subnet prefix length.</param>
/// <param name="RelayHost">The packet relay host.</param>
/// <param name="RelayPort">The packet relay UDP port.</param>
/// <param name="NetworkId">The network identifier.</param>
/// <param name="Mtu">The TUN interface MTU.</param>
public sealed record OverlaySidecarConfig(
    string InterfaceName,
    string OverlayIp,
    int PrefixLength,
    string RelayHost,
    int RelayPort,
    string NetworkId,
    int Mtu)
{
    /// <summary>
    /// Raw wire shape of the sidecar configuration file.
    /// </summary>
    /// <param name="Interface">The TUN interface name.</param>
    /// <param name="OverlayIp">The overlay IPv4 address.</param>
    /// <param name="PrefixLength">The overlay subnet prefix length, if present.</param>
    /// <param name="RelayHost">The packet relay host.</param>
    /// <param name="RelayPort">The packet relay UDP port.</param>
    /// <param name="NetworkId">The network identifier.</param>
    /// <param name="Mtu">The TUN interface MTU, if present.</param>
    public sealed record ConfigFile(
        string? Interface,
        string? OverlayIp,
        int? PrefixLength,
        string? RelayHost,
        int? RelayPort,
        string? NetworkId,
        int? Mtu);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Parses and validates sidecar configuration JSON.
    /// </summary>
    /// <param name="json">The configuration JSON.</param>
    /// <returns>The parsed configuration, or a failure describing the first problem.</returns>
    public static OperationResult<OverlaySidecarConfig> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration is empty.");
        }

        ConfigFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ConfigFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure($"Overlay configuration is not valid JSON: {ex.Message}");
        }

        if (file is null)
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration is empty.");
        }

        if (!LinuxTunNative.IsValidInterfaceName(file.Interface))
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has an invalid interface name.");
        }

        if (!IsValidIPv4(file.OverlayIp))
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has an invalid overlay IP.");
        }

        if (string.IsNullOrWhiteSpace(file.RelayHost))
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has a blank relay host.");
        }

        if (file.RelayPort is null or < 1 or > 65535)
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has an invalid relay port.");
        }

        if (string.IsNullOrWhiteSpace(file.NetworkId))
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has a blank network id.");
        }

        var prefixLength = file.PrefixLength ?? OnlineConstants.TunOverlayPrefixLength;
        if (prefixLength is < 1 or > 32)
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has an invalid prefix length.");
        }

        var mtu = file.Mtu ?? OnlineConstants.TunDefaultMtu;
        if (mtu is < 576 or > 9000)
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has an invalid MTU.");
        }

        return OperationResult<OverlaySidecarConfig>.CreateSuccess(new OverlaySidecarConfig(
            file.Interface,
            file.OverlayIp,
            prefixLength,
            file.RelayHost,
            file.RelayPort.Value,
            file.NetworkId,
            mtu));
    }

    private static bool IsValidIPv4([NotNullWhen(true)] string? value)
    {
        return IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork;
    }
}
