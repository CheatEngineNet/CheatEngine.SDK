using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     A scalar copy of the fixed header of a borrowed Windows <c>DEBUG_EVENT</c>.
/// </summary>
/// <remarks>
///     The native pointer, its union payload, and any handles or pointers in that payload remain borrowed for the
///     unmanaged callback only. This value contains just the three documented DWORD header fields and can safely be
///     retained by a synchronous handler or an observation buffer.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct DebugEventObservation
{
    /// <summary>
    ///     Initializes a new copied debug-event observation.
    /// </summary>
    /// <param name="sequenceNumber">The SDK-local callback sequence number.</param>
    /// <param name="eventCode">The Windows debug-event code.</param>
    /// <param name="processId">The debuggee process identifier from the event header.</param>
    /// <param name="threadId">The debuggee thread identifier from the event header.</param>
    public DebugEventObservation(long sequenceNumber, uint eventCode, uint processId, uint threadId)
    {
        SequenceNumber = sequenceNumber;
        EventCode = eventCode;
        ProcessId = processId;
        ThreadId = threadId;
    }

    /// <summary>The SDK-local sequence number assigned while the callback was admitted.</summary>
    public readonly long SequenceNumber;

    /// <summary>The Windows debug-event code copied from the native header.</summary>
    public readonly uint EventCode;

    /// <summary>The debuggee process identifier copied from the native header.</summary>
    public readonly uint ProcessId;

    /// <summary>The debuggee thread identifier copied from the native header.</summary>
    public readonly uint ThreadId;
}
