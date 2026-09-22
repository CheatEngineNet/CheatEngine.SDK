using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Text;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     <see cref="string" /> as a Lua string: the convenience marshaller. Pushing transcodes UTF-16 to UTF-8 through a
///     stack buffer (a pooled array above <see cref="Utf8Scratch.StackBufferSize" /> bytes) and allocates nothing on the
///     managed side; reading decodes UTF-8 into a new <see cref="string" />, which allocates, so hot paths read through
///     <see cref="Utf8Marshaller" /> or <see cref="LuaState.TryCopyUtf8" /> instead.
/// </summary>
/// <remarks>
///     The marshalled type is <see langword="string" />, not <see langword="string" />?, so that generic code constrained
///     on
///     <c>ILuaMarshaller&lt;T&gt;</c> can name the type a declaration names; <see cref="Push" /> still accepts
///     <see langword="null" /> (pushed as <c>nil</c>) and <see cref="TryRead" /> is annotated so that its result is
///     non-null exactly when it returns <see langword="true" />. Lone surrogates in the input become U+FFFD; invalid UTF-8
///     from Lua becomes U+FFFD. Reading is strict: a number is not converted.
/// </remarks>
public readonly struct StringMarshaller : ILuaMarshaller<string>
{
	/// <summary>Pushes <paramref name="value" />; <see langword="null" /> is pushed as <c>nil</c>.</summary>
	/// <inheritdoc />
	[LuaStackEffect(1)]
	public static void Push(LuaState state, string? value)
	{
		if (value is null)
		{
			state.PushNil();
			return;
		}

		state.PushString(value.AsSpan());
	}

	/// <inheritdoc />
	[LuaStackEffect(0)]
	public static bool TryRead(LuaState state, int index, [MaybeNullWhen(false)] out string value)
	{
		return state.TryReadString(index, out value);
	}
}
