namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>A named PE export and the RVA of its target.</summary>
internal sealed record PortableExecutableExport(string Name, uint RelativeVirtualAddress);
