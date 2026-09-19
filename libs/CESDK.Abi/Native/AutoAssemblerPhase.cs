namespace CESDK.Abi.Native;

/// <summary>
///     The stage of an auto-assembler run during which an <see cref="AutoAssemblerPluginInit.Callback" /> is invoked.
/// </summary>
/// <remarks>
///     <para>
///         <b>Evidence (verified, two sources agree on names and values):</b> the phase enumeration of
///         <c>cepluginsdk.h</c> and of <c>cepluginsdk.pas</c> (CE 7.7.0.10621). Members keep the upstream names without
///         their <c>aa</c> prefix. Neither file explains what distinguishes the stages; the member descriptions below
///         only restate what the names say.
///     </para>
///     <para>
///         <b>Width.</b> 4 bytes here, matching the C enumeration; the value only travels by value as a callback
///         argument. The 7.5 host passes it as a plain 32-bit integer (<i>inferred</i> for 7.7), so all 32 bits are
///         defined on arrival.
///     </para>
/// </remarks>
public enum AutoAssemblerPhase
{
    /// <summary>Upstream <c>aaInitialize</c> (0): start of a run.</summary>
    Initialize = 0,

    /// <summary>Upstream <c>aaPhase1</c> (1): first pass.</summary>
    Phase1 = 1,

    /// <summary>Upstream <c>aaPhase2</c> (2): second pass.</summary>
    Phase2 = 2,

    /// <summary>Upstream <c>aaFinalize</c> (3): end of a run.</summary>
    Finalize = 3
}
