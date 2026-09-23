using System;
using System.Buffers;
using System.Text;

using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>
///     Bounded, never-parsed copies of Cheat Engine's Auto Assembler host text (rejection detail, compilation warnings).
/// </summary>
/// <remarks>
///     The bytes are bounded <em>before</em> decoding: at most <c>maxBytes</c> UTF-8 bytes are read from the Lua string,
///     and a scalar split by the cut is removed with <see cref="Rune.DecodeLastFromUtf8" />
///     (https://learn.microsoft.com/dotnet/api/system.text.rune.decodelastfromutf8), so a truncated copy never ends in a
///     replacement character that Cheat Engine did not send. Embedded NUL bytes are kept. The text is diagnostic only: no
///     outcome category is ever derived from it.
/// </remarks>
internal static class AutoAssemblerHostText
{
	// A UTF-8 scalar is at most four bytes, so a cut can leave at most three bytes of an incomplete scalar.
	private const int MaxSplitScalarBytes = 3;

	/// <summary>
	///     Copies the string at <paramref name="index" /> when <paramref name="capture" /> is set; any other value, or no
	///     capture, gives <see langword="default" />.
	/// </summary>
	internal static AutoAssemblerHostTextCopy Copy(LuaState state, int index, bool capture, int maxBytes)
	{
		if (!capture || !state.TryReadUtf8(index, out ReadOnlySpan<byte> utf8))
		{
			return default;
		}

		bool truncated = utf8.Length > maxBytes;
		ReadOnlySpan<byte> kept = truncated ? TrimSplitScalar(utf8[..maxBytes]) : utf8;
		return new AutoAssemblerHostTextCopy(Encoding.UTF8.GetString(kept), truncated);
	}

	private static ReadOnlySpan<byte> TrimSplitScalar(ReadOnlySpan<byte> bytes)
	{
		int trimmed = 0;
		while (!bytes.IsEmpty && trimmed < MaxSplitScalarBytes)
		{
			OperationStatus status = Rune.DecodeLastFromUtf8(bytes, out _, out int consumed);
			if (status == OperationStatus.Done)
			{
				break;
			}

			int remove = Math.Clamp(consumed, 1, MaxSplitScalarBytes - trimmed);
			bytes = bytes[..^remove];
			trimmed += remove;
		}

		return bytes;
	}
}
