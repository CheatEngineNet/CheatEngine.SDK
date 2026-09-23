using System.Diagnostics;

namespace QualificationTarget;

/// <summary>
///     Counts the occurrences of a byte pattern in memory the process owns: its main image (section by section, so only
///     mapped, readable pages are read) or a pinned heap region. The counts are the ground truth the qualification driver
///     compares a Cheat Engine scan with.
/// </summary>
internal static unsafe class ImageScanner
{
	private const uint ImageScnMemRead = 0x4000_0000;

	/// <summary>The base address and mapped size of the executable image.</summary>
	internal static (nint Base, int Size) MainImage()
	{
		using Process process = Process.GetCurrentProcess();
		ProcessModule module = process.MainModule
							   ?? throw new InvalidOperationException("The main module is not available.");
		return (module.BaseAddress, module.ModuleMemorySize);
	}

	/// <summary>Counts <paramref name="pattern" /> in every readable section of the image at <paramref name="imageBase" />.</summary>
	internal static int CountInImage(nint imageBase, ReadOnlySpan<byte> pattern)
	{
		byte* image = (byte*) imageBase;
		int peHeader = *(int*) (image + 0x3C);
		byte* ntHeaders = image + peHeader;
		if (*(uint*) ntHeaders != 0x0000_4550)
		{
			throw new InvalidOperationException("The main module does not start with a PE header.");
		}

		ushort sectionCount = *(ushort*) (ntHeaders + 6);
		ushort optionalHeaderSize = *(ushort*) (ntHeaders + 20);
		byte* sections = ntHeaders + 24 + optionalHeaderSize;
		int count = 0;
		for (int index = 0; index < sectionCount; index++)
		{
			byte* section = sections + (index * 40);
			uint virtualSize = *(uint*) (section + 8);
			uint virtualAddress = *(uint*) (section + 12);
			uint characteristics = *(uint*) (section + 36);
			if ((characteristics & ImageScnMemRead) != 0 && virtualSize > 0)
			{
				count += Count(new ReadOnlySpan<byte>(image + virtualAddress, checked((int) virtualSize)), pattern);
			}
		}

		return count;
	}

	/// <summary>Counts every start position of <paramref name="pattern" /> in <paramref name="memory" />.</summary>
	internal static int Count(ReadOnlySpan<byte> memory, ReadOnlySpan<byte> pattern)
	{
		int count = 0;
		int offset = 0;
		while (offset <= memory.Length - pattern.Length)
		{
			int found = memory[offset..].IndexOf(pattern);
			if (found < 0)
			{
				break;
			}

			count++;
			offset += found + 1;
		}

		return count;
	}
}
