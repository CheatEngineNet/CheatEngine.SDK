using System.Diagnostics;

namespace CheatEngine.SDK.Engine.Tests.Support;

/// <summary>
///     Turns a failed <see cref="Debug.Assert(bool)" /> into a <see cref="DebugAssertFailedException" /> for the duration
///     of a test, so that a Debug-only guard of the SDK can be observed on any thread instead of ending the process:
///     the <see cref="DefaultTraceListener" /> fails fast when no debugger is attached, so it is taken out of
///     <see cref="Trace.Listeners" /> (the documented way to change what an assertion does) and a throwing listener put
///     in its place. Process-wide, like the runtime it observes; the assembly runs sequentially. The previous listeners
///     are restored on dispose.
/// </summary>
internal sealed class DebugAssertScope : IDisposable
{
    private readonly TraceListener[] _previous;

    public DebugAssertScope()
    {
        _previous = new TraceListener[Trace.Listeners.Count];
        Trace.Listeners.CopyTo(_previous, 0);
        Trace.Listeners.Clear();
        Trace.Listeners.Add(new ThrowingListener());
    }

    public void Dispose()
    {
        Trace.Listeners.Clear();
        Trace.Listeners.AddRange(_previous);
    }

    private sealed class ThrowingListener : TraceListener
    {
        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
        }

        public override void Fail(string? message)
        {
            throw new DebugAssertFailedException(string.IsNullOrEmpty(message) ? "A Debug assertion failed." : message);
        }

        public override void Fail(string? message, string? detailMessage)
        {
            throw new DebugAssertFailedException(string.IsNullOrEmpty(message) ? "A Debug assertion failed." : message);
        }
    }
}
