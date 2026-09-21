using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Lua.Registration;

/// <summary>Returns the registration result, primary failure, compensation report, and any owned lease.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LuaRegistrationResult
{
    internal LuaRegistrationResult(LuaRegistrationResultKind kind, LuaRegistrationFailure? failure,
        LuaRegistrationReleaseOutcome rollback, LuaRegistrationLease? lease)
    {
        Kind = kind;
        Failure = failure;
        Rollback = rollback;
        Lease = lease;
    }

    /// <summary>Gets the stable registration category.</summary>
    public LuaRegistrationResultKind Kind { get; }

    /// <summary>Gets the collision or protected-operation failure when <see cref="IsSuccess" /> is <see langword="false" />.</summary>
    public LuaRegistrationFailure? Failure { get; }

    /// <summary>Gets the compensation outcome after a publication failure, otherwise <see cref="LuaRegistrationReleaseKind.NotAttempted" />.</summary>
    public LuaRegistrationReleaseOutcome Rollback { get; }

    /// <summary>Gets the complete successful lease, or a residual lease after failed compensation. Dispose every non-null lease.</summary>
    public LuaRegistrationLease? Lease { get; }

    /// <summary>Gets whether every requested entry was published.</summary>
    public bool IsSuccess => Kind == LuaRegistrationResultKind.Succeeded;
}
