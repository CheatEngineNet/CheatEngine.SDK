using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin;

/// <summary>
///     A generated Lua global binding for Cheat Engine's <c>readInteger</c> global.
///     <c>CheatEngine.SDK.Engine.Generated.MemoryScalars</c> already wraps this call. This declaration shows the
///     <c>[LuaGlobal]</c> pattern a plugin uses for any Cheat Engine Lua function that has no wrapper, and it
///     exercises the LuaBindings generator and the protected call machinery of <c>CheatEngine.SDK.Lua</c> inside a real
///     plugin
///     assembly. <see cref="CheatEngineSdkLivePlugin.OnEnable" /> calls it and logs the result.
/// </summary>
internal static partial class MemoryBindings
{
	/// <summary>Reads a signed 32-bit integer at <paramref name="address" /> through Cheat Engine's <c>readInteger</c> global.</summary>
	/// <param name="address">The address to read.</param>
	/// <param name="value">The value read, or 0 when the address is not readable.</param>
	/// <returns><see langword="false" /> when the address is not readable (Cheat Engine returned <c>nil</c>).</returns>
	/// <remarks>
	///     Cheat Engine returns an unsigned value unless <c>readInteger</c>'s optional second argument is
	///     <see langword="true" />. Keep that host-specific flag private so callers cannot accidentally request an
	///     unsigned value that the <see langword="int" /> result marshaller rejects for negative values.
	/// </remarks>
	internal static bool TryReadInt32(nuint address, out int value)
	{
		value = 0;
		return TryReadInt32Raw(address, true, out value);
	}

	[LuaGlobal("readInteger")]
	private static partial bool TryReadInt32Raw(nuint address, bool signed, out int value);
}
