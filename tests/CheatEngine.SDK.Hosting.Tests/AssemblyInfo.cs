using Xunit.Sdk;
using Xunit.v3;

// PluginHost, LuaRuntime and the fake exports are process-wide statics, exactly as in a Cheat Engine process where
// one load context hosts one plugin: tests that bootstrap, enable or disable would race across parallel classes.
[assembly: Parallelization(Mode = ParallelMode.None)]
