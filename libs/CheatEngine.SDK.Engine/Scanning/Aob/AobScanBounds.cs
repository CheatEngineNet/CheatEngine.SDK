using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>
///     A non-empty, half-open target-address range <c>[Start, Stop)</c>: the CE work limit of
///     <see
///         cref="AobScanner.TryScanWithinBounds(string, AobScanBounds, AobScanOptions, System.Span{Address}, System.Threading.CancellationToken)" />
///     .
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Stop" /> is exclusive and is passed to CE as the MemScan stop address: on the pinned profile
///         <c>ce-7.7.0.10621-x64-managed-hostfxr</c>, CE reports a match only when it fits entirely below it (Lua-only
///         host
///         observation, spike 2026-09-22, D4.1). <see cref="Start" /> is inclusive for the caller, but CE does not treat
///         it
///         byte-exactly (D4.2): the bounded route drops, and counts, every address CE returns below it.
///     </para>
///     <para>
///         The name deliberately differs from the Client's <c>AobScanRange</c>, whose end is an inclusive filter on the
///         match start: translate between the two rather than passing one for the other. Instances come only from
///         <see cref="TryCreate" /> and <see cref="TryFromModule" />; the default value is invalid, and an invalid value
///         is refused before any CE call.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AobScanBounds
{
	private AobScanBounds(Address start, Address stop)
	{
		Start = start;
		Stop = stop;
	}

	/// <summary>Gets the inclusive lower bound. The bounded route post-filters CE results against it.</summary>
	public Address Start
	{
		get;
	}

	/// <summary>Gets the exclusive upper bound: the CE work limit, passed to CE as the MemScan stop address.</summary>
	public Address Stop
	{
		get;
	}

	/// <summary>Gets the number of bytes in <c>[Start, Stop)</c>, or zero for an invalid value.</summary>
	public ulong Length => IsValid ? Stop.Value - Start.Value : 0;

	/// <summary>Gets whether the range is non-empty. The default value is not.</summary>
	public bool IsValid => Start < Stop;

	/// <summary>Gets whether an address lies in <c>[Start, Stop)</c>.</summary>
	/// <param name="address">The target address to test.</param>
	/// <returns>
	///     <see langword="true" /> when <paramref name="address" /> is at or above <see cref="Start" /> and below
	///     <see cref="Stop" />.
	/// </returns>
	public bool Contains(Address address)
	{
		return address >= Start && address < Stop;
	}

	/// <summary>Creates the half-open range <c>[start, stop)</c>.</summary>
	/// <param name="start">The inclusive lower bound.</param>
	/// <param name="stop">The exclusive upper bound.</param>
	/// <param name="bounds">The range when the method returns <see langword="true" />; otherwise the invalid default.</param>
	/// <returns>
	///     <see langword="false" /> when the range is empty or inverted (<paramref name="stop" /> not above
	///     <paramref name="start" />).
	/// </returns>
	public static bool TryCreate(Address start, Address stop, out AobScanBounds bounds)
	{
		if (stop <= start)
		{
			bounds = default;
			return false;
		}

		bounds = new AobScanBounds(start, stop);
		return true;
	}

	/// <summary>
	///     Creates the range <c>[BaseAddress, BaseAddress + ImageSize)</c> of a module snapshot, which is CE's own module
	///     convention for a stop address.
	/// </summary>
	/// <param name="module">A module snapshot, for example from <see cref="EngineInspection" />.</param>
	/// <param name="bounds">The module range when the method returns <see langword="true" />; otherwise the invalid default.</param>
	/// <returns>
	///     <see langword="false" /> when the module has no reported image size, a zero image size, or an end beyond the
	///     64-bit address space. No CE call is made.
	/// </returns>
	/// <remarks>
	///     The snapshot can be stale: a module unloaded or reloaded after it was enumerated makes these bounds describe
	///     memory that no longer belongs to it.
	/// </remarks>
	public static bool TryFromModule(in ModuleInfo module, out AobScanBounds bounds)
	{
		if (module.ImageSize is not { } size || size.Value == 0 ||
		    size.Value > ulong.MaxValue - module.BaseAddress.Value)
		{
			bounds = default;
			return false;
		}

		bounds = new AobScanBounds(module.BaseAddress, new Address(module.BaseAddress.Value + size.Value));
		return true;
	}
}
