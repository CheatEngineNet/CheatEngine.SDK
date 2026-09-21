using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     The physically contiguous prefix of the classic <c>ExportedFunctions</c> table.
/// </summary>
/// <remarks>
///     <para>
///         Internal by design. Cheat Engine owns this table and supplies it only to the native
///         <c>CEPlugin_InitializePlugin</c> export. A future facade must copy only a verified prefix during that call,
///         validate <see cref="SizeOfExportedFunctions" />, and impose its own lifetime, main-thread, and failure policy.
///     </para>
///     <para>
///         <b>
///             Evidence status: source-indexed C header plus compiled-transcription fixture for x64 layout.
///         </b>
///         The fields through
///         <see cref="GetAddressFromPointer" /> are the contiguous C-header-declared part of
///         <c>ExportedFunctions</c> in the pinned historical <c>cepluginsdk.h</c>. The MSVC x64 fixture validates the
///         physical 144-byte transcription, not a live host. All direct functions are declared <c>__stdcall</c> in the
///         C header. That declaration does not itself make an individual slot callable: conflicting and historically
///         null slots remain opaque below.
///     </para>
///     <para>
///         The next native field is <c>ReadProcessMemory</c>, documented by the header as a pointer to a pointer that
///         can be hooked. That hook-bearing suffix, Delphi object references, and all later capabilities are purposely
///         excluded from this type. Their ABI and ownership must be introduced with a dedicated dangerous facade.
///     </para>
/// </remarks>
[SuppressMessage("Meziantou.Analyzer", "MA0182",
    Justification =
        "This intentionally internal ABI prefix is retained as a CE 7.7 C-header contract, exercised by friend-assembly layout tests, and verified against the native-fixture contract. It remains until a safe classic hosting facade owns the host table.")]
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ExportedFunctionsPrefix
{
    /// <summary>Number of bytes the host initialized in the complete table (offset 0).</summary>
    public int SizeOfExportedFunctions;

    /// <summary>Displays a host message (offset 8 on x64).</summary>
    public delegate* unmanaged[Stdcall]<byte*, void> ShowMessage;

    /// <summary>Registers a classic plugin function (offset 16 on x64).</summary>
    public delegate* unmanaged[Stdcall]<int, PluginType, void*, int> RegisterFunction;

    /// <summary>Unregisters a classic plugin function (offset 24 on x64).</summary>
    public delegate* unmanaged[Stdcall]<int, int, Bool32> UnregisterFunction;

    /// <summary>Pointer to the host's current process identifier (offset 32 on x64).</summary>
    public uint* OpenedProcessId;

    /// <summary>Pointer to the host's current process handle (offset 40 on x64).</summary>
    public void** OpenedProcessHandle;

    /// <summary>Gets Cheat Engine's main window handle (offset 48 on x64).</summary>
    public delegate* unmanaged[Stdcall]<void*> GetMainWindowHandle;

    /// <summary>Runs an auto-assembler script (offset 56 on x64).</summary>
    public delegate* unmanaged[Stdcall]<byte*, Bool32> AutoAssemble;

    /// <summary>Assembles one instruction at an address (offset 64 on x64).</summary>
    public delegate* unmanaged[Stdcall]<nuint, byte*, byte*, int, int*, Bool32> Assembler;

    /// <summary>Disassembles an instruction at an address (offset 72 on x64).</summary>
    public delegate* unmanaged[Stdcall]<nuint, byte*, int, Bool32> Disassembler;

    /// <summary>Requests a register change at an address (offset 80 on x64).</summary>
    public delegate* unmanaged[Stdcall]<nuint, RegisterModificationInfo*, Bool32> ChangeRegistersAtAddress;

    /// <summary>Injects a DLL and invokes its exported function (offset 88 on x64).</summary>
    public delegate* unmanaged[Stdcall]<byte*, byte*, Bool32> InjectDll;

    /// <summary>Creates a memory freeze and returns its host identifier (offset 96 on x64).</summary>
    public delegate* unmanaged[Stdcall]<nuint, int, int> FreezeMemory;

    /// <summary>Removes a memory freeze by host identifier (offset 104 on x64).</summary>
    public delegate* unmanaged[Stdcall]<int, Bool32> UnfreezeMemory;

    /// <summary>Opaque address of the historically nullable <c>FixMem</c> slot (offset 112 on x64).</summary>
    /// <remarks>
    ///     The historical Pascal host initializes this slot to <c>nil</c>. A non-null C-header declaration is not
    ///     evidence that the host exposes a callable implementation, so this SDK never invokes it.
    /// </remarks>
    public void* FixMemory;

    /// <summary>Writes the host process list to a caller-provided byte buffer (offset 120 on x64).</summary>
    public delegate* unmanaged[Stdcall]<byte*, int, Bool32> ProcessList;

    /// <summary>Reloads Cheat Engine settings (offset 128 on x64).</summary>
    public delegate* unmanaged[Stdcall]<Bool32> ReloadSettings;

    /// <summary>Opaque address of the conflicting <c>GetAddressFromPointer</c> slot (offset 136 on x64).</summary>
    /// <remarks>
    ///     The C header returns <c>UINT_PTR</c>, whereas the historical Pascal declaration returns a 32-bit
    ///     <c>dword</c>. The SDK therefore preserves only the physical slot and does not publish or invoke a
    ///     pointer-chain signature until a controlled CE 7.7 host canary resolves the return width.
    /// </remarks>
    public void* GetAddressFromPointer;
}
