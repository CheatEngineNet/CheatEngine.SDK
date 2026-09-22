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
