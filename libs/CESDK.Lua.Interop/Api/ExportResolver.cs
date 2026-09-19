using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CESDK.Lua.Interop.Api;

/// <summary>
///     Resolves export names of one native module and remembers every name that was not found, so that a failed
///     initialization can name all of them at once. Cold path: runs once per bind attempt or probe.
/// </summary>
internal unsafe ref struct ExportResolver
{
    private readonly nint _module;
    private List<string>? _missing;

    /// <summary>Creates a resolver over a module handle that the caller guarantees to be valid and loaded.</summary>
    public ExportResolver(nint module)
    {
        _module = module;
    }

    /// <summary>Number of names asked for so far.</summary>
    public int Requested { get; private set; }

    /// <summary>The names that were asked for and are not exported by the module, in request order.</summary>
    public readonly IReadOnlyList<string> Missing => _missing is null ? [] : _missing;

    /// <summary>Returns the address of <paramref name="name" />, or null after recording the name as missing.</summary>
    public void* Resolve(string name)
    {
        Requested++;
        if (NativeLibrary.TryGetExport(_module, name, out var address) && address != 0) return (void*)address;

        (_missing ??= []).Add(name);
        return null;
    }
}
