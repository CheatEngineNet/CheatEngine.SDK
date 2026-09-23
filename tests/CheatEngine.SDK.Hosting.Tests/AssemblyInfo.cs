using Xunit.Sdk;
using Xunit.v3;

// PluginHost, LuaRuntime and the fake exports are process-wide statics, exactly as in a Cheat Engine process where
// one loaded Hosting assembly instance hosts one plugin: tests that bootstrap, enable or disable would race across
// parallel classes. Whether one Hosting assembly instance equals one assembly load context, or one plugin, is a
// separately qualified host fact (Q09), not assumed by this statement.
[assembly: Parallelization(Mode = ParallelMode.None)]
