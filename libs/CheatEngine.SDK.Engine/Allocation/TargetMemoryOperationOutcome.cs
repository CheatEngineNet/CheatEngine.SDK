using System;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     A compact, allocation-domain outcome that lets a caller classify the original Engine boundary result without
///     inspecting a raw Lua state or localized exception message.
/// </summary>
/// <remarks>
///     <see cref="LuaStatus" /> is meaningful only for <see cref="TargetMemoryOperationOutcomeKind.ProtectedLuaFailure" />.
///     It is <see cref="LuaStatus.Ok" /> for every other kind. An absent allocation or rejected deallocation is an
///     <see cref="TargetMemoryOperationOutcomeKind.ExpectedFailure" />, not a protected Lua failure.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TargetMemoryOperationOutcome
{
    internal TargetMemoryOperationOutcome(TargetMemoryOperationOutcomeKind kind, LuaStatus luaStatus)
    {
        Kind = kind;
        LuaStatus = luaStatus;
    }

    /// <summary>Gets the stable category of the operation result.</summary>
    public TargetMemoryOperationOutcomeKind Kind { get; }

    /// <summary>
    ///     Gets the protected-call status for a Lua failure, or <see cref="LuaStatus.Ok" /> when
    ///     <see cref="Kind" /> is not <see cref="TargetMemoryOperationOutcomeKind.ProtectedLuaFailure" />.
    /// </summary>
    public LuaStatus LuaStatus { get; }

    /// <summary>Gets whether the operation completed successfully.</summary>
    public bool IsSuccess => Kind == TargetMemoryOperationOutcomeKind.Succeeded;

    /// <summary>Gets whether Cheat Engine returned its documented negative result.</summary>
    public bool IsExpectedFailure => Kind == TargetMemoryOperationOutcomeKind.ExpectedFailure;

    /// <summary>
    ///     Gets the corresponding stable Engine failure category, or <see langword="null" /> when no Engine failure is
    ///     represented, including a successful or unspecified value.
    /// </summary>
    public EngineFailureKind? FailureKind => Kind switch
    {
        TargetMemoryOperationOutcomeKind.ExpectedFailure => EngineFailureKind.ExpectedOperationFailure,
        TargetMemoryOperationOutcomeKind.GlobalUnavailable => EngineFailureKind.GlobalUnavailable,
        TargetMemoryOperationOutcomeKind.CapabilityUnavailable => EngineFailureKind.CapabilityUnavailable,
        TargetMemoryOperationOutcomeKind.ProtectedLuaFailure => EngineFailureKind.ProtectedLuaFailure,
        TargetMemoryOperationOutcomeKind.BindingFailure => EngineFailureKind.BindingFailure,
        TargetMemoryOperationOutcomeKind.MarshallingFailure => EngineFailureKind.MarshallingFailure,
        TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable => EngineFailureKind.TargetIdentityUnavailable,
        TargetMemoryOperationOutcomeKind.TargetIdentityMismatch => EngineFailureKind.TargetIdentityMismatch,
        _ => null,
    };

    /// <summary>Creates a successful allocation operation outcome.</summary>
    public static TargetMemoryOperationOutcome Succeeded()
    {
        return new TargetMemoryOperationOutcome(TargetMemoryOperationOutcomeKind.Succeeded, LuaStatus.Ok);
    }

    /// <summary>Creates a specified allocation operation failure outcome.</summary>
    /// <param name="failureKind">The stable Engine failure category to report.</param>
    /// <param name="luaStatus">
    ///     The non-success protected-call status when <paramref name="failureKind" /> is
    ///     <see cref="EngineFailureKind.ProtectedLuaFailure" />; ignored for every other failure category.
    /// </param>
    /// <returns>A specified non-success outcome with a valid Lua status.</returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="failureKind" /> is <see cref="EngineFailureKind.ProtectedLuaFailure" /> and
    ///     <paramref name="luaStatus" /> is successful.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="failureKind" /> is not an Engine failure category supported by memory allocation.
    /// </exception>
    public static TargetMemoryOperationOutcome Failed(EngineFailureKind failureKind, LuaStatus luaStatus = default)
    {
        return failureKind switch
        {
            EngineFailureKind.ExpectedOperationFailure => new TargetMemoryOperationOutcome(
                TargetMemoryOperationOutcomeKind.ExpectedFailure, LuaStatus.Ok),
            EngineFailureKind.GlobalUnavailable => new TargetMemoryOperationOutcome(
                TargetMemoryOperationOutcomeKind.GlobalUnavailable, LuaStatus.Ok),
            EngineFailureKind.CapabilityUnavailable => new TargetMemoryOperationOutcome(
                TargetMemoryOperationOutcomeKind.CapabilityUnavailable, LuaStatus.Ok),
            EngineFailureKind.ProtectedLuaFailure when luaStatus.IsOk => throw new ArgumentException(
                "A protected Lua failure requires a non-success Lua status.", nameof(luaStatus)),
            EngineFailureKind.ProtectedLuaFailure => new TargetMemoryOperationOutcome(
                TargetMemoryOperationOutcomeKind.ProtectedLuaFailure, luaStatus),
            EngineFailureKind.BindingFailure => new TargetMemoryOperationOutcome(
                TargetMemoryOperationOutcomeKind.BindingFailure, LuaStatus.Ok),
            EngineFailureKind.MarshallingFailure => new TargetMemoryOperationOutcome(
                TargetMemoryOperationOutcomeKind.MarshallingFailure, LuaStatus.Ok),
            EngineFailureKind.TargetIdentityUnavailable => new TargetMemoryOperationOutcome(
                TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable, LuaStatus.Ok),
            EngineFailureKind.TargetIdentityMismatch => new TargetMemoryOperationOutcome(
                TargetMemoryOperationOutcomeKind.TargetIdentityMismatch, LuaStatus.Ok),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind), failureKind,
                "The failure category is not supported by memory allocation."),
        };
    }
}
