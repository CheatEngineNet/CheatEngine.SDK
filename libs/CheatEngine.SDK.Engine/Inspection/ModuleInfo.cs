using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>A copied entry from Cheat Engine's <c>enumModules</c> table.</summary>
/// <remarks>
///     Provenance: the CE 7.7.0.10621 <c>celua.txt</c> entry for <c>enumModules</c> specifies <c>Name</c>,
///     <c>Address</c>, <c>Is64Bit</c> and <c>PathToFile</c>. Some hosts also provide <c>Size</c>; when absent,
///     <see cref="ImageSize" /> is <see langword="null" /> rather than a zero-sized module. The record is supported on
///     the Windows x64 CE 7.7 host, but <see cref="Is64Bit" /> describes the enumerated target module rather than the
///     host. It owns no CE object and remains valid only as a snapshot: module unload/reload can make its address stale.
/// </remarks>
/// <param name="Name">The module name returned by Cheat Engine.</param>
/// <param name="BaseAddress">The target-process address at which the module is loaded.</param>
/// <param name="ImageSize">
///     The mapped module size in bytes when the host supplied <c>Size</c>; otherwise
///     <see langword="null" />.
/// </param>
/// <param name="Is64Bit">Whether Cheat Engine reports this module as a 64-bit module.</param>
/// <param name="PathToFile">The path from which Cheat Engine reports the module was loaded.</param>
public readonly record struct ModuleInfo(
	string Name,
	Address BaseAddress,
	MemorySize? ImageSize,
	bool Is64Bit,
	string PathToFile);
