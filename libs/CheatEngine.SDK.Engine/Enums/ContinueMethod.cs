namespace CheatEngine.SDK.Engine.Enums;

/// <summary>
///     How the debugger resumes from a breakpoint (<c>co_*</c> in <c>defines.lua</c>): the argument of
///     <c>debug_continueFromBreakpoint</c>.
/// </summary>
/// <remarks>
///     Values verified against <c>defines.lua</c> of Cheat Engine 7.7.0.10621. The CE names use an underscore (
///     <c>co_run</c>), unlike every other define group.
/// </remarks>
public enum ContinueMethod
{
	/// <summary>Resume normally. CE: <c>co_run</c>.</summary>
	Run = 0,

	/// <summary>Execute one instruction, following a call into the callee. CE: <c>co_stepinto</c>.</summary>
	StepInto = 1,

	/// <summary>Execute one instruction, running a call to completion. CE: <c>co_stepover</c>.</summary>
	StepOver = 2
}
