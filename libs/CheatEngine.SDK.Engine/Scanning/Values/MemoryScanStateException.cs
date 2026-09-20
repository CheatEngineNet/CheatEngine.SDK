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

    /// <summary>Gets the managed operation the caller attempted.</summary>
    public string Operation { get; }

    /// <summary>Gets the session state that rejected the operation.</summary>
    public MemoryScanState State { get; }
}
