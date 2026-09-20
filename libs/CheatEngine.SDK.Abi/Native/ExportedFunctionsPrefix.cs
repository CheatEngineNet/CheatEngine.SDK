using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     The direct-call prefix of the classic <c>ExportedFunctions</c> table.
/// </summary>
/// <remarks>
///     <para>
///         Internal by design. Cheat Engine owns this table and supplies it only to the native
///         <c>CEPlugin_InitializePlugin</c> export. A future facade must copy only a verified prefix during that call,
///         validate <see cref="SizeOfExportedFunctions" />, and impose its own lifetime, main-thread, and failure policy.
///     </para>
///     <para>
///         <b>Evidence status: ExactInstalledFile for fields and calling conventions; InferredUntilFixture for x64 offsets.</b> The fields through
///         <see cref="GetAddressFromPointer" /> are the contiguous direct-function part of
///         <c>ExportedFunctions</c> in the <c>cepluginsdk.h</c> distributed with Cheat Engine 7.7.0.10621 x64
///         (SHA-256 <c>9C0E31BB753D782CE20710D19828F4E97B4371C8733ABD0C5C6F7F485306FB28</c>). The installed Pascal SDK
///         has the same prefix (SHA-256 <c>CDA5269F441120E5A3BFF2F87E289CD71DE9158CA2A619C7D0A734EB98EE6052</c>). All
///         direct functions are declared <c>__stdcall</c> in the C header.
///     </para>
///     <para>
///         The next native field is <c>ReadProcessMemory</c>, documented by the header as a pointer to a pointer that
///         can be hooked. That hook-bearing suffix, Delphi object references, and all later capabilities are purposely
///         excluded from this type. Their ABI and ownership must be introduced with a dedicated dangerous facade.
///     </para>
/// </remarks>
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
    /// <summary>Applies pending freezes (offset 112 on x64).</summary>
    public delegate* unmanaged[Stdcall]<Bool32> FixMemory;
    /// <summary>Writes the host process list to a caller-provided byte buffer (offset 120 on x64).</summary>
    public delegate* unmanaged[Stdcall]<byte*, int, Bool32> ProcessList;
    /// <summary>Reloads Cheat Engine settings (offset 128 on x64).</summary>
    public delegate* unmanaged[Stdcall]<Bool32> ReloadSettings;
    /// <summary>Resolves a base address through a caller-provided 32-bit offset array (offset 136 on x64).</summary>
    public delegate* unmanaged[Stdcall]<nuint, int, int*, nuint> GetAddressFromPointer;
}
