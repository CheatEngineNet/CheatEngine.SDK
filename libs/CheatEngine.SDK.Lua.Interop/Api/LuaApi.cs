using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;

namespace CheatEngine.SDK.Lua.Interop.Api;

/// <summary>
///     The Lua 5.3 C API (<c>lua.h</c>, <c>lauxlib.h</c>, <c>lualib.h</c>) with its C names, constants and macro
///     equivalents, bound to one native module that the caller provides. Import it with
///     <c>using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;</c> to write call sites that read like C.
/// </summary>
/// <remarks>
///     <para>
///         Mechanism: one table of <c>delegate* unmanaged[Cdecl]</c> function pointers, resolved once by
///         <see cref="Initialize" /> or <see cref="TryInitialize" /> with <c>NativeLibrary.TryGetExport</c> from an
///         injectable
///         module handle (Cheat Engine's already loaded <c>lua53-64.dll</c> in production, see
///         <see cref="Loading.LuaModule" />;
///         any
///         Lua 5.3 library in tests). Every exported function is reached through a static forwarding method of the same
///         name,
///         which supplies parameter names and documentation and keeps the table itself unwritable from outside.
///     </para>
///     <para>
///         Precondition of every forwarding method and macro: the table is bound (<see cref="IsInitialized" />). Calling
///         through an unbound table jumps to address zero and takes the process down; there is deliberately no per-call
///         check.
///     </para>
///     <para>
///         No policy lives here. In particular nothing protects the caller from Lua errors: Lua raises with <c>longjmp</c>
///         ,
///         which must never unwind managed frames (unsupported by the runtime: <c>finally</c> blocks are skipped, state is
///         corrupted). Each member documents its stack effect as "Stack: -pops +pushes" and its error behaviour as one of:
///         "Raises: never"; "Raises: memory" (the class the Lua 5.3 manual marks <c>m</c>: the allocator fails, or a
///         <c>__gc</c> finalizer fails inside the collector step that the allocation triggers, which is reported as
///         <see cref="LUA_ERRGCMM" />); "Raises: any" (can run metamethods or other Lua code: call it only where a raise
///         cannot cross managed frames, or when the operands are known to be plain); "Raises: always" (declared for
///         completeness, never call it from managed code).
///     </para>
///     <para>
///         "Raises: memory" is therefore not "safe unless memory runs out". Every allocating function may run a collector
///         step, and that step runs pending finalizers: any <c>__gc</c> metamethod, including a managed callback, can
///         execute
///         re-entrantly during the call, and in a state that holds a failing finalizer the call raises without any
///         allocator
///         failure. What separates this class from "Raises: any" is the trigger (the collector instead of the operands),
///         not
///         the consequence.
///     </para>
///     <para>
///         Thread affinity: the table is immutable once bound and can be used from any thread. The Lua library itself has
///         no
///         locking: states that share a global state (a main state and its threads) must be used by one OS thread at a
///         time.
///     </para>
/// </remarks>
public static partial class LuaApi
{
    private const string NullModuleMessage = "The module handle is zero: pass the handle of a loaded Lua 5.3 library.";

    private static readonly Lock s_gate = new();

    // Written once under s_gate, before s_module is published; never written again.
    private static Table s_table;
    private static nint s_module;

    /// <summary>Whether the function-pointer table is bound. Safe to read from any thread.</summary>
    public static bool IsInitialized => Volatile.Read(ref s_module) != 0;

    /// <summary>The module the table is bound to, or zero while unbound. Safe to read from any thread.</summary>
    public static nint ModuleHandle => Volatile.Read(ref s_module);

    /// <summary>
    ///     Binds the table to the exports of <paramref name="moduleHandle" />. All or nothing: on failure the table is left
    ///     exactly as it was. Idempotent for the same module; thread-safe.
    /// </summary>
    /// <param name="moduleHandle">
    ///     Handle of a loaded native module that exports the Lua 5.3 API (an <c>HMODULE</c> on Windows), as returned by
    ///     <see cref="Loading.LuaModule.TryGetLoaded(string,out nint)" /> or <c>NativeLibrary.Load</c>. The module must stay
    ///     loaded
    ///     for the rest of the process: the table keeps raw addresses into it. Passing a value that is not a module handle
    ///     is undefined behaviour.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="moduleHandle" /> is zero.</exception>
    /// <exception cref="EntryPointNotFoundException">The module lacks at least one export; the message names all of them.</exception>
    /// <exception cref="InvalidOperationException">The table is already bound to a different module.</exception>
    public static void Initialize(nint moduleHandle)
    {
        var result = Bind(moduleHandle, out var failure);
        switch (result)
        {
            case LuaApiBindResult.Bound:
                return;
            case LuaApiBindResult.NullModule:
                throw new ArgumentException(failure, nameof(moduleHandle));
            case LuaApiBindResult.MissingExports:
                throw new EntryPointNotFoundException(failure);
            default:
                throw new InvalidOperationException(failure);
        }
    }

    /// <summary>
    ///     Non-throwing form of <see cref="Initialize" />: binds the table to the exports of
    ///     <paramref name="moduleHandle" />, all or nothing. Idempotent for the same module; thread-safe.
    /// </summary>
    /// <param name="moduleHandle">See <see cref="Initialize" />. Zero is reported as a failure, not thrown.</param>
    /// <param name="failure">
    ///     Null on success. Otherwise a message for a log or a test skip reason: it names every missing export, or the
    ///     module the table is already bound to.
    /// </param>
    /// <returns>True when the table is bound to <paramref name="moduleHandle" /> when the call returns.</returns>
    public static bool TryInitialize(nint moduleHandle, [NotNullWhen(false)] out string? failure)
    {
        var bound = Bind(moduleHandle, out var message) == LuaApiBindResult.Bound;
        failure = bound ? null : message ?? "The Lua API table could not be bound.";
        return bound;
    }

    /// <summary>
    ///     Checks, without binding or changing anything, which exports of the table <paramref name="moduleHandle" /> lacks.
    ///     Usable before or after initialization, from any thread.
    /// </summary>
    /// <param name="moduleHandle">
    ///     Handle of a loaded native module; a value that is not a module handle is undefined
    ///     behaviour.
    /// </param>
    /// <returns>The missing export names in table order; empty when the module can be bound.</returns>
    /// <exception cref="ArgumentException"><paramref name="moduleHandle" /> is zero.</exception>
    public static IReadOnlyList<string> GetMissingExports(nint moduleHandle)
    {
        if (moduleHandle == 0) throw new ArgumentException(NullModuleMessage, nameof(moduleHandle));

        ExportResolver exports = new(moduleHandle);
        Table scratch = default;
        scratch.Load(ref exports);
        return exports.Missing;
    }

    private static LuaApiBindResult Bind(nint moduleHandle, out string? failure)
    {
        failure = null;
        if (moduleHandle == 0)
        {
            failure = NullModuleMessage;
            return LuaApiBindResult.NullModule;
        }

        lock (s_gate)
        {
            if (s_module == moduleHandle) return LuaApiBindResult.Bound;

            // Fill a local table first: a module that turns out to be incomplete must not leave a half-written table
            // behind, and a process that is already bound must not be disturbed at all.
            ExportResolver exports = new(moduleHandle);
            Table candidate = default;
            candidate.Load(ref exports);
            if (exports.Missing.Count != 0)
            {
                failure = DescribeMissing(moduleHandle, exports.Missing, exports.Requested);
                return LuaApiBindResult.MissingExports;
            }

            if (s_module != 0)
            {
                failure = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The Lua API table is already bound to module 0x{s_module:X} and cannot be rebound to module 0x{moduleHandle:X}: one process uses one Lua library.");
                return LuaApiBindResult.BoundToAnotherModule;
            }

            s_table = candidate;
            Volatile.Write(ref s_module, moduleHandle);
            return LuaApiBindResult.Bound;
        }
    }

    private static string DescribeMissing(nint moduleHandle, IReadOnlyList<string> missing, int requested)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Module 0x{moduleHandle:X} is not a usable Lua 5.3 library: {missing.Count} of {requested} required exports are missing ({string.Join(", ", missing)}).");
    }
}
