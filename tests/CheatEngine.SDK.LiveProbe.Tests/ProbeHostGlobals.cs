namespace LiveProbe;

// The source-linked LiveProbeState tests inject the PID reader and never call this generated Lua global. Keeping the
// stub local avoids loading a plugin generator or a Cheat Engine host during unit tests.
internal static class ProbeHostGlobals
{
	internal static long GetOpenedProcessId()
	{
		throw new InvalidOperationException("LiveProbe unit tests must inject CE's opened-process PID.");
	}
}
