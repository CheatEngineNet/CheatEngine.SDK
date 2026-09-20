namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Controls the optional <c>shallow</c> argument of Cheat Engine's <c>getAddressSafe</c>.</summary>
/// <remarks>
///     Target and host symbol spaces have separate result types. <see cref="Shallow" /> is forwarded without managed
///     reinterpretation; <see cref="EngineInspection.ResolveAddress" /> always queries the target table and
///     <see cref="EngineInspection.ResolveHostAddress" /> always queries the host table. The options are immutable
///     copied values with no ownership or thread affinity of their own.
/// </remarks>
/// <param name="Shallow">Value for CE's optional <c>shallow</c> argument.</param>
public readonly record struct AddressResolutionOptions(bool Shallow = false);
