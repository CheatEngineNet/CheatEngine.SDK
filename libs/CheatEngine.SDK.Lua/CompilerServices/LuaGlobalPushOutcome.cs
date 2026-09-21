using System.ComponentModel;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Lua.CompilerServices;

/// <summary>The detailed result of resolving a Lua global for an opt-in generated binding.</summary>
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
    public LuaGlobalPushStatus Status { get; }

    /// <summary>Gets the protected Lua status when <see cref="Status" /> is <see cref="LuaGlobalPushStatus.LuaFailure" />.</summary>
    public LuaStatus LuaStatus { get; }

    /// <summary>Gets whether the global function was pushed.</summary>
    public bool IsSuccess => Status == LuaGlobalPushStatus.Success;

    /// <summary>Gets a successful resolution.</summary>
    public static LuaGlobalPushOutcome Success => default;

    /// <summary>Creates an unavailable resolution.</summary>
    public static LuaGlobalPushOutcome Unavailable => new(LuaGlobalPushStatus.Unavailable, LuaStatus.Ok);

    /// <summary>Creates a protected Lua resolution failure.</summary>
    /// <param name="luaStatus">The protected Lua status.</param>
    public static LuaGlobalPushOutcome LuaFailure(LuaStatus luaStatus)
    {
        return new(LuaGlobalPushStatus.LuaFailure, luaStatus);
    }

    /// <summary>Projects this resolution into a generated binding status.</summary>
    public LuaOperationStatus ToOperationStatus()
    {
        return Status switch
        {
            LuaGlobalPushStatus.Success => LuaOperationStatus.Success,
            LuaGlobalPushStatus.Unavailable => LuaOperationStatus.GlobalUnavailable,
            _ => LuaOperationStatus.LuaFailure(LuaStatus),
        };
    }
}
