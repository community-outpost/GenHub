using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.SingleInstance;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace GenHub.Core.Infrastructure.SingleInstance;

/// <summary>
/// Thread-safe dispatcher and buffer for single-instance IPC commands.
/// Replays pending commands safely to subscribers and validates incoming IPC commands.
/// </summary>
public sealed class SingleInstanceCommandDispatcher : ISingleInstanceCommandReceiver
{
    private readonly object _commandLock = new();
    private readonly List<string> _pendingCommands = [];
    private readonly object? _sender;
    private readonly ILogger? _logger;
    private EventHandler<string>? _commandReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstanceCommandDispatcher"/> class.
    /// </summary>
    /// <param name="sender">The object to pass as the event sender when raising <see cref="CommandReceived"/>.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public SingleInstanceCommandDispatcher(object? sender = null, ILogger? logger = null)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>
    /// Occurs when a command is received from another instance.
    /// Pending commands received prior to subscription are replayed safely upon registration.
    /// </summary>
    public event EventHandler<string>? CommandReceived
    {
        add
        {
            List<string>? commandsToReplay = null;
            lock (_commandLock)
            {
                _commandReceived += value;
                if (_pendingCommands.Count > 0)
                {
                    commandsToReplay = [.. _pendingCommands];
                    _pendingCommands.Clear();
                }
            }

            if (commandsToReplay != null && value != null)
            {
                foreach (var cmd in commandsToReplay)
                {
                    try
                    {
                        value.Invoke(_sender ?? this, cmd);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error replaying buffered single-instance command: {Command}", cmd);
                    }
                }
            }
        }

        remove
        {
            lock (_commandLock)
            {
                _commandReceived -= value;
            }
        }
    }

    /// <summary>
    /// Validates whether a command string matches a known valid IPC command pattern.
    /// </summary>
    /// <param name="command">The command string to validate.</param>
    /// <returns><see langword="true"/> if valid; otherwise, <see langword="false"/>.</returns>
    public static bool IsValidIpcCommand(string command)
    {
        if (string.Equals(command, IpcCommands.ActivateCommand, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (command.StartsWith(IpcCommands.LaunchProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = command[IpcCommands.LaunchProfilePrefix.Length..].Trim();
            return !string.IsNullOrEmpty(id) && !id.Contains('/') && !id.Contains('\\') && !id.Contains("..");
        }

        if (command.StartsWith(IpcCommands.SubscribePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var url = command[IpcCommands.SubscribePrefix.Length..].Trim();
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        if (command.StartsWith(IpcCommands.ImportProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var target = command[IpcCommands.ImportProfilePrefix.Length..].Trim();
            if (target.StartsWith(CommandLineConstants.ProfileImportUriPrefix, StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(CommandLineConstants.ProfileViewUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (target.EndsWith(ProfileSharingConstants.ProfileFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        if (command.StartsWith(IpcCommands.ImportMapPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var target = command[IpcCommands.ImportMapPrefix.Length..].Trim();
            return IsToolShareCommand(target, CommandLineConstants.MapCommand);
        }

        if (command.StartsWith(IpcCommands.ImportReplayPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var target = command[IpcCommands.ImportReplayPrefix.Length..].Trim();
            return IsToolShareCommand(target, CommandLineConstants.ReplayCommand);
        }

        return false;
    }

    /// <summary>
    /// Dispatches a command immediately if a subscriber is registered, or buffers it for later replay.
    /// </summary>
    /// <param name="command">The command string to dispatch or buffer.</param>
    public void DispatchOrBuffer(string command)
    {
        EventHandler<string>? handler;
        lock (_commandLock)
        {
            handler = _commandReceived;
            if (handler == null)
            {
                _pendingCommands.Add(command);
                _logger?.LogDebug("No subscriber attached to CommandReceived; buffered command: {Command}", command);
                return;
            }
        }

        try
        {
            handler.Invoke(_sender ?? this, command);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error invoking CommandReceived for command: {Command}", command);
        }
    }

    /// <summary>
    /// Sanitizes, validates, logs, and dispatches an incoming raw IPC line payload.
    /// </summary>
    /// <param name="rawPayload">The raw payload string read from the pipe.</param>
    /// <param name="platformName">The platform name for diagnostic log messages.</param>
    /// <returns><see langword="true"/> if the command was recognized and handled; otherwise, <see langword="false"/>.</returns>
    public bool TryProcessRawPayload(string? rawPayload, string platformName)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return false;
        }

        var command = CommandLineParser.SanitizePayload(rawPayload).Trim();
        if (IsValidIpcCommand(command))
        {
            LogReceivedCommand(command);
            DispatchOrBuffer(command);
            return true;
        }

        _logger?.LogWarning("Rejecting unknown or malformed IPC command from secondary {Platform} instance.", platformName);
        return false;
    }

    /// <summary>
    /// Logs receipt of an IPC command, masking sensitive argument values when appropriate.
    /// </summary>
    /// <param name="command">The command that was received.</param>
    public void LogReceivedCommand(string command)
    {
        if (_logger == null)
        {
            return;
        }

        if (TryGetMaskedPrefix(command, out var prefix))
        {
            _logger.LogInformation(LogMessages.ReceivedMaskedIpcCommand, prefix);
        }
        else if (command.StartsWith(IpcCommands.LaunchProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = command[IpcCommands.LaunchProfilePrefix.Length..];
            _logger.LogInformation("Received IPC command: {Prefix}{ProfileId}", IpcCommands.LaunchProfilePrefix, id);
        }
        else
        {
            _logger.LogInformation("Received IPC command: {Command}", command);
        }
    }

    private static bool IsToolShareCommand(string target, string toolCommand)
    {
        return ToolShareLink.TryParseShareUri(target, out var parsed) &&
            parsed != null &&
            parsed.ToolCommand.Equals(toolCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetMaskedPrefix(string command, out string prefix)
    {
        if (command.StartsWith(IpcCommands.ImportProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = IpcCommands.ImportProfilePrefix;
            return true;
        }

        if (command.StartsWith(IpcCommands.ImportMapPrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = IpcCommands.ImportMapPrefix;
            return true;
        }

        if (command.StartsWith(IpcCommands.ImportReplayPrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = IpcCommands.ImportReplayPrefix;
            return true;
        }

        if (command.StartsWith(IpcCommands.SubscribePrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = IpcCommands.SubscribePrefix;
            return true;
        }

        prefix = string.Empty;
        return false;
    }
}
