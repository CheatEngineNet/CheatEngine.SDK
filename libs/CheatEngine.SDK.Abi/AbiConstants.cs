using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;

namespace CheatEngine.SDK.Abi;

/// <summary>
///     Numeric constants of the Cheat Engine plugin ABI that do not belong to one particular structure.
/// </summary>
/// <remarks>Pure compile-time constants: usable from any thread, at any time, including before the plugin is enabled.</remarks>
public static class AbiConstants
{
	/// <summary>
	///     The plugin SDK version described by this assembly, and the value a plugin reports back to Cheat Engine in
	///     <see cref="PluginVersion.Version" /> and <see cref="PluginInitRecord.Version" />.
	/// </summary>
	/// <remarks>
	///     <para>
	///         <b>Evidence (verified, three sources agree):</b> the version macro of <c>cepluginsdk.h</c>, the version
	///         constant
	///         of <c>cepluginsdk.pas</c> and the private version constant of the official managed bootstrap
	///         (<c>c# template/SDK/CESDK.cs</c>), all read from the CE 7.7.0.10621 installation, are 6.
	///     </para>
	///     <para>
	///         <b>Inferred from the public 7.5 host source</b> (the 7.7 host is closed source): the host refuses a
	///         plugin that reports a version greater than its own, and it hands the 6-field
	///         <see cref="ManagedExportedFunctions" /> record only to a managed plugin that reports 6 or more. A
	///         managed plugin therefore has to report exactly this value.
	///     </para>
	/// </remarks>
	public const int SdkVersion = 6;
}
