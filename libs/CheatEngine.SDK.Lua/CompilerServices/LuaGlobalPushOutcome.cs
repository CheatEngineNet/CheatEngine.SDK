using System.ComponentModel;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Lua.CompilerServices;

/// <summary>The detailed result of resolving a Lua global for an opt-in generated binding.</summary>
/// <remarks>
///     <c>default(LuaGlobalPushOutcome)</c> is <see cref="LuaGlobalPushStatus.Unknown" />: it never reads as a pushed
///     function, and <see cref="ToOperationStatus" /> projects it to <see cref="LuaOperationStatusKind.Unknown" />.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[StructLayout(LayoutKind.Sequential)]
public readonly struct LuaGlobalPushOutcome
{
	private LuaGlobalPushOutcome(LuaGlobalPushStatus status, LuaStatus luaStatus)
	{
		Status = status;
		LuaStatus = luaStatus;
	}

	/// <summary>Gets the resolution category.</summary>
	public LuaGlobalPushStatus Status
	{
		get;
	}

	/// <summary>Gets the protected Lua status when <see cref="Status" /> is <see cref="LuaGlobalPushStatus.LuaFailure" />.</summary>
	public LuaStatus LuaStatus
	{
		get;
	}

	/// <summary>Gets whether the global function was pushed.</summary>
	public bool IsSuccess => Status == LuaGlobalPushStatus.Success;

	/// <summary>Gets a successful resolution.</summary>
	/// <remarks>Distinct from <see langword="default" />, which is <see cref="LuaGlobalPushStatus.Unknown" />.</remarks>
	public static LuaGlobalPushOutcome Success => new(LuaGlobalPushStatus.Success, LuaStatus.Ok);

	/// <summary>Creates an unavailable resolution.</summary>
	public static LuaGlobalPushOutcome Unavailable => new(LuaGlobalPushStatus.Unavailable, LuaStatus.Ok);

	/// <summary>Creates a protected Lua resolution failure.</summary>
	/// <param name="luaStatus">The protected Lua status.</param>
	public static LuaGlobalPushOutcome LuaFailure(LuaStatus luaStatus)
	{
		return new LuaGlobalPushOutcome(LuaGlobalPushStatus.LuaFailure, luaStatus);
	}

	/// <summary>Projects this resolution into a generated binding status.</summary>
	/// <returns>
	///     <see cref="LuaOperationStatus.Success" />, <see cref="LuaOperationStatus.GlobalUnavailable" /> or
	///     <see cref="LuaOperationStatus.LuaFailure" /> with the preserved <see cref="LuaStatus" />; an
	///     <see cref="LuaGlobalPushStatus.Unknown" /> resolution projects to <see langword="default" />
	///     (<see cref="LuaOperationStatusKind.Unknown" />), never to a failure with an <c>Ok</c> Lua status.
	/// </returns>
	public LuaOperationStatus ToOperationStatus()
	{
		return Status switch
		{
			LuaGlobalPushStatus.Success => LuaOperationStatus.Success,
			LuaGlobalPushStatus.Unavailable => LuaOperationStatus.GlobalUnavailable,
			LuaGlobalPushStatus.LuaFailure => LuaOperationStatus.LuaFailure(LuaStatus),
			_ => default
		};
	}
}
