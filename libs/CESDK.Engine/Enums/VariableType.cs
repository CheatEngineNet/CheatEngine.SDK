using System.Diagnostics.CodeAnalysis;

namespace CESDK.Engine.Enums;

/// <summary>
///     The value types Cheat Engine scans for and stores in memory records (<c>vt*</c> in <c>defines.lua</c>). Passed
///     to <c>MemScan.firstScan</c> as <c>vartype</c>, read from and written to <c>MemoryRecord.Type</c> as a number
///     (<c>MemoryRecord.VarType</c> carries the same value as the <c>vt*</c> name; see <see cref="CEEnumNames" />).
/// </summary>
/// <remarks>
///     Values verified against <c>defines.lua</c> of Cheat Engine 7.7.0.10621. The member names are the domain's own
///     vocabulary (CE calls them byte, word, dword, ...), hence the naming suppression.
/// </remarks>
[SuppressMessage("Naming", "CA1720:Identifiers should not contain type names",
    Justification =
        "The members name Cheat Engine's value types (vtByte, vtSingle, vtDouble, vtString, vtPointer); renaming them would hide the CE names they mirror.")]
public enum VariableType
{
    /// <summary>One byte. CE: <c>vtByte</c>.</summary>
    Byte = 0,

    /// <summary>Two bytes. CE: <c>vtWord</c>.</summary>
    Word = 1,

    /// <summary>Four bytes, the default of <c>firstScan</c>. CE: <c>vtDword</c>.</summary>
    Dword = 2,

    /// <summary>Eight bytes. CE: <c>vtQword</c>.</summary>
    Qword = 3,

    /// <summary>Single-precision float. CE: <c>vtSingle</c>.</summary>
    Single = 4,

    /// <summary>Double-precision float. CE: <c>vtDouble</c>.</summary>
    Double = 5,

    /// <summary>A text string. CE: <c>vtString</c>.</summary>
    String = 6,

    /// <summary>
    ///     A UTF-16 string. CE: <c>vtWideString</c>; the alias <c>vtUnicodeString</c> has the same value and is only used
    ///     by CE's type guesser.
    /// </summary>
    WideString = 7,

    /// <summary>An array of bytes. CE: <c>vtByteArray</c>.</summary>
    ByteArray = 8,

    /// <summary>A binary (bit field) value. CE: <c>vtBinary</c>.</summary>
    Binary = 9,

    /// <summary>Every type at once (scan only). CE: <c>vtAll</c>.</summary>
    All = 10,

    /// <summary>An auto assembler script (memory record only). CE: <c>vtAutoAssembler</c>.</summary>
    AutoAssembler = 11,

    /// <summary>A pointer; only used by CE's type guesser and by structure dissection. CE: <c>vtPointer</c>.</summary>
    Pointer = 12,

    /// <summary>A user-defined custom type. CE: <c>vtCustom</c>.</summary>
    Custom = 13,

    /// <summary>A group header in the address list (memory record only). CE: <c>vtGrouped</c>.</summary>
    Grouped = 14
}
