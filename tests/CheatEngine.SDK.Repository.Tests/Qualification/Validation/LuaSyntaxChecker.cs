using System.Runtime.InteropServices;
using System.Text;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>
///     Compiles Lua source with the committed Cheat Engine Lua 5.3 module (<c>native/cheat-engine/lua53-64.dll</c>) without
///     running it: <c>luaL_newstate</c>, <c>luaL_loadbufferx</c> in text mode, <c>lua_close</c>. Delegates over the
///     exports keep the test project free of unsafe code and of any project reference.
/// </summary>
internal static class LuaSyntaxChecker
{
	private const int LuaOk = 0;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate nint NewState();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int LoadBufferX(nint state, byte[] buffer, nuint size, byte[] name, byte[] mode);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate nint ToLString(nint state, int index, nint length);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void Close(nint state);

	/// <summary>Returns <see langword="null" /> when <paramref name="source" /> compiles, otherwise Lua's message.</summary>
	internal static string? Compile(string source, string chunkName)
	{
		nint library = NativeLibrary.Load(QualificationDocuments.Absolute("native/cheat-engine/lua53-64.dll"));
		try
		{
			NewState newState = Export<NewState>(library, "luaL_newstate");
			LoadBufferX load = Export<LoadBufferX>(library, "luaL_loadbufferx");
			ToLString toString = Export<ToLString>(library, "lua_tolstring");
			Close close = Export<Close>(library, "lua_close");
			nint state = newState();
			if (state == 0)
			{
				throw new InvalidOperationException("luaL_newstate returned no state.");
			}

			try
			{
				byte[] bytes = Encoding.UTF8.GetBytes(source);
				int status = load(state, bytes, (nuint) bytes.Length, Terminated("=" + chunkName), Terminated("t"));
				return status == LuaOk
					? null
					: $"luaL_loadbufferx status {status}: {Marshal.PtrToStringUTF8(toString(state, -1, 0))}";
			}
			finally
			{
				close(state);
			}
		}
		finally
		{
			NativeLibrary.Free(library);
		}
	}

	private static T Export<T>(nint library, string name)
		where T : Delegate
	{
		return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
	}

	private static byte[] Terminated(string text)
	{
		return Encoding.UTF8.GetBytes(text + "\0");
	}
}
