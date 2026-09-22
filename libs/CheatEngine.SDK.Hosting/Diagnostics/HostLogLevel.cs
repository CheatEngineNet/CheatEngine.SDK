namespace CheatEngine.SDK.Hosting.Diagnostics;

/// <summary>Severity of a <see cref="HostLog" /> entry, in increasing order.</summary>
public enum HostLogLevel
{
	/// <summary>Lifecycle tracing: every bootstrap and lifecycle call with its arguments. Off by default.</summary>
	Trace = 0,

	/// <summary>A lifecycle transition that succeeded (enabled, disabled).</summary>
	Information = 1,

	/// <summary>Something unexpected that the host could tolerate.</summary>
	Warning = 2,

	/// <summary>A lifecycle call failed: Cheat Engine was told <c>FALSE</c> or 0, and this entry says why.</summary>
	Error = 3
}
