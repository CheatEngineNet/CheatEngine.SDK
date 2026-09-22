using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Interop.Api;

namespace CheatEngine.SDK.Lua.Interop.Types;

/// <summary>
///     One entry of a registration array for <see cref="LuaApi.luaL_setfuncs" />. The array ends with an entry whose
///     <see cref="name" /> and <see cref="func" /> are both null.
/// </summary>
/// <remarks>
///     Lua copies the name into an interned string and keeps the function pointer, so the array itself only has to live
///     for the duration of the call (a <see langword="stackalloc" /> is fine); the function must stay callable for as long
///     as the
///     state can reach it, which is always true for a static <c>[UnmanagedCallersOnly]</c> method.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct luaL_Reg
{
	/// <summary>NUL-terminated field name, null in the terminating entry.</summary>
	public byte* name;

	/// <summary>The C function, null in the terminating entry.</summary>
	public lua_CFunction func;
}
