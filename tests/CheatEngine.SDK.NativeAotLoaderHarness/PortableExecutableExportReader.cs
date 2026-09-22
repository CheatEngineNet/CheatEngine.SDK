using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace CheatEngine.SDK.NativeAotLoaderHarness;

/// <summary>Reads a PE export directory from file bytes without asking the operating-system loader to map the file.</summary>
internal static class PortableExecutableExportReader
{
	private const int ExportDirectorySize = 40;
	private const int NumberOfNamesOffset = 24;
	private const int AddressOfNamesOffset = 32;

	/// <summary>Returns the PE32+ AMD64 export names in the supplied file.</summary>
	public static List<string> ReadExportNames(byte[] image)
	{
		using MemoryStream imageStream = new(image, false);
		using PEReader peReader = new(imageStream);

		PEHeaders headers = peReader.PEHeaders;
		PEHeader? peHeader = headers.PEHeader;
		if (peHeader is null)
		{
			throw new InvalidOperationException("The file has no portable executable header.");
		}

		if (peHeader.Magic != PEMagic.PE32Plus)
		{
			throw new InvalidOperationException("The file is not a PE32+ image.");
		}

		if (headers.CoffHeader.Machine != Machine.Amd64)
		{
			throw new InvalidOperationException("The file is not an AMD64 image.");
		}

		int exportDirectoryRva = peHeader.ExportTableDirectory.RelativeVirtualAddress;
		if (exportDirectoryRva == 0)
		{
			throw new InvalidOperationException("The file has no export directory.");
		}

		int exportDirectoryOffset = ResolveFileOffset(headers, exportDirectoryRva);
		RequireBytes(image, exportDirectoryOffset, ExportDirectorySize);

		uint numberOfNames = ReadUInt32(image, exportDirectoryOffset + NumberOfNamesOffset);
		uint namesRva = ReadUInt32(image, exportDirectoryOffset + AddressOfNamesOffset);
		if (numberOfNames == 0 || namesRva == 0)
		{
			throw new InvalidOperationException("The export directory has no named exports.");
		}

		List<string> names = new();
		int namesOffset = ResolveFileOffset(headers, checked((int) namesRva));

		for (uint index = 0; index < numberOfNames; index++)
		{
			int nameRvaOffset = checked(namesOffset + checked((int) (index * sizeof(uint))));
			uint nameRva = ReadUInt32(image, nameRvaOffset);
			names.Add(ReadAsciiZeroTerminated(image, ResolveFileOffset(headers, checked((int) nameRva))));
		}

		return names;
	}

	private static int ResolveFileOffset(PEHeaders headers, int relativeVirtualAddress)
	{
		foreach (SectionHeader section in headers.SectionHeaders)
		{
			int sectionLength = Math.Max(section.VirtualSize, section.SizeOfRawData);
			long sectionStart = section.VirtualAddress;
			long sectionEnd = sectionStart + sectionLength;

			if (relativeVirtualAddress < sectionStart || relativeVirtualAddress >= sectionEnd)
			{
				continue;
			}

			long fileOffset = section.PointerToRawData + (relativeVirtualAddress - sectionStart);
			if (fileOffset < 0 || fileOffset > int.MaxValue)
			{
				throw new InvalidOperationException("The export directory resolves outside the file.");
			}

			return (int) fileOffset;
		}

		throw new InvalidOperationException("The export directory does not map to a PE section.");
	}

	private static uint ReadUInt32(byte[] image, int offset)
	{
		RequireBytes(image, offset, sizeof(uint));
		return BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset, sizeof(uint)));
	}

	private static string ReadAsciiZeroTerminated(byte[] image, int offset)
	{
		if (offset < 0 || offset >= image.Length)
		{
			throw new InvalidOperationException("An export name resolves outside the file.");
		}

		int end = offset;
		while (end < image.Length && image[end] != 0)
		{
			end++;
		}

		if (end == image.Length)
		{
			throw new InvalidOperationException("An export name is not zero terminated.");
		}

		return Encoding.ASCII.GetString(image, offset, end - offset);
	}

	private static void RequireBytes(byte[] image, int offset, int count)
	{
		if (offset < 0 || count < 0 || image.Length - offset < count)
		{
			throw new InvalidOperationException("The PE export data is truncated.");
		}
	}
}
