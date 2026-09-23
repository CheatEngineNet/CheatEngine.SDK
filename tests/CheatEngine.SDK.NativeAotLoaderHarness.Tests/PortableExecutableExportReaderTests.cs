using System.Text;

using CheatEngine.SDK.NativeAotLoaderHarness.Tests.Support;

namespace CheatEngine.SDK.NativeAotLoaderHarness.Tests;

/// <summary>
///     The harness's PE export reader against a real PE32+ AMD64 DLL (the checked-in Lua protection bridge) and against
///     in-memory copies patched into the shapes it must refuse. It reads bytes only; no image is mapped or loaded.
/// </summary>
public sealed class PortableExecutableExportReaderTests
{
	[Fact]
	[Trait("Qualification", "Q41")]
	public void Reads_the_four_exports_of_the_checked_in_bridge()
	{
		List<string> names = PortableExecutableExportReader.ReadExportNames(BridgeImage.Load());

		Assert.Equal(BridgeImage.ExportNames, names, StringComparer.Ordinal);
	}

	[Theory]
	[InlineData((ushort) 0x014C)]
	[InlineData((ushort) 0xAA64)]
	[InlineData((ushort) 0x0000)]
	[Trait("Qualification", "Q41")]
	public void Refuses_a_non_amd64_image(ushort machine)
	{
		byte[] image = BridgeImage.Load();
		BridgeImage.SetMachine(image, machine);

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => PortableExecutableExportReader.ReadExportNames(image));

		Assert.Equal("The file is not an AMD64 image.", exception.Message);
	}

	[Fact]
	[Trait("Qualification", "Q41")]
	public void Refuses_an_image_without_an_export_directory()
	{
		byte[] image = BridgeImage.Load();
		BridgeImage.ClearExportDirectory(image);

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => PortableExecutableExportReader.ReadExportNames(image));

		Assert.Equal("The file has no export directory.", exception.Message);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(2)]
	[InlineData(64)]
	[InlineData(512)]
	[InlineData(1024)]
	[Trait("Qualification", "Q41")]
	public void Refuses_truncated_bytes(int length)
	{
		byte[] image = BridgeImage.Load()[..length];

		Exception exception = Assert.ThrowsAny<Exception>(() => PortableExecutableExportReader.ReadExportNames(image));

		Assert.True(exception is InvalidOperationException or BadImageFormatException,
			$"A truncated image must be refused as malformed, not with {exception.GetType().Name}: {exception.Message}");
	}

	[Fact]
	[Trait("Qualification", "Q41")]
	public void Refuses_an_image_truncated_inside_an_export_name()
	{
		byte[] full = BridgeImage.Load();
		int firstName = full.AsSpan().IndexOf(Encoding.ASCII.GetBytes(BridgeImage.ExportNames[0] + "\0"));
		Assert.True(firstName > 0);
		byte[] image = full[..(firstName + 3)];

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => PortableExecutableExportReader.ReadExportNames(image));

		Assert.Equal("An export name is not zero terminated.", exception.Message);
	}
}
