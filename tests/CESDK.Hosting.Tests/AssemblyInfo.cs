using System.Diagnostics.CodeAnalysis;
using Xunit.Sdk;
using Xunit.v3;

// PluginHost, LuaRuntime and the fake exports are process-wide statics, exactly as in a Cheat Engine process where
// one load context hosts one plugin: tests that bootstrap, enable or disable would race across parallel classes.
[assembly: Parallelization(Mode = ParallelMode.None)]

// SonarAnalyzer rule S6640 flags every unsafe context. These tests drive the unsafe surface of the SDK directly,
// so the rule has nothing to say here.
[assembly: SuppressMessage(
    "Major Vulnerability",
    "S6640:Unsafe code blocks should not be used",
    Justification = "Test code drives the unsafe interop surface.")]
