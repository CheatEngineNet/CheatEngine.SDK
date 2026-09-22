namespace LiveProbe;

/// <summary>Everything <c>ce77_live_probe_status_json()</c> reports, captured under the probe's lock.</summary>
internal readonly record struct LiveProbeStatusSnapshot(
	LiveProbeHostFacts Host,
	int BootstrapCalls,
	int OpaqueSecondInt,
	bool TailCanaryWritten,
	int TailWrites,
	uint TailReadBeforeWrite,
	string? TailFailure,
	bool BootstrapGateAllowed,
	string BootstrapGateReason,
	bool RuntimeGateAllowed,
	string RuntimeGateReason,
	LiveProbeFaultDecision Fault,
	IReadOnlyList<string> InjectedFaults,
	int ManagedExceptionThrows,
	string Synchronize,
	string LuaThreads,
	string Reset,
	string Callback);
