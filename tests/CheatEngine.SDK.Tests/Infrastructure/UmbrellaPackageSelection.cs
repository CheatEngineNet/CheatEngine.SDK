namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>The package the fixture must test, as decided by <see cref="UmbrellaPackageSource.Select" />.</summary>
/// <param name="Origin">Whether the fixture packs or copies a supplied file.</param>
/// <param name="PrebuiltPath">The absolute path of the supplied file, or <see langword="null" /> for a self-pack.</param>
internal sealed record UmbrellaPackageSelection(UmbrellaPackageOrigin Origin, string? PrebuiltPath);
