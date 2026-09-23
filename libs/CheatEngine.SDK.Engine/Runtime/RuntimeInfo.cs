using System;

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>
///     An immutable Cheat Engine runtime snapshot: complete version, CE host and target architecture, CE's configured
///     pointer size, target ABI, and optional capability observations.
/// </summary>
/// <remarks>
///     <para>
///         The SDK produces a snapshot with <c>CheatEngine.SDK.Engine.Processes.RuntimeObservations.TryObserveRuntimeInfo</c>,
///         which fills <see cref="Host" /> and, when a target is selected, <see cref="Target" />; the legacy properties are
///         then derived from those observations without inference. A snapshot can also carry caller-supplied facts
///         through the legacy constructor, in which case <see cref="Host" /> and <see cref="Target" /> are
///         <see langword="null" />.
///     </para>
///     <para>
///         This type makes no runtime call and does not infer one fact from another. In particular,
///         <see cref="TargetArchitecture" /> stays <see cref="CheatEngineArchitecture.Unknown" /> until target probes
///         establish it, and <see cref="Capabilities" /> may contain unknown availability or contract fields.
///     </para>
/// </remarks>
public sealed class RuntimeInfo
{
	/// <summary>Initializes a runtime snapshot from explicit, caller-supplied facts.</summary>
	/// <param name="version">The complete Cheat Engine file version.</param>
	/// <param name="systemArchitecture">The CE host architecture reported by <c>getSystemArchitecture</c>.</param>
	/// <param name="targetArchitecture">The architecture established by target probes, or unknown.</param>
	/// <param name="pointerSize">
	///     The pointer size the caller attributes to this snapshot, or unknown. An SDK-produced snapshot uses Cheat
	///     Engine's configured pointer size (<c>getPointerSize</c>) here.
	/// </param>
	/// <param name="targetAbi">The ABI family reported by <c>getABI</c>, or unknown.</param>
	/// <param name="capabilities">The immutable optional-capability observations for this snapshot.</param>
	/// <exception cref="System.ArgumentNullException"><paramref name="capabilities" /> is <see langword="null" />.</exception>
	/// <remarks>This constructor records caller-supplied facts as-is; <see cref="Host" /> and <see cref="Target" /> stay <see langword="null" />.</remarks>
	public RuntimeInfo(
		CheatEngineVersion version,
		CheatEngineArchitecture systemArchitecture,
		CheatEngineArchitecture targetArchitecture,
		PointerSize pointerSize,
		TargetAbi targetAbi,
		RuntimeCapabilities capabilities)
	{
		Version = version;
		SystemArchitecture = systemArchitecture;
		TargetArchitecture = targetArchitecture;
		PointerSize = pointerSize;
		TargetAbi = targetAbi;
		Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
	}

	/// <summary>Initializes a runtime snapshot from separate host and target observations.</summary>
	/// <param name="host">The Cheat Engine host facts.</param>
	/// <param name="target">The selected target's facts, or <see langword="null" /> when no target facts were observed.</param>
	/// <param name="capabilities">The immutable optional-capability observations for this snapshot.</param>
	/// <exception cref="System.ArgumentNullException"><paramref name="capabilities" /> is <see langword="null" />.</exception>
	/// <remarks>
	///     The legacy properties are derived without inference: <see cref="Version" /> is the observed file version or
	///     the default value when it was not observed, <see cref="TargetArchitecture" /> is
	///     <see cref="TargetArchitectureObservation.Architecture" />, <see cref="PointerSize" /> is Cheat Engine's
	///     configured pointer size (<see cref="TargetArchitectureObservation.ConfiguredPointerSize" />, not the target
	///     bitness), and <see cref="TargetAbi" /> is <see cref="TargetArchitectureObservation.Abi" />; each is unknown
	///     without a target.
	/// </remarks>
	public RuntimeInfo(CheatEngineHostObservation host, TargetArchitectureObservation? target,
		RuntimeCapabilities capabilities)
	{
		Host = host;
		Target = target;
		Version = host.FileVersion ?? default;
		SystemArchitecture = host.SystemArchitecture;
		TargetArchitecture = target?.Architecture ?? CheatEngineArchitecture.Unknown;
		PointerSize = target?.ConfiguredPointerSize ?? PointerSize.Unknown;
		TargetAbi = target?.Abi ?? TargetAbi.Unknown;
		Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
	}

	/// <summary>Gets the Cheat Engine host observation of an SDK-produced snapshot, or <see langword="null" /> for caller-supplied facts.</summary>
	public CheatEngineHostObservation? Host
	{
		get;
	}

	/// <summary>
	///     Gets the selected target's observation of an SDK-produced snapshot, or <see langword="null" /> when no target
	///     was selected, its facts could not be read, or the snapshot holds caller-supplied facts.
	/// </summary>
	/// <remarks>The target bitness is <see cref="TargetArchitectureObservation.Bitness" />; it is not <see cref="PointerSize" />.</remarks>
	public TargetArchitectureObservation? Target
	{
		get;
	}

	/// <summary>
	///     Gets the complete CE file version of this snapshot; the default value means it was not observed (for an
	///     SDK-produced snapshot, <c>getCheatEngineFileVersion</c> was absent or returned no value).
	/// </summary>
	public CheatEngineVersion Version
	{
		get;
	}

	/// <summary>Gets the CE host architecture reported by CE's <c>getSystemArchitecture</c> global.</summary>
	public CheatEngineArchitecture SystemArchitecture
	{
		get;
	}

	/// <summary>Gets the target architecture established by target probes, or unknown.</summary>
	public CheatEngineArchitecture TargetArchitecture
	{
		get;
	}

	/// <summary>
	///     Gets Cheat Engine's configured pointer size (<c>getPointerSize</c>) when the SDK produced this snapshot, or the
	///     caller-supplied width; unknown when it was not observed or is not 4 or 8 bytes.
	/// </summary>
	/// <remarks>
	///     This is not the target bitness: Cheat Engine keeps the configured size separately, <c>setPointerSize</c> can
	///     change it, and <c>readPointer</c> follows the bitness instead (spike C3 D3). Read the bitness from
	///     <see cref="TargetArchitectureObservation.Bitness" /> through <see cref="Target" />.
	/// </remarks>
	public PointerSize PointerSize
	{
		get;
	}

	/// <summary>Gets the target ABI family reported by CE's <c>getABI</c> global, or unknown.</summary>
	public TargetAbi TargetAbi
	{
		get;
	}

	/// <summary>Gets the immutable optional-capability observations for this snapshot.</summary>
	public RuntimeCapabilities Capabilities
	{
		get;
	}

	/// <summary>Decodes a CE 7.7 <c>getSystemArchitecture</c> result: 0=i386, 1=x86_64, 2=arm32, 3=arm64.</summary>
	/// <param name="code">The raw CE Lua integer.</param>
	/// <param name="architecture">The decoded architecture, or <see cref="CheatEngineArchitecture.Unknown" />.</param>
	/// <returns><see langword="true" /> only for a documented CE 7.7 discriminant.</returns>
	public static bool TryDecodeSystemArchitecture(int code, out CheatEngineArchitecture architecture)
	{
		switch (code)
		{
			case 0:
				architecture = CheatEngineArchitecture.X86;
				return true;
			case 1:
				architecture = CheatEngineArchitecture.X64;
				return true;
			case 2:
				architecture = CheatEngineArchitecture.Arm32;
				return true;
			case 3:
				architecture = CheatEngineArchitecture.Arm64;
				return true;
			default:
				architecture = CheatEngineArchitecture.Unknown;
				return false;
		}
	}

	/// <summary>
	///     Derives a target architecture from Cheat Engine's ISA-family and bitness facts (<c>targetIsX86</c>,
	///     <c>targetIsArm</c> and <c>targetIs64Bit</c>) without using any pointer width.
	/// </summary>
	/// <param name="isX86Family">The value of <c>targetIsX86</c>.</param>
	/// <param name="isArmFamily">The value of <c>targetIsArm</c>.</param>
	/// <param name="is64Bit">The value of <c>targetIs64Bit</c>, Cheat Engine's 64-bit process flag.</param>
	/// <param name="architecture">
	///     <see cref="CheatEngineArchitecture.X64" /> or <see cref="CheatEngineArchitecture.X86" /> for the x86 family,
	///     <see cref="CheatEngineArchitecture.Arm64" /> or <see cref="CheatEngineArchitecture.Arm32" /> for the ARM
	///     family; <see cref="CheatEngineArchitecture.Unknown" /> when the families are contradictory.
	/// </param>
	/// <returns>
	///     <see langword="true" /> when exactly one family flag is set; <see langword="false" /> when both or neither
	///     are set.
	/// </returns>
	/// <remarks>
	///     <para>
	///         Cheat Engine reports x86-64 as the x86 family plus the 64-bit flag: an x64 target has
	///         <c>targetIsX86() == true</c> and <c>targetIs64Bit() == true</c>. That was observed on CE 7.7.0.10621 x64
	///         for an x64 and an x86 target (spike C3 D2, ObservedHost, Lua-only, 2026-09-22) and matches
	///         <c>TSystemArchitecture=(archX86=0, archArm=1)</c> with x86_64 setting <c>archX86</c> together with the
	///         64-bit flag (<c>ProcessHandlerUnit.pas:24</c>, <c>:115-138</c> at cheat-engine/cheat-engine@ec45d5f,
	///         ObservedSource). The 64-bit flag therefore never selects an ISA family by itself.
	///     </para>
	///     <para>
	///         This is a pure function. With no target selected Cheat Engine also reports x86 family plus 64-bit, so a
	///         caller must read <c>getOpenedProcessID</c> first and never derive an architecture for process identifier
	///         0. The mapping is fixture-tested (C1/C2); it is not a host qualification.
	///     </para>
	/// </remarks>
	public static bool TryDeriveTargetArchitecture(bool isX86Family, bool isArmFamily, bool is64Bit,
		out CheatEngineArchitecture architecture)
	{
		if (isX86Family == isArmFamily)
		{
			architecture = CheatEngineArchitecture.Unknown;
			return false;
		}

		if (isX86Family)
		{
			architecture = is64Bit ? CheatEngineArchitecture.X64 : CheatEngineArchitecture.X86;
			return true;
		}

		architecture = is64Bit ? CheatEngineArchitecture.Arm64 : CheatEngineArchitecture.Arm32;
		return true;
	}

	/// <summary>Decodes a CE 7.7 <c>getOperatingSystem</c> result: 0=Windows, 1=macOS, 2=Linux.</summary>
	/// <param name="code">The raw CE Lua integer.</param>
	/// <param name="operatingSystem">The decoded operating system, or <see cref="CheatEngineOperatingSystem.Unknown" />.</param>
	/// <returns><see langword="true" /> only for a code documented by the CE 7.7 Lua catalogue.</returns>
	/// <remarks>
	///     The codes follow <c>celua.txt:14</c> (CE 7.7.0.10621 x64, ExactInstalledFile). The public CE source at ec45d5f
	///     returns 1 for every non-Windows build (<c>LuaHandler.pas:14863-14867</c>, ObservedSource), so a non-Windows
	///     code is catalogue evidence only; only 0 was observed on the qualified profile (spike C3).
	/// </remarks>
	public static bool TryDecodeOperatingSystem(int code, out CheatEngineOperatingSystem operatingSystem)
	{
		switch (code)
		{
			case 0:
				operatingSystem = CheatEngineOperatingSystem.Windows;
				return true;
			case 1:
				operatingSystem = CheatEngineOperatingSystem.MacOS;
				return true;
			case 2:
				operatingSystem = CheatEngineOperatingSystem.Linux;
				return true;
			default:
				operatingSystem = CheatEngineOperatingSystem.Unknown;
				return false;
		}
	}

	/// <summary>
	///     Splits the packed integer that <c>getCheatEngineFileVersion</c> returns first into a complete file version:
	///     major, minor, release and build, 16 bits each from the most significant.
	/// </summary>
	/// <param name="packed">The non-negative packed Lua integer, for example <c>0x700070000297D</c> for 7.7.0.10621.</param>
	/// <param name="version">The four components, or the default value when <paramref name="packed" /> is negative.</param>
	/// <returns><see langword="false" /> for a negative value, which no 16-bit major component can produce.</returns>
	/// <remarks>
	///     The layout (<c>major shl 48 or minor shl 32 or release shl 16 or build</c>) was observed on CE 7.7.0.10621 x64
	///     (spike C3 D5, Lua-only, ObservedHost design input) and matches <c>lua_getFileVersion</c> in the public source
	///     (<c>LuaHandler.pas:13271-13318</c> at ec45d5f, ObservedSource). This never converts <c>getCEVersion</c>'s
	///     floating-point value.
	/// </remarks>
	public static bool TryDecodeFileVersion(long packed, out CheatEngineVersion version)
	{
		if (packed < 0)
		{
			version = default;
			return false;
		}

		version = new CheatEngineVersion((int) ((packed >> 48) & 0xFFFF), (int) ((packed >> 32) & 0xFFFF),
			(int) ((packed >> 16) & 0xFFFF), (int) (packed & 0xFFFF));
		return true;
	}

	/// <summary>Decodes a CE 7.7 <c>getABI</c> result: 0 for Windows and 1 for Unix/Linux.</summary>
	/// <param name="code">The raw CE Lua integer.</param>
	/// <param name="abi">The decoded ABI family, or <see cref="TargetAbi.Unknown" />.</param>
	/// <returns><see langword="true" /> only for a documented CE 7.7 discriminant.</returns>
	public static bool TryDecodeTargetAbi(int code, out TargetAbi abi)
	{
		switch (code)
		{
			case 0:
				abi = TargetAbi.Windows;
				return true;
			case 1:
				abi = TargetAbi.Unix;
				return true;
			default:
				abi = TargetAbi.Unknown;
				return false;
		}
	}
}
