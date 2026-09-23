using System;

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>
///     An immutable, caller-supplied Cheat Engine runtime snapshot: complete version, CE host and target architecture,
///     pointer size, target ABI, and optional capability observations.
/// </summary>
/// <remarks>
///     This type makes no runtime call and does not infer one fact from another. In particular,
///     <see cref="TargetArchitecture" /> stays <see cref="CheatEngineArchitecture.Unknown" /> until target probes
///     establish it, and <see cref="Capabilities" /> may contain unknown availability or contract fields.
/// </remarks>
public sealed class RuntimeInfo
{
	/// <summary>Initializes a runtime snapshot from explicit observations.</summary>
	/// <param name="version">The complete Cheat Engine file version.</param>
	/// <param name="systemArchitecture">The CE host architecture reported by <c>getSystemArchitecture</c>.</param>
	/// <param name="targetArchitecture">The architecture established by target probes, or unknown.</param>
	/// <param name="pointerSize">The observed pointer width for the applicable process context, or unknown.</param>
	/// <param name="targetAbi">The ABI family reported by <c>getABI</c>, or unknown.</param>
	/// <param name="capabilities">The immutable optional-capability observations for this snapshot.</param>
	/// <exception cref="System.ArgumentNullException"><paramref name="capabilities" /> is <see langword="null" />.</exception>
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

	/// <summary>Gets the complete CE file version supplied for this snapshot.</summary>
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

	/// <summary>Gets the observed pointer width for the applicable process context, or unknown.</summary>
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
