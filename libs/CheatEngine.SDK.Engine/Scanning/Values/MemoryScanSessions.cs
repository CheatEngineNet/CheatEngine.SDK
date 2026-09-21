using System;
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Threading;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Creates plugin-owned <see cref="MemoryScanSession" /> instances through Cheat Engine's scan factories.</summary>
/// <remarks>
///     <para>
///         Cheat Engine 7.7's <c>createMemScan()</c> creates the scanner and <c>createFoundList(memscan)</c> creates
///         its result-list child. This factory is the only production path that turns either returned host object into
///         an <see cref="Owned{T}" />. It therefore establishes the parent/child ownership relationship before exposing
///         the stateful session to managed code.
///     </para>
///     <para>
///         Both globals are resolved in one held <see cref="LuaRuntimeOperation" />. If creating or decoding the child
///         fails after the scanner was created, the factory invokes <c>destroy()</c> on that scanner before it returns
///         failure. A successfully created session owns the child before the parent and preserves
///         <see cref="MemoryScanSession" />'s child-before-parent disposal order. A caller cannot construct an
///         <see cref="Owned{T}" /> for either handle from a borrowed value because that constructor remains internal to
///         the SDK.
///     </para>
/// </remarks>
public static class MemoryScanSessions
{
    private static readonly LuaRef SCreateFoundList = new();
    private static readonly LuaRef SCreateMemScan = new();

    /// <summary>Creates one new plugin-owned scanner and its attached plugin-owned found-list child.</summary>
    /// <param name="session">The new session on success; <see langword="null" /> otherwise.</param>
    /// <returns>
    ///     <see langword="true" /> when both documented factory calls returned valid host objects. Returns
    ///     <see langword="false" /> when either global is unavailable, either protected call fails, or a result is not
    ///     a non-null host object. In every failure after scanner creation, the scanner is rolled back before returning.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     The plugin is not enabled, the caller has no host Lua state, the host cannot push objects, or the caller is
    ///     not on Cheat Engine's main thread.
    /// </exception>
    [MainThreadOnly]
    [RequiresPluginEnabled]
    public static bool TryCreate([NotNullWhen(true)] out MemoryScanSession? session)
    {
        return TryCreateCore(out session, CreateSession);
    }

    // Tests use this seam to prove that an ownership-transfer failure rolls the child back before its parent. It is
    // internal deliberately: callers can select neither the owner construction nor a different adoption policy.
    internal static bool TryCreateCore([NotNullWhen(true)] out MemoryScanSession? session,
        MemoryScanSessionAdopter adopter)
    {
        ArgumentNullException.ThrowIfNull(adopter);
        RequireEnabledMainThread();

        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        using LuaFrame frame = new(state);
        Owned<MemScan>? scanner = null;
        Owned<FoundList>? foundList = null;
        try
        {
            if (!LuaGlobalFunctions.TryPush(state, SCreateMemScan, "createMemScan"u8) ||
                !state.TryCall(0, 1).IsOk ||
                !CEObject.TryRead(state, -1, out var scannerHandle))
            {
                session = null;
                return false;
            }

            scanner = new Owned<MemScan>(MemScan.FromHandle(scannerHandle));

            if (!LuaGlobalFunctions.TryPush(state, SCreateFoundList, "createFoundList"u8))
            {
                session = null;
                return false;
            }

            scanner.Value.Handle.Push(state);
            if (!state.TryCall(1, 1).IsOk || !CEObject.TryRead(state, -1, out var foundListHandle))
            {
                session = null;
                return false;
            }

            foundList = new Owned<FoundList>(FoundList.FromHandle(foundListHandle));
            session = adopter(scanner, foundList);
            return true;
        }
        finally
        {
            // Adoption transfers and empties both wrappers. Every other exit after construction must release the child
            // before the parent while the original operation is still admitted. Swallowing a protected destroy failure
            // avoids hiding the factory failure and, like Owned<T>.Dispose, never retries an unknown native state.
            if (foundList is not null && !foundList.IsDisposed)
            {
                using LuaFrame rollbackFrame = new(state);
                _ = foundList.TryDestroy(state);
            }

            if (scanner is not null && !scanner.IsDisposed)
            {
                using LuaFrame rollbackFrame = new(state);
                _ = scanner.TryDestroy(state);
            }
        }
    }

    private static MemoryScanSession CreateSession(Owned<MemScan> scanner, Owned<FoundList> foundList)
    {
        return MemoryScanSession.Adopt(scanner, foundList);
    }

    private static void RequireEnabledMainThread()
    {
        if (!LuaRuntime.IsAttached)
            throw new InvalidOperationException(
                "The Cheat Engine plugin is not enabled, so a memory scan session cannot be created.");
        if (!LuaRuntime.IsMainThread)
            throw new InvalidOperationException(
                "Memory scan session creation must run on Cheat Engine's main thread.");
    }
}
