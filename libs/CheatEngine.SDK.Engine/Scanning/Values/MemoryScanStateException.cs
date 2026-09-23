using System;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     Thrown when a <see cref="MemoryScanSession" /> operation is not valid in its current
///     <see cref="MemoryScanState" />.
/// </summary>
public sealed class MemoryScanStateException : InvalidOperationException
{
	internal MemoryScanStateException(string operation, MemoryScanState state)
		: base("The memory-scan operation '" + operation + "' is not valid while the session is " + state + ".")
	{
		Operation = operation;
		State = state;
	}

	// A re-entrant call: work that Cheat Engine ran from inside the session's active CE call called back into it.
	internal MemoryScanStateException(string operation, MemoryScanState state, string activeOperation)
		: base("The memory-scan operation '" + operation + "' was refused because the session's '" + activeOperation +
		       "' operation is still inside a Cheat Engine call (the call came from work Cheat Engine ran during it).")
	{
		Operation = operation;
		State = state;
	}

	/// <summary>Gets the managed operation the caller attempted.</summary>
	public string Operation
	{
		get;
	}

	/// <summary>Gets the session state that rejected the operation.</summary>
	public MemoryScanState State
	{
		get;
	}
}
