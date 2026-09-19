namespace CheatEngine.SDK.Engine.Enums;

/// <summary>
///     How the debugger implements a breakpoint (<c>bpm*</c> in <c>defines.lua</c>): the <c>breakpointmethod</c>
///     argument of <c>debug_setBreakpoint</c> and <c>debug_setBreakpointForThread</c>.
/// </summary>
/// <remarks>Values verified against <c>defines.lua</c> of Cheat Engine 7.7.0.10621.</remarks>
public enum BreakpointMethod
{
    /// <summary>A software breakpoint: the instruction is replaced by <c>int3</c>. Execute triggers only. CE: <c>bpmInt3</c>.</summary>
    Int3 = 0,

    /// <summary>A hardware breakpoint in a debug register: at most four, any trigger. CE: <c>bpmDebugRegister</c>.</summary>
    DebugRegister = 1,

    /// <summary>A page-protection exception: no register limit, slower. CE: <c>bpmException</c>.</summary>
    Exception = 2
}
