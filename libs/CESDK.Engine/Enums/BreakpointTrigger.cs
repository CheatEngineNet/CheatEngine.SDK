namespace CESDK.Engine.Enums;

/// <summary>
///     What kind of access fires a breakpoint (<c>bpt*</c> in <c>defines.lua</c>): the <c>trigger</c> argument of
///     <c>debug_setBreakpoint</c> and <c>debug_setBreakpointForThread</c>. With <see cref="Execute" /> the size
///     argument is ignored.
/// </summary>
/// <remarks>Values verified against <c>defines.lua</c> of Cheat Engine 7.7.0.10621.</remarks>
public enum BreakpointTrigger
{
    /// <summary>The instruction at the address is executed; the default. CE: <c>bptExecute</c>.</summary>
    Execute = 0,

    /// <summary>The range is read or written. CE: <c>bptAccess</c>.</summary>
    Access = 1,

    /// <summary>The range is written. CE: <c>bptWrite</c>.</summary>
    Write = 2
}
