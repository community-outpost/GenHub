using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace GenHub.Linux;

// This class plays the roll of Windows Registry 
public class SystemApps
{
    // there should be only 1 instance
    public static SystemApps Instance { get; private set; }
    // list of Apps (binaries) to look for
    public static readonly string[] AppnNames = ["steam", "flatpak", "lutris"];
    public List<string> Path { get; private set; } = new List<string>();
    public Dictionary<string,string> Apps { get; private set; } = new  Dictionary<string, string>();
    public List<string> FlatpackApps { get; private set; } = new List<string>();
    static SystemApps()
    {
        Instance = new SystemApps();
    }

    private SystemApps()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path != null)
            Path.AddRange(path.Split(":").Where(Directory.Exists));
        foreach (var item in AppnNames)
        {
            var res = Whereis(item);
            if (res.Length > 0)
                Apps[item] = res;
        }
    }

    /// <summary>
    /// This is an equivalent of whereis command on windows
    /// </summary>
    /// <param name="program">name of the program to search for</param>
    /// <returns> full path if founded, "" if not</returns>
    private string Whereis(string program)
    {
        if (Path.Count == 0) return "";
        // a query that finds directories containing program
        bool Predict(string item) => File.Exists(System.IO.Path.Combine(item, program));
        // if this query has an output, return it
        return !Path.Exists(Predict) ? "" : System.IO.Path.Combine(Path.First(Predict), program);    }
}