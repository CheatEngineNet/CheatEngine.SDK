namespace CheatEngine.SDK.LiveProbe.Tests;

// The source-linked LiveProbe sources call ProbeHostGlobals.GetOpenedProcessId, which the plugin project generates from
// [LuaGlobal("getOpenedProcessID")]. The tests inject the PID reader and never call it; this stub keeps the same
// namespace and signature without loading the LuaBindings generator or a Cheat Engine host.
internal static class ProbeHostGlobals
{
	internal static long GetOpenedProcessId()
	{
		throw new InvalidOperationException("LiveProbe unit tests must inject CE's opened-process PID.");
	}
}
