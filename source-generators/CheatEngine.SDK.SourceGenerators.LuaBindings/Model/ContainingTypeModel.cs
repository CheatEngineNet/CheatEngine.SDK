using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>
///     The type whose partial part a generated file re-opens: everything the file emitter needs to write
///     <c>namespace N { partial class Outer { partial class Inner { ... } } }</c> and to name the file. Strings only.
/// </summary>
/// <param name="Namespace">
///     The namespace as written in the generated file (<c>Demo.Inner</c>, keyword-escaped); empty for
///     the global namespace.
/// </param>
/// <param name="Declarations">The containing types from the outermost in, the declaring type last.</param>
/// <param name="FullyQualifiedName">
///     The declaring type as an expression: <c>global::Demo.Outer.Inner</c>, for invocations
///     of its methods.
/// </param>
/// <param name="HintBaseName">
///     The dotted name the hint name is built from: <c>Demo.Outer.Inner</c> (no <c>global::</c>, no
///     escapes).
/// </param>
internal sealed record ContainingTypeModel(
    string Namespace,
    EquatableArray<TypeDeclarationModel> Declarations,
    string FullyQualifiedName,
    string HintBaseName);
