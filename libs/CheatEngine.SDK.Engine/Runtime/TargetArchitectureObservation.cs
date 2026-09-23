using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>
///     A copied observation of Cheat Engine's facts about its selected target: backend, bitness, ISA family, Android,
///     ABI and configured pointer size, each kept separate.
/// </summary>
/// <remarks>
///     <para>
///         Produced by <c>RuntimeProcessOperations.ObserveTargetArchitecture</c>, which reads the selected process
///         identifier before and after these facts in one Lua admission and never reads a fact when no target is
///         selected. Each fact comes from one Cheat Engine global; a <see langword="null" /> or <c>Unknown</c> fact
///         means its global is absent, never <see langword="false" />. The computed members below never fill a missing
///         fact from another one.
///     </para>
///     <para>
///         <see cref="Bitness" /> is Cheat Engine's 64-bit process flag (<c>targetIs64Bit</c>, CE's
///         <c>processhandler.is64Bit</c>; <c>setAssemblerMode</c> writes the same flag). CE's <c>readPointer</c>
///         follows it. <see cref="ConfiguredPointerSizeBytes" /> is a different fact: the per-attachment value of
///         <c>getPointerSize</c>, which <c>setPointerSize</c> can set to any integer and which (re)attaching resets.
///         Spike C3 D3 (CE 7.7.0.10621 x64, Lua-only, ObservedHost design input) observed a configured size of 4 on a
///         64-bit target while <c>readPointer</c> still read 8 bytes. Neither fact is derived from the other, from an
///         architecture, or from the plugin's <c>IntPtr.Size</c>.
///     </para>
///     <para>
///         The observation describes what Cheat Engine reports about the target, including a CEServer target. It is
///         not an incarnation proof: <c>Targets.TargetSelection</c> decides whether a local process identity can be
///         established. Consumers may construct values for tests or composition; a constructed value carries whatever
///         its creator asserts.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TargetArchitectureObservation
{
	/// <summary>Initializes a target observation from explicit facts.</summary>
	/// <param name="processId">The selected process identifier from <c>getOpenedProcessID</c>.</param>
	/// <param name="backend">The backend established from <c>isConnectedToCEServer</c>.</param>
	/// <param name="bitness">Cheat Engine's 64-bit process flag as a width: 8 bytes when set, 4 otherwise.</param>
	/// <param name="isX86Family"><c>targetIsX86</c>, or <see langword="null" /> when the global is absent.</param>
	/// <param name="isArmFamily"><c>targetIsArm</c>, or <see langword="null" /> when the global is absent.</param>
	/// <param name="isAndroid"><c>targetIsAndroid</c>, or <see langword="null" /> when the global is absent.</param>
	/// <param name="abiCode">The raw <c>getABI</c> integer, or <see langword="null" /> when the global is absent.</param>
	/// <param name="configuredPointerSizeBytes">
	///     The raw <c>getPointerSize</c> integer, any value Cheat Engine returned, or <see langword="null" /> when the
	///     global is absent.
	/// </param>
	public TargetArchitectureObservation(TargetProcessId processId, TargetBackend backend, PointerSize bitness,
		bool? isX86Family, bool? isArmFamily, bool? isAndroid, int? abiCode, int? configuredPointerSizeBytes)
	{
		ProcessId = processId;
		Backend = backend;
		Bitness = bitness;
		IsX86Family = isX86Family;
		IsArmFamily = isArmFamily;
		IsAndroid = isAndroid;
		AbiCode = abiCode;
		ConfiguredPointerSizeBytes = configuredPointerSizeBytes;
	}

	/// <summary>Gets the selected process identifier the facts were read for.</summary>
	public TargetProcessId ProcessId
	{
		get;
	}

	/// <summary>Gets the backend Cheat Engine uses for the target, or unknown when it cannot be established.</summary>
	public TargetBackend Backend
	{
		get;
	}

	/// <summary>
	///     Gets Cheat Engine's 64-bit process flag (<c>targetIs64Bit</c>) as a width; this is not the configured pointer
	///     size.
	/// </summary>
	public PointerSize Bitness
	{
		get;
	}

	/// <summary>
	///     Gets whether Cheat Engine reports the x86 ISA family (<c>targetIsX86</c>), or <see langword="null" /> when
	///     absent.
	/// </summary>
	/// <remarks>An x64 target is reported as the x86 family with the 64-bit flag set.</remarks>
	public bool? IsX86Family
	{
		get;
	}

	/// <summary>
	///     Gets whether Cheat Engine reports the ARM ISA family (<c>targetIsArm</c>), or <see langword="null" /> when
	///     absent.
	/// </summary>
	public bool? IsArmFamily
	{
		get;
	}

	/// <summary>
	///     Gets whether Cheat Engine reports an Android target (<c>targetIsAndroid</c>), or <see langword="null" /> when
	///     absent.
	/// </summary>
	/// <remarks><see langword="null" /> means the global is absent; it is never read as <see langword="false" />.</remarks>
	public bool? IsAndroid
	{
		get;
	}

	/// <summary>Gets the raw <c>getABI</c> integer, or <see langword="null" /> when the global is absent.</summary>
	/// <remarks>An undocumented code is kept here while <see cref="Abi" /> stays unknown.</remarks>
	public int? AbiCode
	{
		get;
	}

	/// <summary>Gets the raw <c>getPointerSize</c> integer, or <see langword="null" /> when the global is absent.</summary>
	/// <remarks>Any integer is kept, including values other than 4 and 8 that <c>setPointerSize</c> accepts (spike C3 D3b).</remarks>
	public int? ConfiguredPointerSizeBytes
	{
		get;
	}

	/// <summary>Gets the decoded ABI family, or unknown when <see cref="AbiCode" /> is absent or undocumented.</summary>
	public TargetAbi Abi =>
		AbiCode is int code && RuntimeInfo.TryDecodeTargetAbi(code, out TargetAbi abi) ? abi : TargetAbi.Unknown;

	/// <summary>
	///     Gets the configured pointer size when <see cref="ConfiguredPointerSizeBytes" /> is exactly 4 or 8; otherwise
	///     unknown.
	/// </summary>
	public PointerSize ConfiguredPointerSize => ConfiguredPointerSizeBytes switch
	{
		4 => PointerSize.Bit32,
		8 => PointerSize.Bit64,
		_ => PointerSize.Unknown
	};

	/// <summary>
	///     Gets the architecture derived by <see cref="RuntimeInfo.TryDeriveTargetArchitecture" /> when both family facts
	///     and the bitness are known; otherwise, or when the families are contradictory, unknown.
	/// </summary>
	public CheatEngineArchitecture Architecture =>
		IsX86Family is bool isX86 && IsArmFamily is bool isArm && Bitness.IsKnown &&
		RuntimeInfo.TryDeriveTargetArchitecture(isX86, isArm, Bitness == PointerSize.Bit64,
			out CheatEngineArchitecture architecture)
			? architecture
			: CheatEngineArchitecture.Unknown;

	/// <summary>
	///     Gets whether the raw configured pointer size differs from the bitness width (audit Q31.a), or
	///     <see langword="null" /> when either fact is unknown.
	/// </summary>
	public bool? ConfiguredPointerSizeDiffersFromBitness =>
		ConfiguredPointerSizeBytes is int configured && Bitness.IsKnown ? configured != Bitness.Bytes : null;
}
