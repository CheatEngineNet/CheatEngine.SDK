using System.Runtime.InteropServices;

using CheatEngine.SDK.Abi.Native;

namespace CheatEngine.SDK.Abi.Managed;

/// <summary>
///     The record a managed plugin fills in when Cheat Engine calls its bootstrap method
///     (<see cref="ManagedEntryPoint.MethodName" />): plugin name, the three lifecycle callbacks and the SDK version.
///     The first argument of the bootstrap method is the address of this record.
/// </summary>
/// <remarks>
///     <para>
///         <b>Layout (64-bit): 36 bytes, byte-packed.</b> <see cref="Name" /> 0, <see cref="GetVersion" /> 8,
///         <see cref="EnablePlugin" /> 16, <see cref="DisablePlugin" /> 24, <see cref="Version" /> 32. There is no tail
///         padding: the natural (unpacked) layout would be 40 bytes, and writing 40 bytes overruns the host's variable by
///         4 - this is the defect of the official managed mirror, which declares the structure without packing and copies
///         it out with a structure-marshalling call.
///     </para>
///     <para>
///         <b>Evidence.</b> Field order and field widths: the private init structure of the official managed bootstrap
///         (<c>c# template/SDK/CESDK.cs</c>, CE 7.7.0.10621) - <i>verified</i>. Packing: the host-side record is declared
///         packed in the public 7.5 host source - <i>inferred</i> for 7.7, whose host is closed source; packing is also
///         the conservative choice, because a 36-byte write is correct whether the host reserved 36 or 40 bytes.
///         Callback conventions and shapes: the delegate declarations of the same bootstrap file (<c>stdcall</c>,
///         4-byte boolean results) - <i>verified</i>.
///     </para>
///     <para>
///         <b>Usage contract.</b> The host owns the memory; the plugin only writes the five fields, through a pointer
///         (<c>PluginInitRecord* record = (PluginInitRecord*)args;</c>), during the bootstrap call. The host calls the
///         bootstrap twice per plugin (name query, then load), so the writes must be repeatable. The record's address
///         has no alignment guarantee that matters: all accesses through this type are unaligned-safe because of the
///         packing.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct PluginInitRecord
{
	/// <summary>
	///     NUL-terminated plugin name in the process ANSI code page (offset 0).
	/// </summary>
	/// <remarks>
	///     Ownership: the plugin allocates it and must keep it valid <b>for the lifetime of the process</b>; the host
	///     may read through this pointer after the bootstrap call has returned and never frees it. Allocate once in
	///     native memory and reuse the same pointer on the second bootstrap call. Evidence: the official bootstrap
	///     produces the buffer once with an ANSI string conversion into unmanaged memory and never releases it
	///     (<i>verified</i>).
	/// </remarks>
	public byte* Name;

	/// <summary>
	///     Version query callback (offset 8). Receives a host-owned <see cref="PluginVersion" /> to fill in and the byte
	///     size the host reserved for it; returns true on success.
	/// </summary>
	/// <remarks>
	///     Must point at an <c>[UnmanagedCallersOnly]</c> static method declared with the <c>stdcall</c> convention,
	///     which stays valid for the lifetime of the process by construction. The callee must not let an exception
	///     escape. Called on Cheat Engine's main thread (<i>inferred</i> from the 7.5 host source).
	/// </remarks>
	public delegate* unmanaged[Stdcall]<PluginVersion*, int, Bool32> GetVersion;

	/// <summary>
	///     Enable callback (offset 16). Receives the address of a <see cref="ManagedExportedFunctions" /> record and
	///     the plugin id assigned by the host; returns true when the plugin enabled successfully.
	/// </summary>
	/// <remarks>
	///     The record lives in a host stack frame: copy it during the call (honouring
	///     <see cref="ManagedExportedFunctions.SizeOfExportedFunctions" />) and never keep the pointer. Same function
	///     pointer requirements and threading as <see cref="GetVersion" />. The plugin id is declared unsigned by the
	///     official bootstrap (<i>verified</i>) and by the 7.5 host (<i>inferred</i> for 7.7).
	/// </remarks>
	public delegate* unmanaged[Stdcall]<ManagedExportedFunctions*, uint, Bool32> EnablePlugin;

	/// <summary>
	///     Disable callback (offset 24). No arguments; returns true when the plugin disabled successfully.
	/// </summary>
	/// <remarks>
	///     The assembly is never unloaded: a later enable calls <see cref="EnablePlugin" /> again in the same loaded
	///     assembly, with all static state intact. Same function pointer requirements and threading as
	///     <see cref="GetVersion" />.
	/// </remarks>
	public delegate* unmanaged[Stdcall]<Bool32> DisablePlugin;

	/// <summary>
	///     SDK version the plugin was built against (offset 32): write <see cref="AbiConstants.SdkVersion" />.
	/// </summary>
	/// <remarks>
	///     Width 4 bytes (<i>verified</i> in the official bootstrap). Declared unsigned here because the 7.5 host
	///     declares it unsigned (<i>inferred</i> for 7.7); the official bootstrap uses a signed 32-bit field, which is
	///     the same bits for every valid value.
	/// </remarks>
	public uint Version;
}
