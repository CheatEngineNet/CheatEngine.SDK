using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>A copied echo of one assembler call: the context sent to Cheat Engine and the size of its result.</summary>
/// <remarks>
///     <para>
///         The same instruction text assembles to different bytes for a different origin, jump preference or range-check
///         option, or for another target profile. This value keeps them together with the result so that bytes are never
///         reused in a context they were not produced for (audit ch.15, A15-04). It is filled on every outcome of
///         <see
///             cref="InstructionAssembler.TryAssemble(InstructionTargetProfile, string, Address, AssemblePreference, bool, System.Span{byte}, out InstructionAssembly)" />
///         :
///         the context fields always echo the request, while <see cref="Written" /> is non-zero only on success.
///     </para>
///     <para>It owns no Lua storage and no Cheat Engine object; it stays valid after the Lua runtime detaches.</para>
/// </remarks>
/// <param name="Target">The CE-selected process identifier of the profile the call was validated against.</param>
/// <param name="Profile">The instruction profile the call was validated against.</param>
/// <param name="Origin">The target address sent to CE as the instruction origin.</param>
/// <param name="Preference">The jump-encoding preference sent to CE.</param>
/// <param name="SkipRangeCheck">The range-check option sent to CE.</param>
/// <param name="Written">The number of bytes copied to the destination on success; zero for every other outcome.</param>
/// <param name="RequiredLength">The exact byte-table length when CE returned a valid table; zero otherwise.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct InstructionAssembly(
	TargetProcessId Target,
	InstructionProfile Profile,
	Address Origin,
	AssemblePreference Preference,
	bool SkipRangeCheck,
	int Written,
	int RequiredLength);
