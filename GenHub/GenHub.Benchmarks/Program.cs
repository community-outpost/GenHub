using BenchmarkDotNet.Running;
using GenHub.Benchmarks.ModBuilder;

namespace GenHub.Benchmarks;

/// <summary>
/// Entry point for ModBuilder performance benchmarks.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // Run all ModBuilder benchmarks
        BenchmarkRunner.Run<ModBuilderBenchmarks>(args: args);
    }
}
