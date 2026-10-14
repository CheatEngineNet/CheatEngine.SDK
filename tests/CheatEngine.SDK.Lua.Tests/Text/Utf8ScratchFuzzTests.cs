using System.Text;

using CheatEngine.SDK.Lua.Text;

using FsCheck;
using FsCheck.Fluent;

namespace CheatEngine.SDK.Lua.Tests.Text;

/// <summary>Generated UTF-16 checks for the stack-first UTF-8 scratch buffer.</summary>
public sealed class Utf8ScratchFuzzTests
{
	private static readonly Config s_configuration = Config.QuickThrowOnFailure.WithMaxTest(1000).WithEndSize(128);

	[Fact]
	public void Generated_utf16_matches_encoding_utf8_across_stack_and_pool_capacity()
	{
		Prop.ForAll<ushort[]>(values =>
		{
			string generated = new(Array.ConvertAll(values[..Math.Min(values.Length, 128)], static value => (char) value));
			string text = (values.Length == 0 || (values[0] & 1) == 0) ? generated : generated + new string('x', 513);
			byte[] expected = Encoding.UTF8.GetBytes(text);
			Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
			using Utf8Scratch utf8 = Utf8Scratch.Encode(text, scratch);

			Assert.Equal(expected.Length > Utf8Scratch.StackBufferSize, utf8.IsPooled);
			Assert.True(utf8.Bytes.SequenceEqual(expected));
			return true;
		}).Check(s_configuration);
	}

	[Theory]
	[InlineData(511, false)]
	[InlineData(512, false)]
	[InlineData(513, true)]
	public void Ascii_at_the_stack_buffer_cutover_uses_the_documented_storage(int byteCount, bool expectedPooled)
	{
		string text = new('x', byteCount);
		AssertEncodingAndStorage(text, expectedPooled);
	}

	[Theory]
	[InlineData(510, false)]
	[InlineData(512, false)]
	[InlineData(514, true)]
	public void Multibyte_utf8_at_the_stack_buffer_cutover_uses_the_documented_storage(int byteCount,
		bool expectedPooled)
	{
		Assert.Equal(0, byteCount % 2);
		string text = new('\u00E9', byteCount / 2);
		AssertEncodingAndStorage(text, expectedPooled);
	}

	private static void AssertEncodingAndStorage(string text, bool expectedPooled)
	{
		byte[] expected = Encoding.UTF8.GetBytes(text);
		Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
		using Utf8Scratch utf8 = Utf8Scratch.Encode(text, scratch);

		Assert.Equal(expectedPooled, utf8.IsPooled);
		Assert.Equal(expected.Length, utf8.Bytes.Length);
		Assert.True(utf8.Bytes.SequenceEqual(expected));
	}
}
