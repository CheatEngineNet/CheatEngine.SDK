using System;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Controls the optional <c>shallow</c> argument of Cheat Engine's <c>getAddressSafe</c>.</summary>
/// <remarks>
///     Target and host symbol spaces have separate result types. <see cref="Shallow" /> is forwarded without managed
///     reinterpretation; <see cref="EngineInspection.ResolveAddress" /> always queries the target table and
///     <see cref="EngineInspection.ResolveHostAddress" /> always queries the host table. The two-Boolean constructor
///     and <see cref="UseHostSymbolTable" /> property remain only as an obsolete source and binary compatibility
///     shape; passing that legacy host flag to <see cref="EngineInspection.ResolveAddress" /> is rejected so an old
///     positional call cannot silently query a different address space.
/// </remarks>
public readonly record struct AddressResolutionOptions
{
    private readonly bool _useHostSymbolTable;
    private readonly bool _shallow;

    /// <summary>Creates options while retaining the released two-Boolean constructor shape.</summary>
    public AddressResolutionOptions(bool UseHostSymbolTable = false, bool Shallow = false)
    {
        _useHostSymbolTable = UseHostSymbolTable;
        _shallow = Shallow;
    }

    /// <summary>Value for CE's optional <c>shallow</c> argument.</summary>
    public bool Shallow
    {
        get => _shallow;
        init => _shallow = value;
    }

    /// <summary>Gets the removed CE <c>local</c> flag retained for source and binary compatibility.</summary>
    [Obsolete("Use EngineInspection.ResolveHostAddress for host-symbol resolution; ResolveAddress rejects this flag.",
        error: false)]
    public bool UseHostSymbolTable
    {
        get => _useHostSymbolTable;
        init => _useHostSymbolTable = value;
    }

    internal bool HostSymbolTableRequested => _useHostSymbolTable;

    /// <summary>Deconstructs the compatibility shape used by the released 1.0.0 API.</summary>
    public void Deconstruct(out bool UseHostSymbolTable, out bool Shallow)
    {
        UseHostSymbolTable = _useHostSymbolTable;
        Shallow = _shallow;
    }
}
