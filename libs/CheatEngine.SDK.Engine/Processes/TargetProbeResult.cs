using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>The copied facts and status of one <see cref="TargetArchitectureProbe" /> observation.</summary>
/// <remarks>
///     A <see langword="null" /> fact was either not requested, not reached, or its global was absent;
///     <see cref="Resolved" /> and <see cref="Absent" /> tell those cases apart. The facts are kept on every exit path
///     so that a caller can report which globals it probed, but only a <see cref="TargetProbeStatus.Success" /> result
///     attributes them to <see cref="ProcessId" />.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct TargetProbeResult
{
	internal TargetProbeResult(TargetProbeStatus status, LuaStatus luaStatus, int processId,
		TargetProbeFacts resolved, TargetProbeFacts absent, bool? isConnectedToCeServer, bool? is64Bit,
		bool? isX86Family, bool? isArmFamily, bool? isAndroid, int? abiCode, int? configuredPointerSizeBytes)
	{
		Status = status;
		LuaStatus = luaStatus;
		ProcessId = processId;
		Resolved = resolved;
		Absent = absent;
		IsConnectedToCeServer = isConnectedToCeServer;
		Is64Bit = is64Bit;
		IsX86Family = isX86Family;
		IsArmFamily = isArmFamily;
		IsAndroid = isAndroid;
		AbiCode = abiCode;
		ConfiguredPointerSizeBytes = configuredPointerSizeBytes;
	}

	/// <summary>Gets the result category.</summary>
	internal TargetProbeStatus Status
	{
		get;
	}

	/// <summary>Gets the protected Lua status for <see cref="TargetProbeStatus.LuaFailure" />; otherwise OK.</summary>
	internal LuaStatus LuaStatus
	{
		get;
	}

	/// <summary>Gets the positive selected process identifier from the first read, or zero.</summary>
	internal int ProcessId
	{
		get;
	}

	/// <summary>Gets the facts whose global was resolved and returned a well-formed value.</summary>
	internal TargetProbeFacts Resolved
	{
		get;
	}

	/// <summary>Gets the facts whose global was probed and found absent or not callable.</summary>
	internal TargetProbeFacts Absent
	{
		get;
	}

	/// <summary>Gets <c>isConnectedToCEServer()</c>, when read.</summary>
	internal bool? IsConnectedToCeServer
	{
		get;
	}

	/// <summary>Gets <c>targetIs64Bit()</c>, when read.</summary>
	internal bool? Is64Bit
	{
		get;
	}

	/// <summary>Gets <c>targetIsX86()</c>, when read.</summary>
	internal bool? IsX86Family
	{
		get;
	}

	/// <summary>Gets <c>targetIsArm()</c>, when read.</summary>
	internal bool? IsArmFamily
	{
		get;
	}

	/// <summary>Gets <c>targetIsAndroid()</c>, when read.</summary>
	internal bool? IsAndroid
	{
		get;
	}

	/// <summary>Gets the raw <c>getABI()</c> integer, when read.</summary>
	internal int? AbiCode
	{
		get;
	}

	/// <summary>Gets the raw <c>getPointerSize()</c> integer, when read; any 32-bit integer is kept.</summary>
	internal int? ConfiguredPointerSizeBytes
	{
		get;
	}
}
