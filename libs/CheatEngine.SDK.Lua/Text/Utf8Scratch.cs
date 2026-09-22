using System;
using System.Buffers;
using System.Text;

namespace CheatEngine.SDK.Lua.Text;

/// <summary>
///     UTF-16 to UTF-8 transcoding into a caller-provided stack buffer first, a pooled array only when the text does not
///     fit. This is the one place where a managed <see cref="string" /> or <see cref="ReadOnlySpan{T}" /> of
///     <see cref="char" /> becomes the bytes Lua stores.
/// </summary>
/// <remarks>
///     <para>
///         Usage:
///         <c>
///             Span&lt;byte&gt; scratch = stackalloc byte[Utf8Scratch.StackBufferSize]; using Utf8Scratch utf8 =
///             Utf8Scratch.Encode(text, scratch);
///         </c>
///         then read <see cref="Bytes" />. <see cref="Dispose" /> returns the pooled array, if one was rented; the bytes
///         must
///         not be used after that.
///     </para>
///     <para>
///         Encoding follows <see cref="Encoding.UTF8" /> without a byte order mark: a lone surrogate becomes U+FFFD
///         (<c>EF BF BD</c>), nothing throws. The pool is <see cref="ArrayPool{T}.Shared" />; the rented array may be
///         larger
///         than the text and is never zeroed.
///     </para>
/// </remarks>
internal ref struct Utf8Scratch : IDisposable
{
	/// <summary>
	///     Size of the stack buffer the callers of this type allocate: room for 170 characters of any kind, or 512
	///     ASCII characters. Above that a pooled array is used. Kept under the 1 KiB that the runtime's unsafe-code
	///     guidance considers a reasonable <c>stackalloc</c> bound.
	/// </summary>
	public const int StackBufferSize = 512;

	private byte[]? _rented;

	private Utf8Scratch(byte[]? rented, ReadOnlySpan<byte> bytes)
	{
		_rented = rented;
		Bytes = bytes;
	}

	/// <summary>Gets the encoded bytes. Valid until <see cref="Dispose" />.</summary>
	public ReadOnlySpan<byte> Bytes
	{
		get;
		private set;
	}

	/// <summary>Gets a value indicating whether the text did not fit the stack buffer and a pooled array was rented.</summary>
	public readonly bool IsPooled => _rented is not null;

	/// <summary>
	///     Encodes <paramref name="text" />, into <paramref name="stackBuffer" /> when the worst case fits, else into a
	///     pooled array.
	/// </summary>
	/// <param name="text">The UTF-16 text; may be empty.</param>
	/// <param name="stackBuffer">
	///     A buffer the caller owns for the duration of the result, usually
	///     <c>stackalloc byte[StackBufferSize]</c>.
	/// </param>
	/// <returns>The transcoding result; dispose it when the bytes are no longer needed.</returns>
	public static Utf8Scratch Encode(ReadOnlySpan<char> text, Span<byte> stackBuffer)
	{
		if (text.IsEmpty)
		{
			return new Utf8Scratch(null, ReadOnlySpan<byte>.Empty);
		}

		// The worst case (every char a 3-byte sequence, plus the encoder's slack) is cheap to compute and lets the
		// common case skip the exact count.
		int worstCase = Encoding.UTF8.GetMaxByteCount(text.Length);
		if (worstCase <= stackBuffer.Length)
		{
			int written = Encoding.UTF8.GetBytes(text, stackBuffer);
			return new Utf8Scratch(null, stackBuffer[..written]);
		}

		int exact = Encoding.UTF8.GetByteCount(text);
		if (exact <= stackBuffer.Length)
		{
			int written = Encoding.UTF8.GetBytes(text, stackBuffer);
			return new Utf8Scratch(null, stackBuffer[..written]);
		}

		byte[] rented = ArrayPool<byte>.Shared.Rent(exact);
		int count = Encoding.UTF8.GetBytes(text, rented);
		return new Utf8Scratch(rented, new ReadOnlySpan<byte>(rented, 0, count));
	}

	/// <summary>Returns the pooled array, if any. Idempotent.</summary>
	public void Dispose()
	{
		byte[]? rented = _rented;
		_rented = null;
		Bytes = ReadOnlySpan<byte>.Empty;
		if (rented is not null)
		{
			ArrayPool<byte>.Shared.Return(rented);
		}
	}
}
