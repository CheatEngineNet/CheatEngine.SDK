namespace CESDK.SourceGenerators.EngineApi.Tests.Infrastructure;

/// <summary>
///     The collection of every test class that attaches <c>LuaRuntime</c>: the runtime is one ambient binding per
///     process and its epoch is read by every cached reference, so those classes run one at a time while the Roslyn-only
///     classes keep running in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LuaRuntimeSuite
{
    /// <summary>The collection name.</summary>
    public const string Name = "Lua runtime";
}
