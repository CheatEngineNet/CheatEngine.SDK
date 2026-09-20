using System;
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Objects;
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
///         The result is <see langword="false" /> for an unresolved global, protected Lua failure, CE <c>nil</c>, or a
///         value that is not a host object; the stack is restored in every one of those cases. A detached runtime throws
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
        return TryScan(pattern, AobScanOptions.Default, out results);
    }

    /// <summary>Runs AOBScan with explicit protection and alignment options.</summary>
    /// <param name="pattern">CE's AOB pattern string, passed without normalization.</param>
    /// <param name="options">The optional CE arguments and their exact positions.</param>
    /// <param name="results">The caller-owned result list, or <see langword="null" /> on failure/no result.</param>
    /// <returns><see langword="true" /> when CE returned a non-null host object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern" /> is <see langword="null" />.</exception>
    [RequiresPluginEnabled]
    public static bool TryScan(string pattern, AobScanOptions options, [NotNullWhen(true)] out Owned<StringList>? results)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        using LuaFrame frame = new(state);
        if (!LuaGlobalFunctions.TryPush(state, SAobScan, "AOBScan"u8))
        {
            results = null;
            return false;
        }

        StringMarshaller.Push(state, pattern);
        var argumentCount = 1;
        if (options.HasAlignment)
        {
            StringMarshaller.Push(state, options.ProtectionFlags);
            Int32Marshaller.Push(state, (int)options.AlignmentMethod);
            StringMarshaller.Push(state, options.AlignmentParameter);
            argumentCount += 3;
        }
        else if (options.ProtectionFlags is not null)
        {
            StringMarshaller.Push(state, options.ProtectionFlags);
            argumentCount++;
        }

        if (!state.TryCall(argumentCount, 1).IsOk || !CEObject.TryRead(state, -1, out var handle))
        {
            results = null;
            return false;
        }

        results = new Owned<StringList>(StringList.FromHandle(handle));
        return true;
    }
}
