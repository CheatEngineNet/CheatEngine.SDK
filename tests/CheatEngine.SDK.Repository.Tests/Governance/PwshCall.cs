namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>One call of an exported function of a PowerShell module, with named arguments (splatted).</summary>
internal sealed record PwshCall(string Name, string Function, IReadOnlyDictionary<string, object?> Arguments);
