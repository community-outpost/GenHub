using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GenHub.Core.Services.Tools.GenHotkeys;

/// <summary>
/// Parser and serializer for Command &amp; Conquer Generals CommandMap.ini.
/// </summary>
public class CommandMapFile
{
    /// <summary>Gets the list of entries in the CommandMap file.</summary>
    public List<CommandMapEntry> Entries { get; } = [];

    /// <summary>Gets header comments or preamble lines.</summary>
    public List<string> HeaderLines { get; } = [];

    /// <summary>
    /// Loads a CommandMap.ini file from disk.
    /// </summary>
    /// <param name="filePath">The absolute path to the file.</param>
    /// <returns>A loaded <see cref="CommandMapFile"/> instance.</returns>
    public static CommandMapFile Load(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Load(stream);
    }

    /// <summary>
    /// Loads a CommandMap.ini file from a stream.
    /// </summary>
    /// <param name="stream">The readable stream.</param>
    /// <returns>A loaded <see cref="CommandMapFile"/> instance.</returns>
    public static CommandMapFile Load(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var map = new CommandMapFile();
        CommandMapEntry? current = null;

        while (reader.ReadLine() is { } line)
        {
            current = ProcessLine(map, current, line);
        }

        return map;
    }

    /// <summary>
    /// Saves the CommandMap.ini to disk.
    /// </summary>
    /// <param name="filePath">Target destination file path.</param>
    public void Save(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = File.Create(filePath);
        Save(stream);
    }

    /// <summary>
    /// Saves the CommandMap.ini to a stream without emitting a UTF-8 BOM,
    /// as the SAGE INI parser does not support byte order marks.
    /// </summary>
    /// <param name="stream">The writable stream.</param>
    public void Save(Stream stream)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);

        foreach (var header in HeaderLines)
        {
            writer.WriteLine(header);
        }

        foreach (var entry in Entries)
        {
            writer.WriteLine($"CommandMap {entry.Name}");
            foreach (var (k, v) in entry.Properties)
            {
                writer.WriteLine($"  {k} = {v}");
            }

            writer.WriteLine("End");
            writer.WriteLine();
        }
    }

    /// <summary>
    /// Finds or creates a CommandMap entry by its section name.
    /// </summary>
    /// <param name="name">The CommandMap section name.</param>
    /// <returns>The matching or newly appended entry.</returns>
    public CommandMapEntry GetOrCreateEntry(string name)
    {
        var existing = Entries.Find(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        var entry = new CommandMapEntry { Name = name };
        Entries.Add(entry);
        return entry;
    }

    private static CommandMapEntry? ProcessLine(CommandMapFile map, CommandMapEntry? current, string line)
    {
        var trimmed = line.Trim();

        if (current == null)
        {
            if (trimmed.StartsWith("CommandMap", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var name = parts.Length > 1 ? parts[1] : string.Empty;
                return map.GetOrCreateEntry(name);
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                map.HeaderLines.Add(line);
            }

            return null;
        }

        if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var eqIdx = line.IndexOf('=');
        if (eqIdx > 0)
        {
            var key = line[..eqIdx].Trim();
            var val = line[(eqIdx + 1)..].Trim();
            current.Properties[key] = val;
        }

        return current;
    }
}
