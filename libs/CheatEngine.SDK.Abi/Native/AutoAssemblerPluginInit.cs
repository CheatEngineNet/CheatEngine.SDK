using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Registration record for <see cref="PluginType.AutoAssembler" /> (upstream type 8): a preprocessor that sees,
///     and may rewrite, each line of an auto-assembler script.
/// </summary>
/// <remarks>
///     <para><b>Layout (64-bit): 8 bytes.</b> <see cref="Callback" /> 0.</para>
///     <para>
///         <b>Evidence (verified, two sources agree):</b> the type-8 init structure and callback of <c>cepluginsdk.h</c>
///         and of <c>cepluginsdk.pas</c> (CE 7.7.0.10621): one pointer-sized field; callback <c>stdcall</c>, no result,
///         three arguments (pointer to the line's string pointer, phase, 32-bit id).
///     </para>
///     <para>
///         <b>
///             Version dependence (<i>inferred</i> from the 7.5 host source):
///         </b>
///         the three-argument shape is what a plugin reporting <see cref="AbiConstants.SdkVersion" /> receives;
///         a plugin reporting version 5 gets a two-argument variant without the id. This SDK only reports 6.
///     </para>
///     <para>Passed by address to the <c>RegisterFunction</c> slot of the classic table. Native load path only.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct AutoAssemblerPluginInit
{
    /// <summary>
    ///     Invoked per script line and per phase (offset 0). Arguments: in/out pointer to the ANSI text of the line,
    ///     the current <see cref="AutoAssemblerPhase" />, and a 32-bit id whose meaning is not documented upstream
    ///     (presumably it identifies the run, so that state can be kept between phases).
    /// </summary>
    /// <remarks>
    ///     A replacement line written through the first argument stays owned by the plugin and has to outlive the
    ///     call. Thread affinity is not documented upstream: assume any thread. Must stay valid until the function is
    ///     unregistered. Must not let an exception escape.
    /// </remarks>
    public delegate* unmanaged[Stdcall]<byte**, AutoAssemblerPhase, int, void> Callback;
}
