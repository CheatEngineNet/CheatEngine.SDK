namespace CESDK.Abi.Native;

/// <summary>
///     The names of the three functions a <b>native</b> plugin DLL exports (the path a Native AOT build of a plugin
///     would take, because such a DLL has no CLR header and Cheat Engine therefore treats it as native).
/// </summary>
/// <remarks>
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
///         file of the official C sample plugin (CE 7.7.0.10621). The Native AOT load path as a whole is untested.
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
