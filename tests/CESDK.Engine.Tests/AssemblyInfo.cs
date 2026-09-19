using System.Diagnostics.CodeAnalysis;
using Xunit.Sdk;
using Xunit.v3;

// LuaRuntime is one ambient binding per load context and the fake host is process-wide static state: tests that
// attach, detach or push objects would race across parallel test classes. Sequential execution keeps every test's
// view of the runtime stable; the suite is small enough for it not to matter.
[assembly: Parallelization(Mode = ParallelMode.None)]

// SonarAnalyzer rule S6640 flags every unsafe context. These tests drive the unsafe surface of the SDK directly,
// so the rule has nothing to say here.
[assembly: SuppressMessage(
    "Major Vulnerability",
    "S6640:Unsafe code blocks should not be used",
    Justification = "Test code drives the unsafe interop surface.")]
