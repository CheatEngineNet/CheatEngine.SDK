using System;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi;

/// <summary>
///     Process-architecture guard for the layouts in this assembly.
/// </summary>
/// <remarks>
///     <para>
///         The sizes and offsets documented on the structures of this assembly (and asserted by
///         <c>CheatEngine.SDK.Abi.Tests</c>) are the 64-bit ones. The structures themselves are declared with
///         pointer-sized fields, so they would also describe a 32-bit Cheat Engine, but that configuration is neither
///         tested nor supported.
///     </para>
///     <para>
///         Nothing in this assembly checks the architecture implicitly, and no type initializer throws: a caller that
///         wants the guarantee asks for it explicitly, at a point where it can still fail cleanly (for a plugin: inside
///         the bootstrap, where a failure becomes a return code instead of an exception crossing into native code).
///     </para>
/// </remarks>
public static class AbiArchitecture
{
	/// <summary>
	///     Gets a value indicating whether the current process has the architecture this assembly is validated for
	///     (x64).
	/// </summary>
	/// <remarks>Thread-safe, allocation-free, never throws.</remarks>
	public static bool IsSupported => IsSupportedArchitecture(RuntimeInformation.ProcessArchitecture);

	/// <summary>
	///     Throws when the current process does not have the architecture this assembly is validated for.
	/// </summary>
	/// <remarks>
	///     Thread-safe. Must not be called from a frame that native code called directly, unless that frame catches
	///     the exception: use <see cref="IsSupported" /> there.
	/// </remarks>
	/// <exception cref="PlatformNotSupportedException">The process is not an x64 process.</exception>
	public static void ThrowIfUnsupported()
	{
		ThrowIfUnsupported(RuntimeInformation.ProcessArchitecture);
	}

	/// <summary>The policy itself, separated from the process query so that every branch is testable on any machine.</summary>
	internal static bool IsSupportedArchitecture(Architecture architecture)
	{
		return architecture == Architecture.X64;
	}

	/// <summary>Throwing form of <see cref="IsSupportedArchitecture" />.</summary>
	internal static void ThrowIfUnsupported(Architecture architecture)
	{
		if (!IsSupportedArchitecture(architecture))
		{
			throw new PlatformNotSupportedException(
				$"CheatEngine.SDK.Abi is validated for x64 processes only; this process is {architecture}.");
		}
	}
}
