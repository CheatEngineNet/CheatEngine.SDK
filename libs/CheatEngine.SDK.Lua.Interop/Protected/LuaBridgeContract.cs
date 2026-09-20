using System.Runtime.CompilerServices;

namespace CheatEngine.SDK.Lua.Interop.Protected;

// This is the native C11 contract, not a managed object model. Explicit offsets keep an ABI change
// visible in both Unsafe.SizeOf and the source diff before it can cross the LibraryImport boundary.
[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit, Size = Size)]
internal struct LuaBridgeContract
{
    internal const uint ExpectedMagic = 0x4345534B;
    internal const uint ExpectedLegacyAbiVersion = 1;
    internal const ushort ExpectedMajor = 1;
    internal const ushort MinimumMinor = 1;
    internal const int Size = 32;

    [global::System.Runtime.InteropServices.FieldOffset(0)]
    internal uint Magic;

    [global::System.Runtime.InteropServices.FieldOffset(4)]
    internal uint ContractSize;

    [global::System.Runtime.InteropServices.FieldOffset(8)]
    internal ulong SupportedOperations;

    [global::System.Runtime.InteropServices.FieldOffset(16)]
    internal uint ExportTableSize;

    [global::System.Runtime.InteropServices.FieldOffset(20)]
    internal ushort AbiMajor;

    [global::System.Runtime.InteropServices.FieldOffset(22)]
    internal ushort AbiMinor;

    [global::System.Runtime.InteropServices.FieldOffset(24)]
    internal byte PointerSize;

    [global::System.Runtime.InteropServices.FieldOffset(25)]
    internal byte LuaIntegerSize;

    [global::System.Runtime.InteropServices.FieldOffset(26)]
    internal byte SizeTSize;

    [global::System.Runtime.InteropServices.FieldOffset(27)]
    internal byte Reserved;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly bool IsCompatible()
    {
        return Magic == ExpectedMagic &&
               ContractSize == (uint)Unsafe.SizeOf<LuaBridgeContract>() &&
               AbiMajor == ExpectedMajor &&
               AbiMinor >= MinimumMinor &&
               PointerSize == (byte)global::System.IntPtr.Size &&
               LuaIntegerSize == (byte)Unsafe.SizeOf<lua_Integer>() &&
               SizeTSize == (byte)Unsafe.SizeOf<nuint>() &&
               ExportTableSize == (uint)Unsafe.SizeOf<LuaProtectedExports>() &&
               Reserved == 0 &&
               (SupportedOperations & LuaProtectedOperationContract.RequiredBitmap) ==
               LuaProtectedOperationContract.RequiredBitmap;
    }
}
