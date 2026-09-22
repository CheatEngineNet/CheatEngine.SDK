using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>A copied symbol record returned by Cheat Engine's <c>getSymbolInfo</c> global.</summary>
/// <remarks>
///     Provenance: CE 7.7.0.10621 <c>celua.txt</c> says <c>getSymbolInfo</c> returns a <c>SymbolList</c> symbol table;
///     the <c>SymbolList</c> class section defines the canonical fields <c>modulename</c>, <c>searchkey</c>,
///     <c>address</c> and <c>symbolsize</c>. The record owns no CE object. A missing symbol is not represented by an
///     empty record: <see cref="EngineInspection.GetSymbolInfo" /> returns <see cref="InspectionStatus.NotFound" /> for
///     the Lua <c>nil</c> result.
/// </remarks>
/// <param name="ModuleName">The module name reported by the symbol table.</param>
/// <param name="SearchKey">The symbol lookup key reported by the symbol table.</param>
/// <param name="Address">The target-process address of the symbol.</param>
/// <param name="Size">The symbol extent in bytes.</param>
public readonly record struct SymbolInfo(
	string ModuleName,
	string SearchKey,
	Address Address,
	MemorySize Size);
