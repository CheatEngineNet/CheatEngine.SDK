using System;
using System.Text;

using CheatEngine.SDK.Lua.Callbacks;

namespace CheatEngine.SDK.Lua.Registration;

/// <summary>One static Lua global and the native thunk that a registration set publishes for it.</summary>
/// <remarks>
///     Entry construction is a cold registration-time operation. The UTF-8 name is copied so a generated caller can
///     pass an ordinary C# string without retaining a span or relying on reflection-based discovery.
/// </remarks>
public readonly struct LuaRegistrationEntry
{
	private readonly byte[] _utf8Name;

	/// <summary>Initializes one entry.</summary>
	/// <param name="name">The nonempty Lua global name.</param>
	/// <param name="function">The non-null native thunk to wrap and publish.</param>
	/// <exception cref="ArgumentException"><paramref name="name" /> is empty or <paramref name="function" /> is null.</exception>
	public LuaRegistrationEntry(string name, LuaNativeFunction function)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		if (function.IsNull)
		{
			throw new ArgumentException("The registration thunk is null.", nameof(function));
		}

		Name = name;
		_utf8Name = Encoding.UTF8.GetBytes(name);
		Function = function;
	}

	/// <summary>Gets the global name used in diagnostics and release reports.</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets the native thunk wrapped by the Lua registration helper.</summary>
	public LuaNativeFunction Function
	{
		get;
	}

	internal ReadOnlySpan<byte> Utf8Name => _utf8Name;
}
