namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Controls the optional <c>local</c> and <c>shallow</c> arguments of Cheat Engine's <c>getAddressSafe</c>.</summary>
/// <remarks>
///     These names intentionally follow the CE 7.7 Lua contract. <see cref="UseHostSymbolTable" /> asks CE to query
///     its own symbol table; <see cref="Shallow" /> is forwarded without managed reinterpretation. The options are
///     immutable copied values with no ownership or thread affinity of their own.
/// </remarks>
/// <param name="UseHostSymbolTable">Value for CE's optional <c>local</c> argument.</param>
/// <param name="Shallow">Value for CE's optional <c>shallow</c> argument.</param>
public readonly record struct AddressResolutionOptions(bool UseHostSymbolTable = false, bool Shallow = false);
