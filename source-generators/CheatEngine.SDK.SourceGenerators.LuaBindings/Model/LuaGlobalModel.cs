using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>
///     Everything the pipeline keeps about one <c>[LuaGlobal]</c> method: strings, flags and the call model. Two runs
///     over an unchanged method compare equal; nothing here roots a compilation.
/// </summary>
/// <param name="ContainingType">The type whose partial part receives the body.</param>
/// <param name="ContainingTypeIssues">
///     Why that type cannot take a generated part; <see cref="Shared.LuaBindings.Model.ContainingTypeIssues.None" />
///     when it can.
/// </param>
/// <param name="Issues">Why the method cannot be implemented; <see cref="LuaGlobalShapeIssues.None" /> when it can.</param>
/// <param name="Call">The body to emit; <see langword="null" /> when the signature could not be classified.</param>
/// <param name="SortKey">
///     The method name and parameter types, for a deterministic order inside the file (overloads share a
///     name).
/// </param>
/// <param name="HasGeneratedIdentityCollision">
///     Whether this declaration would collide with a local or cache-field identifier that the generated body needs.
///     It is kept separate from shape issues so the analyzer can report CESDK2007 rather than pretending a scalar type
///     was unsupported.
/// </param>
internal sealed record LuaGlobalModel(
    ContainingTypeModel ContainingType,
    ContainingTypeIssues ContainingTypeIssues,
    LuaGlobalShapeIssues Issues,
    LuaGlobalCallModel? Call,
    string SortKey,
    bool HasGeneratedIdentityCollision = false)
{
    /// <summary><see langword="true" /> when a body can be emitted for this method.</summary>
    public bool IsValid => Issues == LuaGlobalShapeIssues.None && ContainingTypeIssues == ContainingTypeIssues.None &&
                           Call is not null && !HasGeneratedIdentityCollision;
}
