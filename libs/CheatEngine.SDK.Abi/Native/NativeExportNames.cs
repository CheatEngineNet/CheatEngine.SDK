namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     The names of the three functions a <b>classic native</b> plugin DLL exports. Classic native load path only: a
///     NativeAOT DLL exposing them is not a supported CheatEngine.SDK profile, because Cheat Engine unloads plugins with
///     <c>FreeLibrary</c>, which .NET does not support for NativeAOT libraries; see <c>libs/CheatEngine.SDK.Abi/README.md</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b>Not a replacement for the managed bootstrap.</b> Exporting these three names instead of the generated
///         <c>CESDK.CESDK.CEPluginInitialize</c> changes the load profile, the table the plugin receives (the classic
///         159-slot table instead of <see cref="Managed.ManagedExportedFunctions" />) and the unload contract at once.
///         Analyzer <c>CESDK0006</c> reports an <c>[UnmanagedCallersOnly]</c> export of any <c>CEPlugin_</c> name, and
///         the packaged build target reports <c>PublishAot</c> on a plugin library with <c>CESDK9102</c>.
///     </para>
///     <para>
///         All three are <c>stdcall</c> and return a 4-byte boolean (<see cref="Bool32" />):
///     </para>
///     <list type="bullet">
///         <item>
///             <description><see cref="GetVersion" />: <c>(PluginVersion* version, int sizeOfPluginVersion)</c>.</description>
///         </item>
///         <item>
///             <description>
///                 <see cref="InitializePlugin" />: <c>(ExportedFunctions* exports, int pluginId)</c>, where the
///                 first argument is the classic 159-slot table (not mapped by this assembly) and lives in a
///                 host
///                 stack frame: copy it during the call. The C header declares the id signed, the 7.5 host declares it
///                 unsigned
///                 (<i>inferred</i> for 7.7): same width.
///             </description>
///         </item>
///         <item>
///             <description><see cref="DisablePlugin" />: no arguments.</description>
///         </item>
///     </list>
///     <para>
///         <b>Evidence (verified):</b> the three prototypes at the end of <c>cepluginsdk.h</c> and the module-definition
///         file of the official C sample plugin (CE 7.7.0.10621). The classic native load path is not qualified on any
///         profile, and a NativeAOT plugin DLL is not supported (unload restriction:
///         https://learn.microsoft.com/dotnet/core/deploying/native-aot/libraries).
///     </para>
///     <para>
///         Compile-time constants, so they can be used as <c>[UnmanagedCallersOnly(EntryPoint = ...)]</c> arguments.
///     </para>
/// </remarks>
public static class NativeExportNames
{
	/// <summary>Export that fills in a <see cref="PluginVersion" />.</summary>
	public const string GetVersion = "CEPlugin_GetVersion";

	/// <summary>Export that receives the classic exported-functions table and the plugin id, and enables the plugin.</summary>
	public const string InitializePlugin = "CEPlugin_InitializePlugin";

	/// <summary>Export that disables the plugin.</summary>
	public const string DisablePlugin = "CEPlugin_DisablePlugin";
}
