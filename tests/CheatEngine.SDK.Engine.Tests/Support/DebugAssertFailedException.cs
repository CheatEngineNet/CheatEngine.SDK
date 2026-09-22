namespace CheatEngine.SDK.Engine.Tests.Support;

/// <summary>Thrown by <see cref="DebugAssertScope" /> in place of the process-ending failure of a Debug assertion.</summary>
public sealed class DebugAssertFailedException : Exception
{
	public DebugAssertFailedException()
	{
	}

	public DebugAssertFailedException(string message)
		: base(message)
	{
	}

	public DebugAssertFailedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
