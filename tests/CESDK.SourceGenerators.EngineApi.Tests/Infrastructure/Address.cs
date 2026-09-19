// ReSharper disable once CheckNamespace

namespace CESDK.Engine.Values;

/// <summary>
///     A test-only stand-in for the real <c>CESDK.Engine.Values.Address</c> (<c>libs/CESDK.Engine/Values/Address.cs</c>),
///     declared here rather than referenced from <c>libs/CESDK.Engine</c> because this project deliberately keeps its
///     real SDK references at <c>CESDK.Annotations</c>, <c>CESDK.Lua.Interop</c> and <c>CESDK.Lua</c> only: the
///     generator's own compilation must stay unaware of <c>CESDK.Engine</c>, which is the assembly the generated file
///     becomes *part of* (self-reference, not a dependency) once <c>libs/CESDK.Engine</c> itself builds. The namespace
///     here is declared literally as <c>CESDK.Engine.Values</c> (not this test project's own namespace) because that is
///     the exact namespace the generated wrapper's <c>global::CESDK.Engine.Values.Address</c> reference must resolve
///     against.
/// </summary>
/// <remarks>
///     <para>
///         This mirrors the source-stub pattern of <c>ContractStubs</c> in the other Roslyn test projects
///         rather than adding a project reference this generator's tests are not meant to have.
///         Only the surface <c>Emit/EngineApiFileEmitter.cs</c>'s address-typed wrapper calls is reproduced (a
///         constructing conversion and <see cref="ToUInt64" />), not the real type's parsing, formatting or arithmetic,
///         which the generated wrapper bodies never touch.
///     </para>
///     <para>
///         Declaring it inside this test assembly (rather than as a second syntax tree fed into each per-run compilation)
///         is what lets <c>GeneratedAssembly.Delegate&lt;TDelegate&gt;</c> work: a delegate type in this assembly can only
///         bind to a dynamically-loaded method whose parameter type resolves to the very same runtime type, and an
///         unresolved dependency of an <see cref="System.Runtime.Loader.AssemblyLoadContext" />-loaded assembly falls back
///         to the default load context, where this already-loaded test assembly's own <see cref="Address" /> is found.
///     </para>
/// </remarks>
public readonly struct Address
{
    /// <summary>Wraps a raw address, mirroring the real type's constructor.</summary>
    /// <param name="value">The address.</param>
    public Address(ulong value)
    {
        Value = value;
    }

    /// <summary>Gets the raw address.</summary>
    public ulong Value { get; }

    /// <summary>Converts a raw value, mirroring the real type's implicit conversion.</summary>
    /// <param name="value">The address.</param>
    public static implicit operator Address(ulong value)
    {
        return new Address(value);
    }

    /// <summary>Unwraps the raw value, mirroring the real type's named accessor.</summary>
    /// <returns>The raw address.</returns>
    public ulong ToUInt64()
    {
        return Value;
    }
}
