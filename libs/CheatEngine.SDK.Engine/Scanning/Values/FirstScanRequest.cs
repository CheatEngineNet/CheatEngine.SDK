using System;

using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     The fourteen positional arguments of Cheat Engine 7.7's
///     <c>
///         MemScan.firstScan(scanoption, vartype, roundingtype, input1, input2, startAddress, stopAddress,
///         protectionflags, alignmenttype, alignmentparam, isHexadecimalInput, isNotABinaryString, isunicodescan,
///         iscasesensitive)
///     </c>
///     method.
/// </summary>
/// <remarks>
///     <para>
///         The exact order is documented in CE 7.7.0.10621 <c>celua.txt</c> lines 2568-2613. Start and stop are modelled
///         as target <see cref="Address" /> values even though the Lua document calls them <c>integer</c>: a 64-bit
///         target address must not be narrowed to a managed <see cref="int" />. The session always pushes them as Lua
///         integers, bit for bit (an address at or above 2^63 becomes a negative Lua integer), never as strings, which CE
///         would resolve through its symbol handler. This value intentionally does not validate every semantic
///         combination CE may accept; the session validates the documented first-scan option set (including CE's
///         non-contiguous <c>vtGrouped</c> value) and non-null string values.
///     </para>
///     <para>
///         Range semantics observed on the pinned profile <c>ce-7.7.0.10621-x64-managed-hostfxr</c> (Lua-only host
///         observation, spike 2026-09-22, decision D4; not a C3 receipt): <see cref="StopAddress" /> is an exclusive
///         bound and a match is reported only when it fits entirely below it; <see cref="StartAddress" /> is not
///         byte-exact, so a match that begins slightly before it can be reported and a caller that needs an exact start
///         must post-filter the returned addresses. An empty or inverted range is not refused by CE (it reports zero
///         results with a misleading error text), which is why <see cref="ByteArray(string, Address, Address)" />
///         refuses it before any CE call.
///     </para>
/// </remarks>
public readonly struct FirstScanRequest
{
	/// <summary>Initializes a complete first-scan request.</summary>
	/// <param name="scanOption">The initial comparison mode.</param>
	/// <param name="variableType">The value type to scan.</param>
	/// <param name="roundingType">The floating-point comparison rule.</param>
	/// <param name="input1">The primary scan text; use an empty string when the option does not need it.</param>
	/// <param name="input2">The secondary scan text; use an empty string when the option does not need it.</param>
	/// <param name="startAddress">
	///     The lower target-address bound. Not byte-exact on the pinned CE 7.7 profile: post-filter results that
	///     must begin at or above it.
	/// </param>
	/// <param name="stopAddress">
	///     The exclusive upper target-address bound: on the pinned CE 7.7 profile a match is reported only when it
	///     fits entirely below it.
	/// </param>
	/// <param name="protectionFlags">The CE protection-flags text, such as <c>+W-C</c>.</param>
	/// <param name="fastScanMethod">The address-alignment rule.</param>
	/// <param name="alignmentParameter">The alignment rule's CE string parameter.</param>
	/// <param name="isHexadecimalInput">Whether CE interprets the inputs as hexadecimal text.</param>
	/// <param name="isNotBinaryString">Whether a binary type's input is decimal rather than a bit string.</param>
	/// <param name="isUnicodeScan">Whether a string scan uses UTF-16 rather than CE's normal UTF-8 mode.</param>
	/// <param name="isCaseSensitive">Whether a string scan matches case.</param>
	public FirstScanRequest(
		ScanOption scanOption,
		VariableType variableType,
		RoundingType roundingType,
		string input1,
		string input2,
		Address startAddress,
		Address stopAddress,
		string protectionFlags,
		FastScanMethod fastScanMethod,
		string alignmentParameter,
		bool isHexadecimalInput,
		bool isNotBinaryString,
		bool isUnicodeScan,
		bool isCaseSensitive)
	{
		ScanOption = scanOption;
		VariableType = variableType;
		RoundingType = roundingType;
		Input1 = input1;
		Input2 = input2;
		StartAddress = startAddress;
		StopAddress = stopAddress;
		ProtectionFlags = protectionFlags;
		FastScanMethod = fastScanMethod;
		AlignmentParameter = alignmentParameter;
		IsHexadecimalInput = isHexadecimalInput;
		IsNotBinaryString = isNotBinaryString;
		IsUnicodeScan = isUnicodeScan;
		IsCaseSensitive = isCaseSensitive;
	}

	/// <summary>Gets the CE first-scan comparison mode.</summary>
	public ScanOption ScanOption
	{
		get;
	}

	/// <summary>Gets the CE value type.</summary>
	public VariableType VariableType
	{
		get;
	}

	/// <summary>Gets the CE floating-point rounding rule.</summary>
	public RoundingType RoundingType
	{
		get;
	}

	/// <summary>Gets the primary scan text.</summary>
	public string Input1
	{
		get;
	}

	/// <summary>Gets the secondary scan text.</summary>
	public string Input2
	{
		get;
	}

	/// <summary>
	///     Gets the lower target-address bound. On the pinned CE 7.7.0.10621 x64 profile it is not byte-exact: a match
	///     beginning slightly before it can be reported, so a caller that needs an exact start post-filters the results
	///     (host observation, spike D4.2).
	/// </summary>
	public Address StartAddress
	{
		get;
	}

	/// <summary>
	///     Gets the exclusive upper target-address bound. On the pinned CE 7.7.0.10621 x64 profile a match is reported
	///     only when it fits entirely below this address (host observation, spike D4.1).
	/// </summary>
	public Address StopAddress
	{
		get;
	}

	/// <summary>Gets CE's protection-flags text.</summary>
	public string ProtectionFlags
	{
		get;
	}

	/// <summary>Gets CE's fast-scan alignment method.</summary>
	public FastScanMethod FastScanMethod
	{
		get;
	}

	/// <summary>Gets CE's string alignment parameter.</summary>
	public string AlignmentParameter
	{
		get;
	}

	/// <summary>Gets whether CE parses the input text as hexadecimal.</summary>
	public bool IsHexadecimalInput
	{
		get;
	}

	/// <summary>Gets whether a binary input is decimal rather than a bit string.</summary>
	public bool IsNotBinaryString
	{
		get;
	}

	/// <summary>Gets whether a string scan uses UTF-16.</summary>
	public bool IsUnicodeScan
	{
		get;
	}

	/// <summary>Gets whether a string scan is case-sensitive.</summary>
	public bool IsCaseSensitive
	{
		get;
	}

	/// <summary>Builds an exact-value request over the full 64-bit target address range.</summary>
	/// <param name="variableType">The value type to scan.</param>
	/// <param name="input">The exact value text.</param>
	/// <returns>
	///     A request using CE's normal rounded, non-aligned and non-hexadecimal settings, from address zero to the
	///     largest 64-bit address as its stop bound.
	/// </returns>
	public static FirstScanRequest ExactValue(VariableType variableType, string input)
	{
		return new FirstScanRequest(
			ScanOption.ExactValue,
			variableType,
			RoundingType.Rounded,
			input,
			string.Empty,
			Address.Zero,
			new Address(ulong.MaxValue),
			string.Empty,
			FastScanMethod.NotAligned,
			string.Empty,
			false,
			false,
			false,
			false);
	}

	/// <summary>
	///     Builds an exhaustive hexadecimal byte-array first scan over <c>[startAddress, stopAddress)</c> with CE's
	///     "find everything" protection string and no alignment.
	/// </summary>
	/// <param name="pattern">
	///     CE's byte-array pattern text (for example <c>48 8B ?? 89</c>), passed without normalization as the first input.
	/// </param>
	/// <param name="startAddress">The lower bound; not byte-exact on the pinned CE 7.7 profile (post-filter results).</param>
	/// <param name="stopAddress">The exclusive upper bound; a match is reported only when it fits entirely below it.</param>
	/// <returns>
	///     The fourteen CE positions <c>(soExactValue, vtByteArray, rtRounded, pattern, "", startAddress, stopAddress, "",
	///     fsmNotAligned, "", true, false, false, false)</c>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="stopAddress" /> is not greater than <paramref name="startAddress" />: an empty or inverted
	///     range is refused before any CE call.
	/// </exception>
	/// <remarks>
	///     The empty protection string is CE's "find everything" value (<c>celua.txt</c> line 847). This request does not
	///     post-filter: the bounded route of <see cref="Aob.AobScanner" /> does, and a direct
	///     <see cref="MemoryScanSession" /> user must drop results below <paramref name="startAddress" /> itself.
	/// </remarks>
	public static FirstScanRequest ByteArray(string pattern, Address startAddress, Address stopAddress)
	{
		return ByteArray(pattern, startAddress, stopAddress, string.Empty, FastScanMethod.NotAligned, string.Empty);
	}

	/// <summary>
	///     Builds an exhaustive hexadecimal byte-array first scan over <c>[startAddress, stopAddress)</c> with explicit
	///     CE protection and alignment arguments.
	/// </summary>
	/// <param name="pattern">CE's byte-array pattern text, passed without normalization as the first input.</param>
	/// <param name="startAddress">The lower bound; not byte-exact on the pinned CE 7.7 profile (post-filter results).</param>
	/// <param name="stopAddress">The exclusive upper bound; a match is reported only when it fits entirely below it.</param>
	/// <param name="protectionFlags">CE's protection-flags text; the empty string means "find everything".</param>
	/// <param name="fastScanMethod">The address-alignment rule.</param>
	/// <param name="alignmentParameter">The alignment rule's CE string parameter; empty for no alignment.</param>
	/// <returns>
	///     The fourteen CE positions <c>(soExactValue, vtByteArray, rtRounded, pattern, "", startAddress, stopAddress,
	///     protectionFlags, fastScanMethod, alignmentParameter, true, false, false, false)</c>.
	/// </returns>
	/// <exception cref="ArgumentNullException">A string argument is <see langword="null" />.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="stopAddress" /> is not greater than <paramref name="startAddress" />: an empty or inverted
	///     range is refused before any CE call.
	/// </exception>
	public static FirstScanRequest ByteArray(string pattern, Address startAddress, Address stopAddress,
		string protectionFlags, FastScanMethod fastScanMethod, string alignmentParameter)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(protectionFlags);
		ArgumentNullException.ThrowIfNull(alignmentParameter);
		if (stopAddress <= startAddress)
		{
			throw new ArgumentOutOfRangeException(nameof(stopAddress), stopAddress,
				"A byte-array scan range must be non-empty: the exclusive stop address must be greater than the start address.");
		}

		return new FirstScanRequest(
			ScanOption.ExactValue,
			VariableType.ByteArray,
			RoundingType.Rounded,
			pattern,
			string.Empty,
			startAddress,
			stopAddress,
			protectionFlags,
			fastScanMethod,
			alignmentParameter,
			true,
			false,
			false,
			false);
	}
}
