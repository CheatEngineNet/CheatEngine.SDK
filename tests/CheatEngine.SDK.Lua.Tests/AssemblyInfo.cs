using Xunit.Sdk;
using Xunit.v3;

// LuaRuntime is one ambient binding per load context and LuaRef epochs are read from it: tests that attach, detach
// or create references would race with each other across parallel test classes. Sequential execution keeps every
// test's view of the epoch stable; the suite is fast enough for it not to matter.
[assembly: Parallelization(Mode = ParallelMode.None)]
