using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>The jump-encoding preference Cheat Engine's <c>assemble</c> global accepts as its third argument.</summary>
/// <remarks>
///     The values mirror Cheat Engine's own constants, <c>TassemblerPreference=(apNone, apShort, apLong, apFar)</c>
///     (<c>Assemblerunit.pas:2712-2714</c> at cheat-engine/cheat-engine@ec45d5f, ObservedSource) and the CE 7.7 Lua
///     catalogue (<c>celua.txt:236-238</c>, ExactInstalledFile). They are passed to CE unchanged and frozen by a test. A
///     preference changes which encoding CE picks for a jump or call; it is recorded in
///     <see cref="InstructionAssembly.Preference" /> so that assembled bytes are never separated from the context that
///     produced them (audit A15-04, A15-18).
/// </remarks>
[SuppressMessage("Naming", "CA1720:Identifiers should not contain type names",
	Justification =
		"The members mirror Cheat Engine's apShort/apLong/apFar jump preferences; renaming them would hide the CE names they mirror.")]
public enum AssemblePreference : byte
{
	/// <summary>No preference: Cheat Engine chooses the encoding (<c>apNone</c>, 0).</summary>
	None = 0,

	/// <summary>Prefer the short encoding (<c>apShort</c>, 1).</summary>
	Short = 1,

	/// <summary>Prefer the long encoding (<c>apLong</c>, 2).</summary>
	Long = 2,

	/// <summary>Prefer the far encoding (<c>apFar</c>, 3).</summary>
	Far = 3
}
