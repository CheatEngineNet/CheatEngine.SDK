using CESDK.SourceGenerators.Shared.LuaEmit;

namespace CESDK.SourceGenerators.EngineApi.Model;

/// <summary>
///     One entry of a spec file: the curated XML-doc summary (original English, never copied from Cheat Engine's
///     documentation) plus the call shape <c>LuaGlobalCallEmitter</c> needs. <see cref="Call" /> always has
///     <see cref="LuaGlobalCallModel.Modifiers" /> <c>"public static"</c>: EngineApi emits complete declarations, never a
///     partial body for another generator's output to complete.
/// </summary>
/// <param name="Summary">The text of the generated member's <c>&lt;summary&gt;</c>, already curated English.</param>
/// <param name="Call">The validated call shape, ready for <c>LuaGlobalCallEmitter.Emit</c>.</param>
internal sealed record SpecCallModel(string Summary, LuaGlobalCallModel Call);
