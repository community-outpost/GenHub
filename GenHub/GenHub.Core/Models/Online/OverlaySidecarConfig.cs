using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Online.Tun;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
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
    /// <param name="Mtu">The interface MTU, if present.</param>
    public sealed record WireShape(
        string? Interface,
        string? OverlayIp,
        int? PrefixLength,
        string? RelayHost,
        int? RelayPort,
        string? NetworkId,
        int? Mtu);

    /// <summary>
    /// Parses and validates sidecar configuration JSON or base64-encoded JSON.
    /// </summary>
    /// <param name="json">The configuration JSON or base64 string.</param>
    /// <returns>The parsed configuration, or a failure describing the first problem.</returns>
    public static OperationResult<OverlaySidecarConfig> Parse(string? json) => Parse(json, null);

    /// <summary>
    /// Parses and validates sidecar configuration JSON or base64-encoded JSON with an optional fallback overlay IP.
    /// </summary>
    /// <param name="json">The configuration JSON or base64 string.</param>
    /// <param name="defaultOverlayIp">Optional overlay IP address to use if omitted from configuration.</param>
    /// <returns>The parsed configuration, or a failure describing the first problem.</returns>
    public static OperationResult<OverlaySidecarConfig> Parse(string? json, string? defaultOverlayIp)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration is empty.");
        }

        var decodedJson = TryDecodeBase64(json);

        try
        {
            using var doc = JsonDocument.Parse(decodedJson);
            var root = doc.RootElement;

            // Interface name: default to OS appropriate name if not provided
            string iface;
            if (root.TryGetProperty("interface", out var ifaceProp))
            {
                var val = ifaceProp.ValueKind == JsonValueKind.String ? ifaceProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(val) || !LinuxTunNative.IsValidInterfaceName(val))
                {
                    return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has an invalid interface name.");
                }

                iface = val;
            }
            else
            {
                iface = OperatingSystem.IsWindows()
                    ? OnlineConstants.TunDefaultWindowsInterfaceName
                    : OnlineConstants.TunDefaultInterfaceName;
            }

            // Overlay IP: check "overlayIp" and "assignedIp"
            var overlayIp = GetStringProperty(root, "overlayIp")
                ?? GetStringProperty(root, "assignedIp")
                ?? defaultOverlayIp;
            if (!IsValidIPv4(overlayIp))
            {
                return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has an invalid overlay IP.");
            }

            // Network ID
            var networkId = GetStringProperty(root, "networkId");
            if (string.IsNullOrWhiteSpace(networkId))
            {
                return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has a blank network id.");
            }

            // Relay host and port: check flat fields first, then nested "relay" or "gateway" object, then defaults
            var relayHost = GetStringProperty(root, "relayHost");
            int? relayPort = GetIntProperty(root, "relayPort");

            if ((root.TryGetProperty("relay", out var relayElement) || root.TryGetProperty("gateway", out relayElement))
                && relayElement.ValueKind == JsonValueKind.Object)
            {
                if (string.IsNullOrWhiteSpace(relayHost))
                {
                    relayHost = GetStringProperty(relayElement, "host");
                }

                relayPort ??= GetIntProperty(relayElement, "port");
            }

            if (string.IsNullOrWhiteSpace(relayHost))
            {
                relayHost = ApiConstants.OnlineRelayHost;
            }

            relayPort ??= OnlineConstants.DefaultRelayPort;

            if (relayPort is < 1 or > 65535)
            {
                return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has an invalid relay port.");
            }

            // Prefix length / subnet
            var prefixLength = GetIntProperty(root, "prefixLength");
            if (prefixLength is null && root.TryGetProperty("subnet", out var subnetElement) && subnetElement.ValueKind == JsonValueKind.String)
            {
                var subnet = subnetElement.GetString();
                if (!string.IsNullOrEmpty(subnet) && subnet.Contains('/'))
                {
                    var slashIndex = subnet.IndexOf('/');
                    if (int.TryParse(subnet[(slashIndex + 1)..], out var parsedPrefix))
                    {
                        prefixLength = parsedPrefix;
                    }
                }
            }

            if (prefixLength is null && root.TryGetProperty("subnetMask", out var maskElement) && maskElement.ValueKind == JsonValueKind.String)
            {
                var maskStr = maskElement.GetString();
                if (IPAddress.TryParse(maskStr, out var maskIp))
                {
                    var bytes = maskIp.GetAddressBytes();
                    var bits = 0;
                    foreach (var b in bytes)
                    {
                        bits += BitOperations.PopCount(b);
                    }

                    prefixLength = bits;
                }
            }

            prefixLength ??= OnlineConstants.TunOverlayPrefixLength;
            if (prefixLength is < 1 or > 32)
            {
                return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has an invalid prefix length.");
            }

            // MTU
            var mtu = GetIntProperty(root, "mtu") ?? OnlineConstants.TunDefaultMtu;
            if (mtu is < 576 or > 9000)
            {
                return OperationResult<OverlaySidecarConfig>.CreateFailure("Overlay configuration has an invalid MTU.");
            }

            return OperationResult<OverlaySidecarConfig>.CreateSuccess(new OverlaySidecarConfig(
                iface,
                overlayIp,
                prefixLength.Value,
                relayHost,
                relayPort.Value,
                networkId,
                mtu));
        }
        catch (JsonException ex)
        {
            return OperationResult<OverlaySidecarConfig>.CreateFailure($"Malformed JSON: {ex.Message}");
        }
    }

    private static string TryDecodeBase64(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return trimmed;
        }

        try
        {
            var bytes = Convert.FromBase64String(trimmed);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return trimmed;
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }

        return null;
    }

    private static int? GetIntProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
            {
                return value;
            }

            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool IsValidIPv4([NotNullWhen(true)] string? ip)
    {
        return !string.IsNullOrWhiteSpace(ip)
            && IPAddress.TryParse(ip, out var address)
            && address.AddressFamily == AddressFamily.InterNetwork;
    }
}
