using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

using CheatEngine.SDK.NativeAotLoaderHarness.Tests.Support;

using FsCheck;
using FsCheck.Fluent;

namespace CheatEngine.SDK.NativeAotLoaderHarness.Tests;

/// <summary>Bounded byte fuzzing of PE inspection; no input is mapped or executed.</summary>
public sealed class PortableExecutableFuzzTests
{
	private static readonly Config s_configuration = Config.QuickThrowOnFailure.WithMaxTest(1000).WithEndSize(512);

	[Fact]
	public void Arbitrary_image_bytes_have_only_documented_malformed_image_failures()
	{
		Prop.ForAll<byte[]>(bytes =>
		{
			AssertReadOrMalformed(bytes);
			return true;
		}).Check(s_configuration);
	}

	[Fact]
	public void Generated_truncations_never_escape_as_index_or_arithmetic_errors()
	{
		byte[] complete = BridgeImage.Load();
		Prop.ForAll<ushort>(length =>
		{
			byte[] image = complete[..(length % (complete.Length + 1))];
			AssertReadOrMalformed(image);
			return true;
		}).Check(s_configuration);
	}

	[Fact]
	public void Generated_ascii_export_names_are_read_exactly_without_loading_the_image()
	{
		Prop.ForAll<byte[]>(bytes =>
		{
			string replacement = "Fuzz" + new string(Array.ConvertAll(bytes[..Math.Min(bytes.Length, 24)],
				static value => (char) ('A' + (value % 26))));
			byte[] image = BridgeImage.Load();
			BridgeImage.RenameExport(image, BridgeImage.ExportNames[0], replacement);
			string[] expected = [.. BridgeImage.ExportNames];
			expected[0] = replacement;
			Assert.Equal(expected, PortableExecutableExportReader.ReadExportNames(image), StringComparer.Ordinal);
			return true;
		}).Check(s_configuration);
	}

	[Fact]
	public void Generated_high_export_rvas_are_refused_as_malformed_data()
	{
		Prop.ForAll<uint>(value =>
		{
			foreach (bool nameEntry in (bool[]) [false, true])
			{
				byte[] image = WithRva(value | 0x80000000U, nameEntry);
				Assert.Throws<InvalidOperationException>(() => PortableExecutableExportReader.ReadExportNames(image));
			}

			return true;
		}).Check(s_configuration);
	}

	[Theory]
	[InlineData(false, 0x7FFFFFFFU)]
	[InlineData(false, 0x80000000U)]
	[InlineData(false, 0xFFFFFFFFU)]
	[InlineData(true, 0x7FFFFFFFU)]
	[InlineData(true, 0x80000000U)]
	[InlineData(true, 0xFFFFFFFFU)]
	public void Rvas_outside_the_file_address_range_report_a_data_error(bool nameEntry, uint rva)
	{
		byte[] image = WithRva(rva, nameEntry);
		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => PortableExecutableExportReader.ReadExportNames(image));
		Assert.NotEmpty(exception.Message);
	}

	private static void AssertReadOrMalformed(byte[] image)
	{
		byte[] original = (byte[]) image.Clone();
		List<string>? names = null;
		Exception? exception = Record.Exception(() => names = PortableExecutableExportReader.ReadExportNames(image));
		if (exception is null)
		{
			Assert.NotNull(names);
			Assert.InRange(names.Count, 1, image.Length / sizeof(uint));
			Assert.All(names, static name => Assert.All(name, static character => Assert.InRange(character, '\0', '\x7F')));
		}
		else
		{
			Assert.True(exception is InvalidOperationException or BadImageFormatException,
				$"Malformed PE bytes produced unexpected {exception.GetType().Name}: {exception.Message}");
		}

		Assert.Equal(original, image);
	}

	private static byte[] WithRva(uint rva, bool nameEntry)
	{
		byte[] image = BridgeImage.Load();
		using PEReader reader = new(new MemoryStream(image, false));
		PEHeaders headers = reader.PEHeaders;
		int directoryRva = headers.PEHeader!.ExportTableDirectory.RelativeVirtualAddress;
		int directoryOffset = FixtureOffset(headers, directoryRva);
		int offset = directoryOffset + 32;
		if (nameEntry)
		{
			int namesRva = checked((int) BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset)));
			offset = FixtureOffset(headers, namesRva);
		}

		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset), rva);
		return image;
	}

	private static int FixtureOffset(PEHeaders headers, int rva)
	{
		int matches = 0;
		int offset = 0;
		foreach (SectionHeader section in headers.SectionHeaders)
		{
			if (rva >= section.VirtualAddress && (long) rva < (long) section.VirtualAddress + section.SizeOfRawData)
			{
				matches++;
				offset = section.PointerToRawData + rva - section.VirtualAddress;
			}
		}

		Assert.Equal(1, matches);
		return offset;
	}
}
