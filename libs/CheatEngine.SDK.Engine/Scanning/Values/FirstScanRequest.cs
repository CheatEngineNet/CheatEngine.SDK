using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     The fourteen positional arguments of Cheat Engine 7.7's
///     <c>MemScan.firstScan(scanoption, vartype, roundingtype, input1, input2, startAddress, stopAddress,
///     protectionflags, alignmenttype, alignmentparam, isHexadecimalInput, isNotABinaryString, isunicodescan,
///     iscasesensitive)</c> method.
/// </summary>
/// <remarks>
///     The exact order is documented in CE 7.7.0.10621 <c>celua.txt</c> lines 2568-2613. Start and stop are modelled as
///     target <see cref="Address" /> values even though the Lua document calls them <c>integer</c>: a 64-bit target address
///     must not be narrowed to a managed <see cref="int" />. This value intentionally does not validate every semantic
///     combination CE may accept; the session validates the documented first-scan option set (including CE's
///     non-contiguous <c>vtGrouped</c> value) and non-null string values.
/// </remarks>
public readonly struct FirstScanRequest
{
    /// <summary>Initializes a complete first-scan request.</summary>
    /// <param name="scanOption">The initial comparison mode.</param>
    /// <param name="variableType">The value type to scan.</param>
    /// <param name="roundingType">The floating-point comparison rule.</param>
    /// <param name="input1">The primary scan text; use an empty string when the option does not need it.</param>
    /// <param name="input2">The secondary scan text; use an empty string when the option does not need it.</param>
    /// <param name="startAddress">The first target address to consider.</param>
    /// <param name="stopAddress">The last target address to consider.</param>
    /// <param name="protectionFlags">The CE protection-flags text, such as <c>+W-C</c>.</param>
    /// <param name="fastScanMethod">The address-alignment rule.</param>
    /// <param name="alignmentParameter">The alignment rule's CE string parameter.</param>
    /// <param name="isHexadecimalInput">Whether CE interprets the inputs as hexadecimal text.</param>
    /// <param name="isNotBinaryString">Whether a binary type's input is decimal rather than a bit string.</param>
    /// <param name="isUnicodeScan">Whether a string scan uses UTF-16 rather than CE's normal UTF-8 mode.</param>
    /// <param name="isCaseSensitive">Whether a string scan matches case.</param>
    public FirstScanRequest(
        ScanOption scanOption,
        VariableType variableType,
        RoundingType roundingType,
        string input1,
        string input2,
        Address startAddress,
        Address stopAddress,
        string protectionFlags,
        FastScanMethod fastScanMethod,
        string alignmentParameter,
        bool isHexadecimalInput,
        bool isNotBinaryString,
        bool isUnicodeScan,
        bool isCaseSensitive)
    {
        ScanOption = scanOption;
        VariableType = variableType;
        RoundingType = roundingType;
        Input1 = input1;
        Input2 = input2;
        StartAddress = startAddress;
        StopAddress = stopAddress;
        ProtectionFlags = protectionFlags;
        FastScanMethod = fastScanMethod;
        AlignmentParameter = alignmentParameter;
        IsHexadecimalInput = isHexadecimalInput;
        IsNotBinaryString = isNotBinaryString;
        IsUnicodeScan = isUnicodeScan;
        IsCaseSensitive = isCaseSensitive;
    }

    /// <summary>Gets the CE first-scan comparison mode.</summary>
    public ScanOption ScanOption { get; }

    /// <summary>Gets the CE value type.</summary>
    public VariableType VariableType { get; }

    /// <summary>Gets the CE floating-point rounding rule.</summary>
    public RoundingType RoundingType { get; }

    /// <summary>Gets the primary scan text.</summary>
    public string Input1 { get; }

    /// <summary>Gets the secondary scan text.</summary>
    public string Input2 { get; }

    /// <summary>Gets the first target address to consider.</summary>
    public Address StartAddress { get; }

    /// <summary>Gets the last target address to consider.</summary>
    public Address StopAddress { get; }

    /// <summary>Gets CE's protection-flags text.</summary>
    public string ProtectionFlags { get; }

    /// <summary>Gets CE's fast-scan alignment method.</summary>
    public FastScanMethod FastScanMethod { get; }

    /// <summary>Gets CE's string alignment parameter.</summary>
    public string AlignmentParameter { get; }

    /// <summary>Gets whether CE parses the input text as hexadecimal.</summary>
    public bool IsHexadecimalInput { get; }

    /// <summary>Gets whether a binary input is decimal rather than a bit string.</summary>
    public bool IsNotBinaryString { get; }

    /// <summary>Gets whether a string scan uses UTF-16.</summary>
    public bool IsUnicodeScan { get; }

    /// <summary>Gets whether a string scan is case-sensitive.</summary>
    public bool IsCaseSensitive { get; }

    /// <summary>Builds an exact-value request over the full 64-bit target address range.</summary>
    /// <param name="variableType">The value type to scan.</param>
    /// <param name="input">The exact value text.</param>
    /// <returns>A request using CE's normal rounded, non-aligned and non-hexadecimal settings.</returns>
    public static FirstScanRequest ExactValue(VariableType variableType, string input)
    {
        return new FirstScanRequest(
            ScanOption.ExactValue,
            variableType,
            RoundingType.Rounded,
            input,
            string.Empty,
            Address.Zero,
            new Address(ulong.MaxValue),
            string.Empty,
            FastScanMethod.NotAligned,
            string.Empty,
            false,
            false,
            false,
            false);
    }
}
