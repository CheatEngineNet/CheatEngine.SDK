using System;
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>Runs Cheat Engine's string-form <c>AOBScan</c> and returns its caller-owned StringList result.</summary>
/// <remarks>
///     <para>
///         The exact CE 7.7.0.10621 name is <c>AOBScan</c>. It returns a StringList of matching addresses, and CE's
///         own documentation says that the caller must free the list. This API exposes that fact as
///         <see cref="Owned{T}" />: dispose it before plugin disable, preferably after copying the wanted strings or
///         addresses into managed storage. The returned <see cref="StringList" /> remains a borrowed handle and has no
///         public destroy member.
///     </para>
///     <para>
///         <see cref="TryScanDetailed(string, out Owned{StringList}?)" /> preserves the distinct outcomes of an
///         unresolved global, protected Lua failure, CE <c>nil</c>, and a malformed non-nil result. The boolean
///         <c>TryScan</c> overloads retain their existing convenience contract by returning <see langword="false" /> for
///         all of those outcomes. The stack is restored in every case. A detached runtime throws
///         <see cref="InvalidOperationException" />. CE's documentation does not state AOB scan thread affinity, so this
///         method makes no unverified main-thread claim and uses the calling thread's host Lua state. The owner it
///         returns follows <see cref="Owned{T}" />'s existing main-thread destruction contract.
///     </para>
/// </remarks>
public static class AobScanner
{
    private static readonly LuaRef SAobScan = new();

    /// <summary>Runs AOBScan with only its required pattern argument.</summary>
    /// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
    /// <param name="results">The caller-owned result list, or <see langword="null" /> on failure/no result.</param>
    /// <returns><see langword="true" /> when CE returned a non-null host object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
    [RequiresPluginEnabled]
    public static bool TryScan(string pattern, [NotNullWhen(true)] out Owned<StringList>? results)
    {
        return TryScanDetailed(pattern, out results) == AobScanStatus.Success;
    }

    /// <summary>Runs AOBScan with explicit protection and alignment options.</summary>
    /// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
    /// <param name="options">The optional CE arguments and their exact positions.</param>
    /// <param name="results">The caller-owned result list, or <see langword="null" /> on failure/no result.</param>
    /// <returns><see langword="true" /> when CE returned a non-null host object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
    [RequiresPluginEnabled]
    public static bool TryScan(string pattern, AobScanOptions options,
        [NotNullWhen(true)] out Owned<StringList>? results)
    {
        return TryScanDetailed(pattern, options, out results) == AobScanStatus.Success;
    }

    /// <summary>Runs AOBScan with only its required pattern argument and reports its precise result category.</summary>
    /// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
    /// <param name="results">The caller-owned result list only when the returned status is <see cref="AobScanStatus.Success" />.</param>
    /// <returns>The protected AOBScan outcome without parsing a Lua error message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
    [RequiresPluginEnabled]
    public static AobScanStatus TryScanDetailed(string pattern, out Owned<StringList>? results)
    {
        return TryScanDetailed(pattern, AobScanOptions.Default, out results);
    }

    /// <summary>Runs AOBScan with explicit protection and alignment options and reports its precise result category.</summary>
    /// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
    /// <param name="options">The optional CE arguments and their exact positions.</param>
    /// <param name="results">The caller-owned result list only when the returned status is <see cref="AobScanStatus.Success" />.</param>
    /// <returns>The protected AOBScan outcome without parsing a Lua error message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
    [RequiresPluginEnabled]
    public static AobScanStatus TryScanDetailed(string pattern, AobScanOptions options,
        out Owned<StringList>? results)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        using LuaFrame frame = new(state);
        try
        {
            var global = LuaGlobalFunctions.TryPushWithStatus(state, SAobScan, "AOBScan"u8);
            if (global == LuaGlobalPushStatus.Unavailable)
            {
                results = null;
                return AobScanStatus.GlobalUnavailable;
            }

            if (global != LuaGlobalPushStatus.Success)
            {
                results = null;
                return AobScanStatus.LuaFailure;
            }

            var argumentCount = PushArguments(state, pattern, options);
            if (!state.TryCall(argumentCount, 1).IsOk)
            {
                results = null;
                return AobScanStatus.LuaFailure;
            }

            if (state.IsNil(-1))
            {
                results = null;
                return AobScanStatus.NoResult;
            }

            if (!CEObject.TryRead(state, -1, out var handle))
            {
                results = null;
                return AobScanStatus.InvalidResult;
            }

            results = new Owned<StringList>(StringList.FromHandle(handle));
            return AobScanStatus.Success;
        }
        catch (LuaException)
        {
            results = null;
            return AobScanStatus.LuaFailure;
        }
    }

    private static int PushArguments(LuaState state, string pattern, AobScanOptions options)
    {
        StringMarshaller.Push(state, pattern);
        if (options.HasAlignment)
        {
            StringMarshaller.Push(state, options.ProtectionFlags);
            Int32Marshaller.Push(state, (int)options.AlignmentMethod);
            StringMarshaller.Push(state, options.AlignmentParameter);
            return 4;
        }

        if (options.ProtectionFlags is null) return 1;

        StringMarshaller.Push(state, options.ProtectionFlags);
        return 2;
    }
}
