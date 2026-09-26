using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Online.Tun;

/// <summary>
/// Provisions the Linux TUN interface for the overlay using iproute2,
/// escalating through polkit on desktops or non-interactive sudo on headless
/// hosts. The interface persists across joins; re-running setup just
/// re-addresses it for the current overlay IP.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed class LinuxTunSetup(ILogger<LinuxTunSetup> logger) : ITunInterfaceSetup
{
    private const int MinPrefixLength = 1;
    private const int MaxPrefixLength = 32;
    private const int MinMtu = 576;
    private const int MaxMtu = 9000;
    private const int ErrorSnippetLength = 300;

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> SetupAsync(
        string interfaceName,
        IPAddress overlayIp,
        int prefixLength,
        int mtu,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overlayIp);

        var valid = ValidateArguments(interfaceName, overlayIp, prefixLength, mtu);
        if (!valid.Success)
        {
            return valid;
        }

        var steps = await RunSetupStepsAsync(interfaceName, overlayIp, prefixLength, mtu, cancellationToken).ConfigureAwait(false);
        if (!steps.Success)
        {
            var script = BuildManualSetupScript(interfaceName, overlayIp, prefixLength, mtu, Environment.UserName);
            logger.LogError("Linux TUN setup failed: {Error}", steps.AllErrors);
            return OperationResult<bool>.CreateFailure(
                $"{steps.FirstError} To set it up manually, run: {script}. Then rejoin the lobby.");
        }

        if (!LinuxTunInterface.Exists(interfaceName))
        {
            return OperationResult<bool>.CreateFailure(
                $"Virtual LAN interface '{interfaceName}' vanished during setup.");
        }

        logger.LogInformation(
            "Linux TUN interface {Interface} is up with overlay address {Ip}/{Prefix}.",
            interfaceName,
            overlayIp,
            prefixLength);
        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <summary>
    /// Builds the iproute2 arguments creating a TUN interface owned by a user.
    /// </summary>
    /// <param name="interfaceName">The validated interface name.</param>
    /// <param name="user">The user owning the interface.</param>
    /// <returns>The argument list for the ip utility.</returns>
    internal static IReadOnlyList<string> BuildCreateArgs(string interfaceName, string user) =>
        ["tuntap", "add", "dev", interfaceName, "mode", "tun", "user", user];

    /// <summary>
    /// Builds the iproute2 arguments flushing IPv4 addresses from an interface.
    /// </summary>
    /// <param name="interfaceName">The validated interface name.</param>
    /// <returns>The argument list for the ip utility.</returns>
    internal static IReadOnlyList<string> BuildFlushArgs(string interfaceName) =>
        ["-4", "addr", "flush", "dev", interfaceName];

    /// <summary>
    /// Builds the iproute2 arguments assigning an overlay address.
    /// </summary>
    /// <param name="interfaceName">The validated interface name.</param>
    /// <param name="overlayIp">The overlay IPv4 address.</param>
    /// <param name="prefixLength">The subnet prefix length.</param>
    /// <returns>The argument list for the ip utility.</returns>
    internal static IReadOnlyList<string> BuildAddressArgs(string interfaceName, IPAddress overlayIp, int prefixLength) =>
        ["addr", "add", $"{overlayIp}/{prefixLength}", "dev", interfaceName];

    /// <summary>
    /// Builds the iproute2 arguments setting the MTU and bringing the link up.
    /// </summary>
    /// <param name="interfaceName">The validated interface name.</param>
    /// <param name="mtu">The MTU to configure.</param>
    /// <returns>The argument list for the ip utility.</returns>
    internal static IReadOnlyList<string> BuildLinkUpArgs(string interfaceName, int mtu) =>
        ["link", "set", "dev", interfaceName, "mtu", mtu.ToString(), "up"];

    /// <summary>
    /// Builds the one-line manual fallback users can paste into a terminal when
    /// escalation is unavailable.
    /// </summary>
    /// <param name="interfaceName">The validated interface name.</param>
    /// <param name="overlayIp">The overlay IPv4 address.</param>
    /// <param name="prefixLength">The subnet prefix length.</param>
    /// <param name="mtu">The MTU to configure.</param>
    /// <param name="user">The user owning the interface.</param>
    /// <returns>A shell command chaining the setup steps with sudo.</returns>
    internal static string BuildManualSetupScript(
        string interfaceName,
        IPAddress overlayIp,
        int prefixLength,
        int mtu,
        string user)
    {
        var escapedUser = user
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);
        var ip = OnlineConstants.TunIpBinary;
        var address = string.Join(' ', BuildAddressArgs(interfaceName, overlayIp, prefixLength));
        var linkUp = string.Join(' ', BuildLinkUpArgs(interfaceName, mtu));
        return $"{ip} link show {interfaceName} >/dev/null 2>&1 || sudo {ip} tuntap add mode tun dev {interfaceName} user \"{escapedUser}\" && sudo {ip} addr flush dev {interfaceName} && sudo {ip} {address} && sudo {ip} {linkUp}";
    }

    private static OperationResult<bool> ValidateArguments(
        string interfaceName,
        IPAddress overlayIp,
        int prefixLength,
        int mtu)
    {
        if (!OperatingSystem.IsLinux())
        {
            return OperationResult<bool>.CreateFailure("TUN interfaces are only supported on Linux.");
        }

        if (!LinuxTunNative.IsValidInterfaceName(interfaceName))
        {
            return OperationResult<bool>.CreateFailure($"Invalid TUN interface name '{interfaceName}'.");
        }

        if (overlayIp.AddressFamily != AddressFamily.InterNetwork)
        {
            return OperationResult<bool>.CreateFailure("Overlay configuration has an invalid overlay IP.");
        }

        if (prefixLength is < MinPrefixLength or > MaxPrefixLength)
        {
            return OperationResult<bool>.CreateFailure("Overlay configuration has an invalid prefix length.");
        }

        if (mtu is < MinMtu or > MaxMtu)
        {
            return OperationResult<bool>.CreateFailure("Overlay configuration has an invalid MTU.");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static async Task<CommandResult> RunIpAsync(IReadOnlyList<string> ipArgs, CancellationToken cancellationToken)
    {
        if (LinuxTunNative.IsElevated())
        {
            return await RunProcessAsync(OnlineConstants.TunIpBinary, ipArgs, cancellationToken).ConfigureAwait(false);
        }

        var pkexecArgs = new List<string> { "--disable-internal-agent", OnlineConstants.TunIpBinary };
        pkexecArgs.AddRange(ipArgs);
        var pkexec = await RunProcessAsync(OnlineConstants.TunPkexecBinary, pkexecArgs, cancellationToken).ConfigureAwait(false);
        if (pkexec.Succeeded)
        {
            return pkexec;
        }

        var sudoArgs = new List<string> { OnlineConstants.TunSudoNonInteractiveFlag, OnlineConstants.TunIpBinary };
        sudoArgs.AddRange(ipArgs);
        var sudo = await RunProcessAsync(OnlineConstants.TunSudoBinary, sudoArgs, cancellationToken).ConfigureAwait(false);
        if (sudo.Succeeded)
        {
            return sudo;
        }

        return CommandResult.StartFailure($"escalation failed (pkexec: {pkexec.Describe()}; sudo: {sudo.Describe()})");
    }

    [SuppressMessage("Security", "S4036:ProcessStartInfo.FileName should not be relative", Justification = "Privileged helpers resolve via PATH across distributions.")]
    private static async Task<CommandResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        using var process = new Process { EnableRaisingEvents = true };
        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => AppendLine(output, e.Data);
        process.ErrorDataReceived += (_, e) => AppendLine(output, e.Data);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return CommandResult.StartFailure($"Cannot start '{fileName}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(OnlineConstants.TunSetupTimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillSilently(process);
            if (timeout.IsCancellationRequested)
            {
                return CommandResult.StartFailure($"'{fileName}' timed out after {OnlineConstants.TunSetupTimeoutMs}ms.");
            }

            return CommandResult.StartFailure($"'{fileName}' was cancelled.");
        }

        // After an async wait, drain the redirected streams. The parameterless WaitForExit() is required
        // by the runtime to ensure asynchronous redirected stdout/stderr events are fully processed.
#pragma warning disable S6966 // Synchronous WaitForExit required by design to drain redirected output streams
        process.WaitForExit();
#pragma warning restore S6966
        return new CommandResult(true, process.ExitCode, Snippet(output.ToString()));
    }

    private static void AppendLine(StringBuilder builder, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        // Stdout and stderr readers fire on different threads.
        lock (builder)
        {
            builder.Append(line.Trim()).Append(' ');
        }
    }

    private static string Snippet(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= ErrorSnippetLength)
        {
            return trimmed;
        }

        return trimmed[..ErrorSnippetLength] + "...";
    }

    private static void KillSilently(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited.
        }
    }

    private static async Task<OperationResult<bool>> RunSetupStepsAsync(
        string interfaceName,
        IPAddress overlayIp,
        int prefixLength,
        int mtu,
        CancellationToken cancellationToken)
    {
        var created = await EnsureInterfaceAsync(interfaceName, cancellationToken).ConfigureAwait(false);
        if (!created.Success)
        {
            return created;
        }

        var addressed = await AssignAddressAsync(interfaceName, overlayIp, prefixLength, cancellationToken).ConfigureAwait(false);
        if (!addressed.Success)
        {
            return addressed;
        }

        return await BringLinkUpAsync(interfaceName, mtu, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OperationResult<bool>> EnsureInterfaceAsync(string interfaceName, CancellationToken cancellationToken)
    {
        if (LinuxTunInterface.Exists(interfaceName))
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        var create = await RunIpAsync(BuildCreateArgs(interfaceName, Environment.UserName), cancellationToken).ConfigureAwait(false);
        if (!create.Succeeded)
        {
            return OperationResult<bool>.CreateFailure(
                $"Could not create virtual LAN interface '{interfaceName}' ({create.Describe()}).");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static async Task<OperationResult<bool>> AssignAddressAsync(
        string interfaceName,
        IPAddress overlayIp,
        int prefixLength,
        CancellationToken cancellationToken)
    {
        var flush = await RunIpAsync(BuildFlushArgs(interfaceName), cancellationToken).ConfigureAwait(false);
        if (!flush.Succeeded)
        {
            return OperationResult<bool>.CreateFailure(
                $"Could not flush addresses on '{interfaceName}' ({flush.Describe()}).");
        }

        var address = await RunIpAsync(
            BuildAddressArgs(interfaceName, overlayIp, prefixLength), cancellationToken).ConfigureAwait(false);
        if (!address.Succeeded)
        {
            return OperationResult<bool>.CreateFailure(
                $"Could not assign overlay address {overlayIp}/{prefixLength} to '{interfaceName}' ({address.Describe()}).");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static async Task<OperationResult<bool>> BringLinkUpAsync(
        string interfaceName,
        int mtu,
        CancellationToken cancellationToken)
    {
        var link = await RunIpAsync(BuildLinkUpArgs(interfaceName, mtu), cancellationToken).ConfigureAwait(false);
        if (!link.Succeeded)
        {
            return OperationResult<bool>.CreateFailure(
                $"Could not bring virtual LAN interface '{interfaceName}' up ({link.Describe()}).");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private sealed record CommandResult(bool Started, int ExitCode, string Output)
    {
        public bool Succeeded => Started && ExitCode == 0;

        public string Describe() => Started ? $"exit {ExitCode} ({Output})" : Output;

        public static CommandResult StartFailure(string reason) => new(false, -1, reason);
    }
}
