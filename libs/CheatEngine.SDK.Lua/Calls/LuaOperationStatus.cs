using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Lua.Calls;

/// <summary>A compact, allocation-free outcome for an opt-in generated Lua global binding.</summary>
/// <remarks>
///     The status deliberately does not capture Lua's error text: extracting it reads the transient Lua stack and
///     allocates a managed string. Callers can classify a protected failure by <see cref="LuaStatus" /> without exposing
///     a <c>LuaState</c> or parsing a localized exception message.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct LuaOperationStatus : IEquatable<LuaOperationStatus>
{
    private LuaOperationStatus(LuaOperationStatusKind kind, LuaStatus luaStatus)
    {
        Kind = kind;
        LuaStatus = luaStatus;
    }

    /// <summary>Gets the factual binding outcome.</summary>
    public LuaOperationStatusKind Kind { get; }

    /// <summary>Gets the protected Lua status for <see cref="LuaOperationStatusKind.LuaFailure" />; otherwise <see cref="LuaStatus.Ok" />.</summary>
    public LuaStatus LuaStatus { get; }

    /// <summary>Gets a successful status.</summary>
    public static LuaOperationStatus Success => default;

    /// <summary>Gets a status for an absent or non-callable global.</summary>
    public static LuaOperationStatus GlobalUnavailable => new(LuaOperationStatusKind.GlobalUnavailable, LuaStatus.Ok);

    /// <summary>Gets a status for an unqualified Lua <c>nil</c> result.</summary>
    public static LuaOperationStatus NilResult => new(LuaOperationStatusKind.NilResult, LuaStatus.Ok);

    /// <summary>Gets a status for a non-nil result outside the declared marshalling contract.</summary>
    public static LuaOperationStatus InvalidResult => new(LuaOperationStatusKind.InvalidResult, LuaStatus.Ok);

    /// <summary>Gets a status for a stack-capacity failure before the call begins.</summary>
    public static LuaOperationStatus StackUnavailable => new(LuaOperationStatusKind.StackUnavailable, LuaStatus.Ok);

    /// <summary>Gets a status for a protected Lua failure.</summary>
    /// <param name="luaStatus">The unmodified status returned by the protected Lua primitive.</param>
    public static LuaOperationStatus LuaFailure(LuaStatus luaStatus)
    {
        return new(LuaOperationStatusKind.LuaFailure, luaStatus);
    }

    /// <summary>Gets whether the call and result conversions completed successfully.</summary>
    public bool IsSuccess => Kind == LuaOperationStatusKind.Success;

    /// <inheritdoc />
    public bool Equals(LuaOperationStatus other)
    {
        return Kind == other.Kind && LuaStatus == other.LuaStatus;
    }

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is LuaOperationStatus other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine((int)Kind, LuaStatus);
    }

    /// <summary>Compares two operation statuses.</summary>
    public static bool operator ==(LuaOperationStatus left, LuaOperationStatus right)
    {
        return left.Equals(right);
    }

    /// <summary>Compares two operation statuses.</summary>
    public static bool operator !=(LuaOperationStatus left, LuaOperationStatus right)
    {
        return !left.Equals(right);
    }
}
