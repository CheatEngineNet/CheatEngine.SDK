namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>A PE import module and the functions imported from it.</summary>
internal sealed record PortableExecutableImport(string ModuleName, IReadOnlyList<string> Symbols);
