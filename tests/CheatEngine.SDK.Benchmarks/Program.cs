using BenchmarkDotNet.Running;

namespace CheatEngine.SDK.Benchmarks;

/// <summary>
///     Entry point of the benchmark application. Every BenchmarkDotNet switch is passed through, for example
///     <c>--list flat</c>, <c>--filter *Push*</c> or <c>--job short</c>.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Discovers every benchmark class of this assembly; with no --filter the switcher asks which ones to run.
        _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
