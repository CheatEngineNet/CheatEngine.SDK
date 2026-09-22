using System.Text;

using CheatEngine.SDK.Lua.Text;

namespace CheatEngine.SDK.Lua.Tests.Text;

/// <summary>The transcoder that turns managed text into the bytes Lua stores. No Lua library involved.</summary>
public sealed class Utf8ScratchTests
{
	[Fact]
	public void Ascii_fits_the_stack_buffer_and_is_copied_byte_for_byte()
	{
		Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
		using Utf8Scratch utf8 = Utf8Scratch.Encode("readInteger", scratch);

		Assert.False(utf8.IsPooled);
		Assert.True(utf8.Bytes.SequenceEqual("readInteger"u8));
	}

	[Fact]
	public void Non_ascii_text_is_encoded_as_utf8()
	{
		// Two 2-byte sequences, one 3-byte sequence and one 4-byte sequence (a surrogate pair in UTF-16).
		Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
		using Utf8Scratch utf8 = Utf8Scratch.Encode("caf\u00E9 \u00FC \u20AC \uD83D\uDE00", scratch);

		byte[] expected =
			[0x63, 0x61, 0x66, 0xC3, 0xA9, 0x20, 0xC3, 0xBC, 0x20, 0xE2, 0x82, 0xAC, 0x20, 0xF0, 0x9F, 0x98, 0x80];
		Assert.True(utf8.Bytes.SequenceEqual(expected));
	}

	[Fact]
	public void Lone_surrogates_become_the_replacement_character_and_never_throw()
	{
		Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
		using Utf8Scratch high = Utf8Scratch.Encode("a\uD800b", scratch);
		Assert.True(high.Bytes.SequenceEqual(new byte[] { 0x61, 0xEF, 0xBF, 0xBD, 0x62 }));

		Span<byte> scratch2 = stackalloc byte[Utf8Scratch.StackBufferSize];
		using Utf8Scratch low = Utf8Scratch.Encode("\uDC00", scratch2);
		Assert.True(low.Bytes.SequenceEqual(new byte[] { 0xEF, 0xBF, 0xBD }));
	}

	[Fact]
	public void Empty_text_gives_empty_bytes_without_pooling()
	{
		Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
		using Utf8Scratch utf8 = Utf8Scratch.Encode(ReadOnlySpan<char>.Empty, scratch);

		Assert.True(utf8.Bytes.IsEmpty);
		Assert.False(utf8.IsPooled);
	}

	[Fact]
	public void Text_whose_worst_case_exceeds_the_buffer_but_whose_real_size_fits_stays_on_the_stack()
	{
		// 200 ASCII characters: worst case 603 bytes > 512, exact 200 bytes <= 512.
		string text = new('x', 200);
		Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
		using Utf8Scratch utf8 = Utf8Scratch.Encode(text, scratch);

		Assert.False(utf8.IsPooled);
		Assert.Equal(200, utf8.Bytes.Length);
	}

	[Fact]
	public void Text_above_the_stack_threshold_is_transcoded_through_the_pool_and_matches_encoding_utf8()
	{
		string text = string.Concat(Enumerable.Repeat("\u00E9\u20AC\uD83D\uDE00x", 300));
		Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
		using Utf8Scratch utf8 = Utf8Scratch.Encode(text, scratch);

		Assert.True(utf8.IsPooled);
		Assert.True(utf8.Bytes.SequenceEqual(Encoding.UTF8.GetBytes(text)));
	}

	[Fact]
	public void Dispose_is_idempotent_and_clears_the_bytes()
	{
		string text = new('y', 5_000);
		Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
		Utf8Scratch utf8 = Utf8Scratch.Encode(text, scratch);
		Assert.True(utf8.IsPooled);

		utf8.Dispose();
		utf8.Dispose();

		Assert.False(utf8.IsPooled);
		Assert.True(utf8.Bytes.IsEmpty);
	}
}
