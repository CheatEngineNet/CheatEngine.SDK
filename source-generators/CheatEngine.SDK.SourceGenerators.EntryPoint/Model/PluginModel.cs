using CheatEngine.SDK.SourceGenerators.Shared.Shapes;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Model;

/// <summary>
///     Everything the pipeline keeps about one <c>[CheatEnginePlugin]</c> class: strings and flags only, so that two
///     runs over an unchanged class compare equal and nothing roots a compilation.
/// </summary>
/// <param name="FullyQualifiedTypeName">
///     The class as it is written in generated code: <c>global::</c>-qualified, containing types included, keywords
///     escaped (for example <c>global::My.Outer.Plugin</c>). Identifiers keep their declared characters, ASCII or not.
/// </param>
/// <param name="DisplayName">The attribute's name argument, verbatim; empty when it is missing.</param>
/// <param name="DeclaredDiagnosticIds">
///     Diagnostic IDs the class declares through <c>[Experimental]</c> or <c>[Obsolete(DiagnosticId = ...)]</c>, joined
///     with <c>", "</c> (see <see cref="Parsing.EntryPointDeclaredDiagnosticIds" />); empty when there is none.
/// </param>
/// <param name="Issues">Why the class cannot be bootstrapped; <see cref="PluginShapeIssues.None" /> when it can.</param>
internal sealed record PluginModel(
	string FullyQualifiedTypeName,
	string DisplayName,
	string DeclaredDiagnosticIds,
	PluginShapeIssues Issues)
{
	/// <summary><see langword="true" /> when the generated factory can construct this class.</summary>
	public bool IsValid => Issues == PluginShapeIssues.None;
}
