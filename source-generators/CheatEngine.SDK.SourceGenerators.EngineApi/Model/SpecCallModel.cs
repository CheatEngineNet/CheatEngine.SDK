using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Model;

/// <summary>
///     One entry of a spec file: the curated XML-doc summary (original English, never copied from Cheat Engine's
///     documentation) plus the call shape <c>LuaGlobalCallEmitter</c> needs. <see cref="Call" /> always has
///     <see cref="LuaGlobalCallModel.Modifiers" /> <c>"public static"</c>: EngineApi emits complete declarations, never a
///     partial body for another generator's output to complete.
/// </summary>
/// <param name="Line">1-based line where the entry starts, used for an entry-wide diagnostic.</param>
/// <param name="MethodLine">1-based line of <c>method</c>, used for a generated-member conflict diagnostic.</param>
/// <param name="MethodColumn">1-based column of the <c>method</c> value.</param>
/// <param name="GlobalLine">1-based line of <c>global</c>, used for a generated-cache conflict diagnostic.</param>
/// <param name="GlobalColumn">1-based column of the <c>global</c> value.</param>
/// <param name="Summary">The text of the generated member's <c>&lt;summary&gt;</c>, already curated English.</param>
/// <param name="Call">The validated call shape, ready for <c>LuaGlobalCallEmitter.Emit</c>.</param>
/// <param name="Contract">
///     The CE 7.7 evidence contract when the source file opts into <c>contract: ce77</c>; <see langword="null" />
///     only for legacy in-repository fixtures while they are migrated.
/// </param>
internal sealed record SpecCallModel(
	int Line,
	int MethodLine,
	int MethodColumn,
	int GlobalLine,
	int GlobalColumn,
	string Summary,
	LuaGlobalCallModel Call,
	SpecContract? Contract);
