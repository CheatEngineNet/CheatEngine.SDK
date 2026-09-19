namespace CheatEngine.SDK.Engine.Enums;

/// <summary>
///     The alignment rule a scan applies to candidate addresses (<c>fsm*</c> in <c>defines.lua</c>, Cheat Engine's
///     "fast scan method"): the <c>alignmenttype</c> argument of <c>MemScan.firstScan</c>, of <c>AOBScan</c> and of
///     its variants, and the <c>MemScan.Fastscanmethod</c> property. The companion <c>alignmentparam</c> is a string:
///     the divisor for <see cref="Aligned" />, the required trailing digits for <see cref="LastDigits" />.
/// </summary>
/// <remarks>
///     Values verified against <c>defines.lua</c> of Cheat Engine 7.7.0.10621.
/// </remarks>
public enum FastScanMethod
{
    /// <summary>Check every address. CE: <c>fsmNotAligned</c>.</summary>
    NotAligned = 0,

    /// <summary>Only addresses divisible by the alignment parameter. CE: <c>fsmAligned</c>.</summary>
    Aligned = 1,

    /// <summary>Only addresses whose hexadecimal text ends with the alignment parameter. CE: <c>fsmLastDigits</c>.</summary>
    LastDigits = 2
}
