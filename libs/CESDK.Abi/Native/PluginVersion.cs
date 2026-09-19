using System.Runtime.InteropServices;
using CESDK.Abi.Managed;

namespace CESDK.Abi.Native;

/// <summary>
///     The structure a plugin fills in when Cheat Engine asks for its version and display name. Used by both load
///     paths: the native <see cref="NativeExportNames.GetVersion" /> export and the managed
///     <see cref="PluginInitRecord.GetVersion" /> callback.
/// </summary>
/// <remarks>
///     <para>
///         <b>Layout (64-bit): 16 bytes, natural alignment.</b> <see cref="Version" /> 0 (followed by 4 bytes of padding),
///         <see cref="PluginName" /> 8.
///     </para>
///     <para>
///         <b>Evidence (verified, three sources agree on order and widths):</b> the version structure of
///         <c>cepluginsdk.h</c>, the version record of <c>cepluginsdk.pas</c> and the private version structure of the
///         official managed bootstrap, all from the CE 7.7.0.10621 installation. The header and the managed bootstrap
///         declare the first field unsigned, the Pascal unit declares it signed: same width, and no valid version is
///         negative. None of the three files requests packing.
///     </para>
///     <para>
///         The host owns the memory and passes its byte size as the second callback argument; write the two fields
///         through the pointer and do not retain it.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct PluginVersion
{
    /// <summary>
    ///     SDK version the plugin is compatible with (offset 0): write <see cref="AbiConstants.SdkVersion" />.
    /// </summary>
    /// <remarks>
    ///     The explanatory note next to this field in the C header still talks about versions 1 and 2; it predates
    ///     the current value of the version constant in the same file and is not a constraint.
    /// </remarks>
    public uint Version;

    /// <summary>
    ///     NUL-terminated ANSI display name of the plugin (offset 8).
    /// </summary>
    /// <remarks>
    ///     Ownership: the plugin's. The pointer must outlive the call (static or never-freed native memory, not a
    ///     stack buffer, not a pinned managed array). On the managed path use the same buffer as
    ///     <see cref="PluginInitRecord.Name" />.
    /// </remarks>
    public byte* PluginName;
}
