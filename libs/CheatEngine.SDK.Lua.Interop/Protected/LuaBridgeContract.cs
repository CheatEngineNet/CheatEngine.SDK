using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Lua.Interop.Protected;

// This is the native C11 contract, not a managed object model. Explicit offsets keep an ABI change
// visible in both Unsafe.SizeOf and the source diff before it can cross the LibraryImport boundary.
[StructLayout(LayoutKind.Explicit, Size = Size)]
internal struct LuaBridgeContract
{
	internal const uint ExpectedMagic = 0x4345534B;
	internal const uint ExpectedLegacyAbiVersion = 1;
	internal const ushort ExpectedMajor = 1;
	internal const ushort MinimumMinor = 1;
	internal const int Size = 32;

	[FieldOffset(0)] internal uint Magic;

	[FieldOffset(4)] internal uint ContractSize;

	[FieldOffset(8)] internal ulong SupportedOperations;

	[FieldOffset(16)] internal uint ExportTableSize;

	[FieldOffset(20)] internal ushort AbiMajor;

	[FieldOffset(22)] internal ushort AbiMinor;

	[FieldOffset(24)] internal byte PointerSize;

	[FieldOffset(25)] internal byte LuaIntegerSize;

	[FieldOffset(26)] internal byte SizeTSize;

	[FieldOffset(27)] internal byte Reserved;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal readonly bool IsCompatible()
	{
		return Magic == ExpectedMagic &&
			   ContractSize == (uint) Unsafe.SizeOf<LuaBridgeContract>() &&
			   AbiMajor == ExpectedMajor &&
			   AbiMinor >= MinimumMinor &&
			   PointerSize == (byte) lua_KContext.Size &&
			   LuaIntegerSize == (byte) Unsafe.SizeOf<lua_Integer>() &&
			   SizeTSize == (byte) Unsafe.SizeOf<nuint>() &&
			   ExportTableSize == (uint) Unsafe.SizeOf<LuaProtectedExports>() &&
			   Reserved == 0 &&
			   (SupportedOperations & LuaProtectedOperationContract.RequiredBitmap) ==
			   LuaProtectedOperationContract.RequiredBitmap;
	}
}
