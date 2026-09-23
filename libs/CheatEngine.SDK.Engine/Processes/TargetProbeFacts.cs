using System;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>The Cheat Engine target facts that <see cref="TargetArchitectureProbe" /> can read, one flag per Lua global.</summary>
/// <remarks>
///     The probe always reads <see cref="SelectedProcess" /> first and last. The other facts are read in the fixed
///     order of their flag values, and only when the caller requests them, so an operation that needs the instruction
///     set never calls <c>getPointerSize</c>, <c>getABI</c> or <c>isConnectedToCEServer</c>.
/// </remarks>
[Flags]
internal enum TargetProbeFacts : byte
{
	/// <summary>No fact.</summary>
	None = 0,

	/// <summary><c>isConnectedToCEServer</c>: whether Cheat Engine is connected to a CEServer backend.</summary>
	CeServerConnection = 1,

	/// <summary><c>targetIs64Bit</c>: Cheat Engine's 64-bit process flag for the selected target.</summary>
	Bitness = 2,

	/// <summary><c>targetIsX86</c>: whether the selected target belongs to the x86 ISA family.</summary>
	X86Family = 4,

	/// <summary><c>targetIsArm</c>: whether the selected target belongs to the ARM ISA family.</summary>
	ArmFamily = 8,

	/// <summary><c>targetIsAndroid</c>: whether Cheat Engine reports an Android target.</summary>
	Android = 16,

	/// <summary><c>getABI</c>: the raw target ABI-family code.</summary>
	Abi = 32,

	/// <summary><c>getPointerSize</c>: Cheat Engine's configured pointer size, as a raw integer.</summary>
	ConfiguredPointerSize = 64,

	/// <summary><c>getOpenedProcessID</c>: the selected process identifier, read before and after every other fact.</summary>
	SelectedProcess = 128,

	/// <summary>The three facts that define an instruction profile.</summary>
	InstructionSet = Bitness | X86Family | ArmFamily,

	/// <summary>Every fact the probe can read after the first process-identifier read.</summary>
	AllTargetFacts = CeServerConnection | Bitness | X86Family | ArmFamily | Android | Abi | ConfiguredPointerSize
}
