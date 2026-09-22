using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>A copied address/value row from an initialized CE found list.</summary>
/// <remarks>
///     This value contains no CE object handle and remains usable after the session is disposed. Bulk copying is bounded
///     by the caller-provided destination; selecting a workflow-level cardinality budget remains Client policy.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MemoryScanResult(Address Address, string Value);
