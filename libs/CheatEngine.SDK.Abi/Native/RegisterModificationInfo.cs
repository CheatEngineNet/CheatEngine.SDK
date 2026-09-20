using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Classic plugin SDK register-change request record for an x64 breakpoint.
/// </summary>
/// <remarks>
///     <para>
///         Internal and dangerous by design: a host callback can use this record to alter a debugged thread's
///         registers and flags. It is present solely as the argument of the internal classic exported-functions prefix;
///         no public API accepts it.
///     </para>
///     <para>
///         <b>Evidence status: ExactInstalledFile for fields; InferredUntilFixture for x64 offsets.</b> This is the AMD64
///         branch of
///         <c>REGISTERMODIFICATIONINFO</c> in the <c>cepluginsdk.h</c> distributed with Cheat Engine 7.7.0.10621 x64
///         (SHA-256 <c>9C0E31BB753D782CE20710D19828F4E97B4371C8733ABD0C5C6F7F485306FB28</c>). The installed Pascal SDK
///         contains the matching <c>TRegisterModificationBP64</c> field sequence (SHA-256
///         <c>CDA5269F441120E5A3BFF2F87E289CD71DE9158CA2A619C7D0A734EB98EE6052</c>). Both use 4-byte Windows
///         <c>BOOL</c> fields and pointer-sized x64 register values.
///     </para>
///     <para>
///         Every <c>Change*</c> field selects whether the paired <c>New*</c> value is applied. The record is supplied
///         only for the duration of the host call and is never retained by this SDK.
///     </para>
/// </remarks>
[SuppressMessage("Meziantou.Analyzer", "MA0182",
    Justification =
        "This intentionally internal C-header mirror is the register-change function-pointer argument in ExportedFunctionsPrefix, exercised by friend-assembly layout tests, and verified against the native-fixture contract. It remains internal until a safe facade owns this dangerous host-call contract.")]
[StructLayout(LayoutKind.Sequential)]
internal struct RegisterModificationInfo
{
    /// <summary>Address at which Cheat Engine should apply the requested changes (offset 0 on x64).</summary>
    public nuint Address;

    /// <summary>Whether to replace the EAX register (offset 8 on x64).</summary>
    public Bool32 ChangeEax;

    /// <summary>Whether to replace the EBX register (offset 12 on x64).</summary>
    public Bool32 ChangeEbx;

    /// <summary>Whether to replace the ECX register (offset 16 on x64).</summary>
    public Bool32 ChangeEcx;

    /// <summary>Whether to replace the EDX register (offset 20 on x64).</summary>
    public Bool32 ChangeEdx;

    /// <summary>Whether to replace the ESI register (offset 24 on x64).</summary>
    public Bool32 ChangeEsi;

    /// <summary>Whether to replace the EDI register (offset 28 on x64).</summary>
    public Bool32 ChangeEdi;

    /// <summary>Whether to replace the EBP register (offset 32 on x64).</summary>
    public Bool32 ChangeEbp;

    /// <summary>Whether to replace the ESP register (offset 36 on x64).</summary>
    public Bool32 ChangeEsp;

    /// <summary>Whether to replace the EIP register (offset 40 on x64).</summary>
    public Bool32 ChangeEip;

    /// <summary>Whether to replace the R8 register (offset 44 on x64).</summary>
    public Bool32 ChangeR8;

    /// <summary>Whether to replace the R9 register (offset 48 on x64).</summary>
    public Bool32 ChangeR9;

    /// <summary>Whether to replace the R10 register (offset 52 on x64).</summary>
    public Bool32 ChangeR10;

    /// <summary>Whether to replace the R11 register (offset 56 on x64).</summary>
    public Bool32 ChangeR11;

    /// <summary>Whether to replace the R12 register (offset 60 on x64).</summary>
    public Bool32 ChangeR12;

    /// <summary>Whether to replace the R13 register (offset 64 on x64).</summary>
    public Bool32 ChangeR13;

    /// <summary>Whether to replace the R14 register (offset 68 on x64).</summary>
    public Bool32 ChangeR14;

    /// <summary>Whether to replace the R15 register (offset 72 on x64).</summary>
    public Bool32 ChangeR15;

    /// <summary>Whether to replace the carry flag (offset 76 on x64).</summary>
    public Bool32 ChangeCf;

    /// <summary>Whether to replace the parity flag (offset 80 on x64).</summary>
    public Bool32 ChangePf;

    /// <summary>Whether to replace the auxiliary carry flag (offset 84 on x64).</summary>
    public Bool32 ChangeAf;

    /// <summary>Whether to replace the zero flag (offset 88 on x64).</summary>
    public Bool32 ChangeZf;

    /// <summary>Whether to replace the sign flag (offset 92 on x64).</summary>
    public Bool32 ChangeSf;

    /// <summary>Whether to replace the overflow flag (offset 96 on x64).</summary>
    public Bool32 ChangeOf;

    /// <summary>New EAX value (offset 104 on x64).</summary>
    public nuint NewEax;

    /// <summary>New EBX value (offset 112 on x64).</summary>
    public nuint NewEbx;

    /// <summary>New ECX value (offset 120 on x64).</summary>
    public nuint NewEcx;

    /// <summary>New EDX value (offset 128 on x64).</summary>
    public nuint NewEdx;

    /// <summary>New ESI value (offset 136 on x64).</summary>
    public nuint NewEsi;

    /// <summary>New EDI value (offset 144 on x64).</summary>
    public nuint NewEdi;

    /// <summary>New EBP value (offset 152 on x64).</summary>
    public nuint NewEbp;

    /// <summary>New ESP value (offset 160 on x64).</summary>
    public nuint NewEsp;

    /// <summary>New EIP value (offset 168 on x64).</summary>
    public nuint NewEip;

    /// <summary>New R8 value (offset 176 on x64).</summary>
    public nuint NewR8;

    /// <summary>New R9 value (offset 184 on x64).</summary>
    public nuint NewR9;

    /// <summary>New R10 value (offset 192 on x64).</summary>
    public nuint NewR10;

    /// <summary>New R11 value (offset 200 on x64).</summary>
    public nuint NewR11;

    /// <summary>New R12 value (offset 208 on x64).</summary>
    public nuint NewR12;

    /// <summary>New R13 value (offset 216 on x64).</summary>
    public nuint NewR13;

    /// <summary>New R14 value (offset 224 on x64).</summary>
    public nuint NewR14;

    /// <summary>New R15 value (offset 232 on x64).</summary>
    public nuint NewR15;

    /// <summary>New carry flag (offset 240 on x64).</summary>
    public Bool32 NewCf;

    /// <summary>New parity flag (offset 244 on x64).</summary>
    public Bool32 NewPf;

    /// <summary>New auxiliary carry flag (offset 248 on x64).</summary>
    public Bool32 NewAf;

    /// <summary>New zero flag (offset 252 on x64).</summary>
    public Bool32 NewZf;

    /// <summary>New sign flag (offset 256 on x64).</summary>
    public Bool32 NewSf;

    /// <summary>New overflow flag (offset 260 on x64).</summary>
    public Bool32 NewOf;
}
