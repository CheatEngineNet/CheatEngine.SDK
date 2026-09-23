using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Explicit target-architecture and address-width facts observed for one instruction operation.</summary>
/// <remarks>
///     <para>
///         This is a validation contract, not a command that reconfigures Cheat Engine's ambient assembler or
///         disassembler. <see cref="InstructionProfiles.TryObserveCurrent" /> obtains the facts from CE's target
///         probes: the ISA family from <c>targetIsX86</c>/<c>targetIsArm</c> and the width from Cheat Engine's 64-bit
///         process flag <c>targetIs64Bit</c>. On Cheat Engine an x64 target is the x86 family with the 64-bit flag set.
///         The SDK never substitutes <see cref="System.IntPtr.Size" />, the x64 CE-host width, CE's configured pointer
///         size (<c>getPointerSize</c>), or a module bitness flag for this profile.
///     </para>
///     <para>
///         The mapping follows <see cref="Runtime.RuntimeInfo.TryDeriveTargetArchitecture" /> (spike C3 D2, ObservedHost,
///         Lua-only design input, and CE source ec45d5f, ObservedSource). Fixture tests cover it at C1/C2; it is not a
///         live host qualification. A profile rejects only inconsistent values and over-wide addresses before Lua is
///         entered; it does not claim target identity, relocation support, or host qualification.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct InstructionProfile
{
	internal InstructionProfile(CheatEngineArchitecture architecture, PointerSize addressWidth)
	{
		Architecture = architecture;
		AddressWidth = addressWidth;
	}

	/// <summary>Gets the observed target instruction architecture.</summary>
	public CheatEngineArchitecture Architecture
	{
		get;
	}

	/// <summary>Gets the observed target pointer and instruction-address width.</summary>
	public PointerSize AddressWidth
	{
		get;
	}

	/// <summary>Gets the conventional x86 instruction profile.</summary>
	public static InstructionProfile X86
	{
		get;
	} = new(CheatEngineArchitecture.X86, PointerSize.Bit32);

	/// <summary>Gets the conventional x64 instruction profile.</summary>
	public static InstructionProfile X64
	{
		get;
	} = new(CheatEngineArchitecture.X64, PointerSize.Bit64);

	/// <summary>Gets the conventional 32-bit ARM instruction profile.</summary>
	public static InstructionProfile Arm32
	{
		get;
	} = new(CheatEngineArchitecture.Arm32, PointerSize.Bit32);

	/// <summary>Gets the conventional 64-bit ARM instruction profile.</summary>
	public static InstructionProfile Arm64
	{
		get;
	} = new(CheatEngineArchitecture.Arm64, PointerSize.Bit64);

	/// <summary>Gets whether the architecture and width form one supported instruction profile.</summary>
	public bool IsValid => Architecture switch
	{
		CheatEngineArchitecture.X86 or CheatEngineArchitecture.Arm32 => AddressWidth == PointerSize.Bit32,
		CheatEngineArchitecture.X64 or CheatEngineArchitecture.Arm64 => AddressWidth == PointerSize.Bit64,
		_ => false
	};

	internal InstructionOperationStatus Validate(Address address)
	{
		if (!IsValid)
		{
			return InstructionOperationStatus.InvalidProfile;
		}

		return AddressWidth == PointerSize.Bit32 && address.Value > uint.MaxValue
			? InstructionOperationStatus.AddressExceedsProfileWidth
			: InstructionOperationStatus.Success;
	}
}
