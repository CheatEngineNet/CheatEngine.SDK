using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using CESDK.Lua.Interop.Api;
using CESDK.Lua.Interop.Loading;

namespace CESDK.Hosting.Bootstrap;

/// <summary>
///     Finds the Lua library the host process already has loaded and binds <see cref="LuaApi" /> to it. In production
///     that is Cheat Engine's <c>lua53-64.dll</c>, found through
///     <see cref="LuaModule.TryGetLoaded(out nint)" /> (a
///     loaded-module lookup that never loads a second copy). The test seam <see cref="Resolver" /> replaces the lookup
///     with a managed function pointer, so that tests bind to the native Lua fixture (any Lua 5.3 DLL, any file name)
///     or simulate a process without Lua.
/// </summary>
internal static unsafe class LuaModuleLocator
{
    // The function pointer is stored as an integer so that it can be read and written with volatile semantics.
    private static nint s_resolver;

    /// <summary>
    ///     Gets or sets the test seam: a static method returning the module handle to bind, or zero when there is none.
    ///     Null (the default) selects the production lookup. Not a delegate: a managed function pointer, read once per
    ///     enable.
    /// </summary>
    internal static delegate*<nint> Resolver
    {
        get => (delegate*<nint>)Volatile.Read(ref s_resolver);
        set => Volatile.Write(ref s_resolver, (nint)value);
    }

    /// <summary>Locates the module and binds the API table to it, all or nothing.</summary>
    /// <param name="failure">Why it failed, for the log; null on success.</param>
    /// <returns><see langword="true" /> when <see cref="LuaApi.IsInitialized" /> holds for the located module.</returns>
    internal static bool TryBind([NotNullWhen(false)] out string? failure)
    {
        var handle = Locate(out var fromSeam);
        if (handle == 0)
        {
            failure = fromSeam
                ? "The test module resolver returned no Lua module."
                : "The process has no loaded module named '" + LuaModule.CheatEngine64ModuleName +
                  "': not a 64-bit Cheat Engine process, or its Lua library has another name.";
            return false;
        }

        return BindLocated(handle, !fromSeam, out failure);
    }

    /// <summary>
    ///     Binds the API table to <paramref name="handle" /> and settles the loader reference that the lookup added to it.
    /// </summary>
    /// <param name="handle">The located module.</param>
    /// <param name="counted">
    ///     <see langword="true" /> when the handle carries a reference that the lookup added and this call must settle;
    ///     <see langword="false" /> when the caller owns the handle (the test seam), which is then never released here.
    /// </param>
    /// <param name="failure">Why it failed, for the log; null on success.</param>
    /// <returns><see langword="true" /> when <see cref="LuaApi.IsInitialized" /> holds for <paramref name="handle" />.</returns>
    /// <remarks>
    ///     A bound table needs one reference for the life of the process: the call that binds it keeps its own. Every
    ///     other outcome (already bound to this module, refused, or not a Lua library) releases it, so that enabling again
    ///     never accumulates references.
    /// </remarks>
    internal static bool BindLocated(nint handle, bool counted, [NotNullWhen(false)] out string? failure)
    {
        // The bound module goes from zero to its final value once, so a match read here still holds after the bind.
        var boundBefore = LuaApi.ModuleHandle == handle;
        var bound = LuaApi.TryInitialize(handle, out var bindFailure);
        if (counted && (boundBefore || !bound)) NativeLibrary.Free(handle);

        if (!bound)
        {
            failure = string.Create(CultureInfo.InvariantCulture,
                $"The Lua API table could not be bound to module 0x{handle:X}: {bindFailure}");
            return false;
        }

        failure = null;
        return true;
    }

    private static nint Locate(out bool fromSeam)
    {
        var resolver = Resolver;
        if (resolver is not null)
        {
            fromSeam = true;
            return resolver();
        }

        fromSeam = false;
        return LuaModule.TryGetLoaded(out var handle) ? handle : 0;
    }
}
