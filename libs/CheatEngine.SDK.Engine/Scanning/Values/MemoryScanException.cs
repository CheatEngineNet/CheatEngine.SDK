using System;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     A stable managed failure of a memory-scan operation: missing CE capability, protected Lua error, or an unexpected
///     host result. State violations use <see cref="MemoryScanStateException" /> instead.
/// </summary>
/// <remarks>
///     The exception preserves a <see cref="LuaException" /> as its inner cause for protected-call failures, but its own
///     message and <see cref="Operation" /> use SDK-owned stable identifiers rather than exposing Lua member names or
///     host-provided error text.
/// </remarks>
public sealed class MemoryScanException : InvalidOperationException
{
    internal MemoryScanException(MemoryScanFailureKind kind, string operation, string message,
        LuaException? innerException = null)
        : base(message, innerException)
    {
        FailureKind = kind;
        Operation = operation;
    }

    /// <summary>Gets the stable category of the failed operation.</summary>
    public MemoryScanFailureKind FailureKind { get; }

    /// <summary>Gets the stable SDK operation identifier that failed.</summary>
    public string Operation { get; }
}
