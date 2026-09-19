using CESDK.SourceGenerators.Shared.LuaBindings.Model;
using CESDK.SourceGenerators.Shared.LuaEmit;

namespace CESDK.SourceGenerators.LuaBindings.Model;

/// <summary>
///     Everything the pipeline keeps about one <c>[LuaFunction]</c> method: strings, flags and the thunk model. Two
///     runs over an unchanged method compare equal; nothing here roots a compilation.
/// </summary>
/// <param name="ContainingType">The type the thunk is emitted into.</param>
/// <param name="ContainingTypeIssues">
///     Why that type cannot take a generated part; <see cref="Shared.LuaBindings.Model.ContainingTypeIssues.None" />
///     when it can.
/// </param>
/// <param name="LuaName">
///     The attribute's name argument, verbatim; empty when missing. Used to detect duplicates within a
///     type.
/// </param>
/// <param name="Issues">Why the method cannot be exported; <see cref="LuaFunctionShapeIssues.None" /> when it can.</param>
/// <param name="Thunk">The thunk to emit; <see langword="null" /> when the signature could not be classified.</param>
internal sealed record LuaFunctionModel(
    ContainingTypeModel ContainingType,
    ContainingTypeIssues ContainingTypeIssues,
    string LuaName,
    LuaFunctionShapeIssues Issues,
    LuaThunkModel? Thunk)
{
    /// <summary>
    ///     <see langword="true" /> when a thunk can be emitted for this method (before the duplicate-name check of its
    ///     group).
    /// </summary>
    public bool IsValid => Issues == LuaFunctionShapeIssues.None && ContainingTypeIssues == ContainingTypeIssues.None &&
                           Thunk is not null;
}
