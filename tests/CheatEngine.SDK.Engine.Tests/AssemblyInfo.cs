using Xunit.Sdk;
using Xunit.v3;

// LuaRuntime is one ambient binding per load context and the fake host is process-wide static state: tests that
// attach, detach or push objects would race across parallel test classes. Sequential execution keeps every test's
// view of the runtime stable; the suite is small enough for it not to matter.
[assembly: Parallelization(Mode = ParallelMode.None)]
