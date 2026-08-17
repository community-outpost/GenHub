using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;
using GenHub.Benchmarks.ModBuilder;

namespace GenHub.Benchmarks;

/// <summary>
/// Entry point for ModBuilder performance benchmarks.
/// </summary>
public static class Program
{
    /// <summary>
    /// Application main entry point.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && !args.Contains("--bdn"))
        {
            var directRunner = new ModBuilderDirectRunner();
            return await directRunner.RunAsync(args);
        }

        // Run BenchmarkDotNet benchmarks
        _ = BenchmarkRunner.Run<ModBuilderBenchmarks>(args: args);
        return 0;
    }
}
