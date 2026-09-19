using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CESDK.Lua.Interop.Api;

namespace CESDK.Lua.Interop.Loading;

/// <summary>
///     Finds a Lua library that is <b>already mapped</b> into the current process, so that <see cref="LuaApi" /> binds to
///     the copy the host uses. It never loads anything. No policy: which name to ask for, when, and what to do when it
///     is absent is decided by the caller (<c>CESDK.Hosting</c> in production).
/// </summary>
/// <remarks>
///     <para>
///         Why not <c>NativeLibrary.TryLoad("lua53-64.dll")</c>: a bare-name <c>LoadLibrary</c> does return the mapped
///         module
///         when there is one, but when there is none it goes through the DLL search path and loads whatever file of that
///         name
///         it finds first - exactly the "second copy" (with its own, unrelated global state) that must never be bound, and
///         a
///         DLL-planting vector. The loaded-module lookup cannot do that: it answers from the loader's module list or
///         fails.
///     </para>
///     <para>
///         Why <c>GetModuleHandleExW</c> rather than <c>GetModuleHandleW</c>: the plain variant returns an uncounted
///         handle,
///         which the Win32 documentation itself flags as racy against a concurrent <c>FreeLibrary</c>. The Ex variant with
///         no
///         flags takes a loader reference, so the handle has the same semantics as one from <c>NativeLibrary.Load</c> and
///         stays valid for the function-pointer table that is built from it.
///     </para>
/// </remarks>
public static unsafe partial class LuaModule
{
    /// <summary>File name of the Lua 5.3 library inside a 64-bit Cheat Engine process.</summary>
    public const string CheatEngine64ModuleName = "lua53-64.dll";

    /// <summary>
    ///     Looks for a module that is already loaded in the current process. Thread-safe; does not run any code of the
    ///     module.
    /// </summary>
    /// <param name="moduleName">
    ///     Module file name, compared case-insensitively against the loaded modules (".dll" is assumed when there is no
    ///     extension), or a full path to disambiguate. With a bare name and several loaded modules of that name, which one
    ///     is returned is unspecified.
    /// </param>
    /// <param name="moduleHandle">
    ///     The module handle (usable with <c>NativeLibrary.GetExport</c> and <see cref="LuaApi.Initialize" />), or zero.
    ///     Each successful call adds one loader reference to the module: keep it for the life of the process, which is
    ///     what a bound <see cref="LuaApi" /> needs, or balance it with <c>NativeLibrary.Free</c>.
    /// </param>
    /// <returns>
    ///     True when the module is loaded. False when it is not, and always on a non-Windows platform, where this lookup
    ///     is not implemented.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="moduleName" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="moduleName" /> is empty.</exception>
    public static bool TryGetLoaded(string moduleName, out nint moduleHandle)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);

        moduleHandle = 0;
        if (!OperatingSystem.IsWindows()) return false;

        nint handle = 0;
        fixed (char* name = moduleName)
        {
            if (GetModuleHandleExW(0, name, &handle) == 0) return false;
        }

        moduleHandle = handle;
        return handle != 0;
    }

    /// <summary><see cref="TryGetLoaded(string, out nint)" /> for <see cref="CheatEngine64ModuleName" />.</summary>
    /// <param name="moduleHandle">The module handle, or zero.</param>
    /// <returns>True when the current process has Cheat Engine's 64-bit Lua library loaded.</returns>
    public static bool TryGetLoaded(out nint moduleHandle)
    {
        return TryGetLoaded(CheatEngine64ModuleName, out moduleHandle);
    }

    // BOOL GetModuleHandleExW(DWORD dwFlags, LPCWSTR lpModuleName, HMODULE* phModule); dwFlags = 0: counted handle.
    [LibraryImport("kernel32", EntryPoint = "GetModuleHandleExW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SupportedOSPlatform("windows")]
    private static partial int GetModuleHandleExW(uint dwFlags, char* lpModuleName, nint* phModule);
}
