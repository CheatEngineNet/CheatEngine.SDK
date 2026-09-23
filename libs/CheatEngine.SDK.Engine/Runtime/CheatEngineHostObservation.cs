using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>A copied observation of facts about the Cheat Engine host process itself, independent of any target.</summary>
/// <remarks>
///     <para>
///         Each field comes from exactly one Cheat Engine global and nothing is inferred between them: in particular
///         <see cref="SystemArchitecture" /> never fills <see cref="CheatEngineIs64Bit" />. A <see langword="null" /> or
///         <c>Unknown</c> field means the global is absent (for <see cref="FileVersion" />, also that CE returned no
///         version because its version resource was unreadable).
///     </para>
///     <para>
///         Produced by <c>RuntimeHostOperations.ObserveHost</c>. The spike C3 values for CE 7.7.0.10621 x64 (Lua-only,
///         ObservedHost design input) are file version 7.7.0.10621, system architecture x86_64, 64-bit and Windows.
///     </para>
/// </remarks>
/// <param name="FileVersion">The complete file version from <c>getCheatEngineFileVersion</c>, or <see langword="null" />.</param>
/// <param name="SystemArchitecture">The host architecture from <c>getSystemArchitecture</c>, or unknown.</param>
/// <param name="CheatEngineIs64Bit">
///     Whether <c>cheatEngineIs64Bit</c> reported a 64-bit Cheat Engine, or
///     <see langword="null" />.
/// </param>
/// <param name="OperatingSystem">The operating system from <c>getOperatingSystem</c>, or unknown.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct CheatEngineHostObservation(
	CheatEngineVersion? FileVersion,
	CheatEngineArchitecture SystemArchitecture,
	bool? CheatEngineIs64Bit,
	CheatEngineOperatingSystem OperatingSystem);
