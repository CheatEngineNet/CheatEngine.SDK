using System;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>A compact, allocation-free outcome for <see cref="AobScanner.TryScanOutcome(string, out Objects.Owned{Objects.StringList}?)" />.</summary>
/// <remarks>
///     The result count is meaningful only for <see cref="AobScanOutcomeKind.Matches" /> and
///     <see cref="AobScanOutcomeKind.NoMatches" />. It is the count of the caller-owned host list observed immediately
///     after <c>AOBScan</c> returned; it is not a CE execution bound, a range/module guarantee, an early-stop proof, or
///     a promise that the host list cannot later change. <see cref="LuaStatus" /> is meaningful only for
///     <see cref="AobScanOutcomeKind.ProtectedLuaFailure" />.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct AobScanOutcome : IEquatable<AobScanOutcome>
{
    private AobScanOutcome(AobScanOutcomeKind kind, LuaStatus luaStatus, int resultCount)
    {
        Kind = kind;
        LuaStatus = luaStatus;
        ResultCount = resultCount;
    }

    /// <summary>Gets the factual protected scan category.</summary>
    public AobScanOutcomeKind Kind { get; }

    /// <summary>Gets the protected Lua status for a Lua failure; otherwise <see cref="CheatEngine.SDK.Lua.Calls.LuaStatus.Ok" />.</summary>
    public LuaStatus LuaStatus { get; }

    /// <summary>Gets the verified host-list count when <see cref="HasResultCount" /> is <see langword="true" />.</summary>
    public int ResultCount { get; }

    /// <summary>Gets whether <see cref="ResultCount" /> was read from a valid host StringList and satisfies its outcome invariant.</summary>
    public bool HasResultCount => Kind switch
    {
        AobScanOutcomeKind.Matches => ResultCount > 0,
        AobScanOutcomeKind.NoMatches => ResultCount == 0,
        _ => false,
    };

    /// <summary>Gets whether CE returned a valid caller-owned StringList with a verified count.</summary>
    public bool IsSuccess => HasResultCount;

    /// <summary>Creates a positive-match outcome.</summary>
    /// <param name="resultCount">The verified positive StringList count.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resultCount" /> is not positive.</exception>
    public static AobScanOutcome Matches(int resultCount)
    {
        if (resultCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(resultCount), resultCount,
                "A positive-match AOB outcome requires a positive StringList count.");

        return new AobScanOutcome(AobScanOutcomeKind.Matches, LuaStatus.Ok, resultCount);
    }

    /// <summary>Gets the outcome for a valid empty StringList.</summary>
    public static AobScanOutcome NoMatches => new(AobScanOutcomeKind.NoMatches, LuaStatus.Ok, 0);

    /// <summary>Gets the outcome for a missing or non-callable required global.</summary>
    public static AobScanOutcome GlobalUnavailable => new(AobScanOutcomeKind.GlobalUnavailable, LuaStatus.Ok, 0);

    /// <summary>Creates the outcome for a protected Lua failure.</summary>
    /// <param name="luaStatus">The failed protected Lua status.</param>
    /// <exception cref="ArgumentException"><paramref name="luaStatus" /> is successful.</exception>
    public static AobScanOutcome ProtectedLuaFailure(LuaStatus luaStatus)
    {
        if (luaStatus.IsOk)
            throw new ArgumentException("A successful Lua status cannot describe a protected Lua failure.",
                nameof(luaStatus));

        return new AobScanOutcome(AobScanOutcomeKind.ProtectedLuaFailure, luaStatus, 0);
    }

    /// <summary>Gets the outcome for a raw Lua <c>nil</c> result.</summary>
    public static AobScanOutcome NoResult => new(AobScanOutcomeKind.NoResult, LuaStatus.Ok, 0);

    /// <summary>Gets the outcome for a non-nil value that was not a valid host object.</summary>
    public static AobScanOutcome InvalidResult => new(AobScanOutcomeKind.InvalidResult, LuaStatus.Ok, 0);

    /// <summary>Gets the outcome for a valid host object whose StringList count could not be read.</summary>
    public static AobScanOutcome ResultListCountUnavailable =>
        new(AobScanOutcomeKind.ResultListCountUnavailable, LuaStatus.Ok, 0);

    /// <inheritdoc />
    public bool Equals(AobScanOutcome other)
    {
        return Kind == other.Kind && LuaStatus == other.LuaStatus && ResultCount == other.ResultCount;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is AobScanOutcome other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine((int)Kind, LuaStatus, ResultCount);
    }

    /// <summary>Tests two AOB scan outcomes for equality.</summary>
    public static bool operator ==(AobScanOutcome left, AobScanOutcome right)
    {
        return left.Equals(right);
    }

    /// <summary>Tests two AOB scan outcomes for inequality.</summary>
    public static bool operator !=(AobScanOutcome left, AobScanOutcome right)
    {
        return !left.Equals(right);
    }
}
