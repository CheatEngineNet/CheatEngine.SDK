using System;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>A compact, allocation-free status for one runtime process operation.</summary>
/// <remarks>
///     <para>
///         <see cref="LuaStatus" /> is meaningful only for <see cref="ProcessOperationStatusKind.ProtectedLuaFailure" />.
///         The status never copies a Lua error object or localized message from the transient Lua stack.
///     </para>
///     <para>
///         <c>default(ProcessOperationStatus)</c> has the kind <see cref="ProcessOperationStatusKind.Unknown" />, so
///         <see cref="IsSuccess" /> is <see langword="false" /> for a status that no operation assigned. Only
///         <see cref="Success" /> reports success.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ProcessOperationStatus : IEquatable<ProcessOperationStatus>
{
	private ProcessOperationStatus(ProcessOperationStatusKind kind, LuaStatus luaStatus)
	{
		Kind = kind;
		LuaStatus = luaStatus;
	}

	/// <summary>Gets the factual result category.</summary>
	public ProcessOperationStatusKind Kind
	{
		get;
	}

	/// <summary>Gets the protected Lua status for a Lua failure; otherwise <see cref="LuaStatus.Ok" />.</summary>
	public LuaStatus LuaStatus
	{
		get;
	}

	/// <summary>Gets a successful status.</summary>
	public static ProcessOperationStatus Success => new(ProcessOperationStatusKind.Success, LuaStatus.Ok);

	/// <summary>Gets a status for a target that is not currently selected.</summary>
	public static ProcessOperationStatus TargetNotAttached =>
		new(ProcessOperationStatusKind.TargetNotAttached, LuaStatus.Ok);

	/// <summary>Gets a status for an explicit selection that could not be confirmed.</summary>
	public static ProcessOperationStatus SelectionNotConfirmed =>
		new(ProcessOperationStatusKind.SelectionNotConfirmed, LuaStatus.Ok);

	/// <summary>Gets a status for a required but unavailable Lua global.</summary>
	public static ProcessOperationStatus GlobalUnavailable =>
		new(ProcessOperationStatusKind.GlobalUnavailable, LuaStatus.Ok);

	/// <summary>Gets a status for a result outside the documented process-observation shape.</summary>
	public static ProcessOperationStatus InvalidResult => new(ProcessOperationStatusKind.InvalidResult, LuaStatus.Ok);

	/// <summary>
	///     Gets a status for a selected process identifier that differed between the two reads bracketing an
	///     observation.
	/// </summary>
	public static ProcessOperationStatus TargetChanged => new(ProcessOperationStatusKind.TargetChanged, LuaStatus.Ok);

	/// <summary>Gets a status for Cheat Engine's file-as-process sentinel identifier (4294967295).</summary>
	public static ProcessOperationStatus FileAsProcessTarget =>
		new(ProcessOperationStatusKind.FileAsProcessTarget, LuaStatus.Ok);

	/// <summary>Creates a status for a protected Lua failure.</summary>
	/// <param name="luaStatus">The non-success protected Lua status.</param>
	/// <exception cref="ArgumentException"><paramref name="luaStatus" /> is successful.</exception>
	public static ProcessOperationStatus ProtectedLuaFailure(LuaStatus luaStatus)
	{
		if (luaStatus.IsOk)
		{
			throw new ArgumentException("A successful Lua status cannot describe a protected Lua failure.",
				nameof(luaStatus));
		}

		return new ProcessOperationStatus(ProcessOperationStatusKind.ProtectedLuaFailure, luaStatus);
	}

	/// <summary>Gets whether the process operation completed and supplied its declared observation.</summary>
	public bool IsSuccess => Kind == ProcessOperationStatusKind.Success;

	/// <inheritdoc />
	public bool Equals(ProcessOperationStatus other)
	{
		return Kind == other.Kind && LuaStatus == other.LuaStatus;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is ProcessOperationStatus other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine((int) Kind, LuaStatus);
	}

	/// <summary>Tests two process operation statuses for equality.</summary>
	public static bool operator ==(ProcessOperationStatus left, ProcessOperationStatus right)
	{
		return left.Equals(right);
	}

	/// <summary>Tests two process operation statuses for inequality.</summary>
	public static bool operator !=(ProcessOperationStatus left, ProcessOperationStatus right)
	{
		return !left.Equals(right);
	}
}
