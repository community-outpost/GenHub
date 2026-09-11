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
            var trimmed = line.Trim();

            if (current == null)
            {
                if (trimmed.StartsWith("CommandMap ", StringComparison.OrdinalIgnoreCase))
                {
                    var name = trimmed[11..].Trim();
                    current = new CommandMapEntry { Name = name };
                    map.Entries.Add(current);
                }
                else
                {
                    map.HeaderLines.Add(line);
                }
            }
            else
            {
                if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
                {
                    current = null;
                }
                else
                {
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var propKey = trimmed[..eqIdx].Trim();
                        var propVal = trimmed[(eqIdx + 1)..].Trim();
                        current.Properties[propKey] = propVal;
                    }
                }
            }
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
    /// Saves the CommandMap.ini to a stream.
    /// </summary>
    /// <param name="stream">The writable stream.</param>
    public void Save(Stream stream)
    {
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);

        foreach (var line in HeaderLines)
        {
            writer.WriteLine(line);
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
    /// Represents a single CommandMap entry.
    /// </summary>
    public class CommandMapEntry
    {
        /// <summary>Gets or sets the command name (e.g. "SAVE_VIEW1").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets properties in this block.</summary>
        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets or sets the primary key (e.g. "KEY_F1").</summary>
        public string? Key
        {
            get => Properties.TryGetValue("Key", out var val) ? val : null;
            set
            {
                if (value != null)
                {
                    Properties["Key"] = value;
                }
                else
                {
                    Properties.Remove("Key");
                }
            }
        }
    }
}
