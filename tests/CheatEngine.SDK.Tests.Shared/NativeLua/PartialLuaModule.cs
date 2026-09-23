using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace CheatEngine.SDK.Tests.Shared.NativeLua;

/// <summary>
///     A copy of the Lua fixture in which exactly one export is missing, loaded from a private temporary folder: the
///     "module that lacks exports the SDK binds" of qualification scenario Q11. The name <see cref="RemovedExport" /> is
///     overwritten in the PE export name table by <see cref="ReplacementName" />, which has the same length and sorts to
///     the same position, so the table stays sorted and <c>GetProcAddress</c>'s binary search still finds every other
///     export.
/// </summary>
/// <remarks>
///     Only the bytes of the copy are patched; the committed fixture is never modified. The copy is loaded in the test
///     process only, under another file name, and freed and deleted by <see cref="Dispose" />. Production code never
///     loads a second Lua module. This file stays free of xUnit types, like the rest of the fixture.
/// </remarks>
internal sealed class PartialLuaModule : IDisposable
{
	/// <summary>The export the copy lacks.</summary>
	public const string RemovedExport = "lua_rotate";

	/// <summary>The name that replaces it: same length, same sorted position between its neighbours.</summary>
	public const string ReplacementName = "lua_rotatf";

	private const int PeHeaderPointerOffset = 0x3C;
	private const int OptionalHeaderOffsetFromSignature = 24;
	private const int ExportDirectoryOffsetInOptionalHeader = 112;
	private const int SectionHeaderSize = 40;
	private const ushort Pe32PlusMagic = 0x20B;

	private readonly string _directory;

	private PartialLuaModule(string directory, nint handle)
	{
		_directory = directory;
		Handle = handle;
	}

	/// <summary>The handle of the loaded copy; zero after <see cref="Dispose" />.</summary>
	public nint Handle
	{
		get;
		private set;
	}

	/// <summary>Frees the copy and deletes its folder.</summary>
	public void Dispose()
	{
		if (Handle != 0)
		{
			NativeLibrary.Free(Handle);
			Handle = 0;
		}

		try
		{
			Directory.Delete(_directory, true);
		}
		catch (IOException)
		{
			// Another handle still maps the file; the folder name is unique, so a leftover never collides.
		}
		catch (UnauthorizedAccessException)
		{
			// Same as above.
		}
	}

	/// <summary>Copies <paramref name="fixturePath" />, removes <see cref="RemovedExport" /> from the copy and loads it.</summary>
	/// <exception cref="InvalidOperationException">The image is not PE32+ or does not export <see cref="RemovedExport" />.</exception>
	public static PartialLuaModule Load(string fixturePath)
	{
		byte[] image = File.ReadAllBytes(fixturePath);
		RenameExport(image, RemovedExport, ReplacementName);
		string directory = Path.Combine(Path.GetTempPath(), "CheatEngine.SDK.Tests",
			"partial-lua-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, "lua53-partial.dll");
		File.WriteAllBytes(path, image);
		return new PartialLuaModule(directory, NativeLibrary.Load(path));
	}

	/// <summary>
	///     Overwrites the export name <paramref name="existing" /> of a PE32+ <paramref name="image" /> in place with
	///     <paramref name="replacement" />, found through the export directory rather than by searching the bytes.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	///     The image is not PE32+, has no such export, or the replacement would leave the name table unsorted.
	/// </exception>
	internal static void RenameExport(byte[] image, string existing, string replacement)
	{
		if (replacement.Length != existing.Length)
		{
			throw new ArgumentException("The replacement must have the length of the original name.",
				nameof(replacement));
		}

		int signature = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(PeHeaderPointerOffset));
		int optionalHeader = signature + OptionalHeaderOffsetFromSignature;
		if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(signature)) != 0x4550 ||
			BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(optionalHeader)) != Pe32PlusMagic)
		{
			throw new InvalidOperationException("The Lua fixture is not a PE32+ image.");
		}

		int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(signature + 6));
		int sections = optionalHeader + BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(signature + 20));
		uint exportRva =
			BinaryPrimitives.ReadUInt32LittleEndian(
				image.AsSpan(optionalHeader + ExportDirectoryOffsetInOptionalHeader));
		int exportDirectory = ToFileOffset(image, sections, sectionCount, exportRva);
		int nameCount = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(exportDirectory + 24));
		int nameTable = ToFileOffset(image, sections, sectionCount,
			BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(exportDirectory + 32)));

		List<(string Name, int Offset)> names = [];
		for (int index = 0; index < nameCount; index++)
		{
			int offset = ToFileOffset(image, sections, sectionCount,
				BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(nameTable + (4 * index))));
			int length = image.AsSpan(offset).IndexOf((byte) 0);
			names.Add((Encoding.ASCII.GetString(image, offset, length), offset));
		}

		int target = names.FindIndex(entry => string.Equals(entry.Name, existing, StringComparison.Ordinal));
		if (target < 0)
		{
			throw new InvalidOperationException($"The Lua fixture does not export '{existing}'.");
		}

		bool staysSorted = (target == 0 || string.CompareOrdinal(names[target - 1].Name, replacement) < 0) &&
						   (target == names.Count - 1 ||
							string.CompareOrdinal(replacement, names[target + 1].Name) < 0);
		if (!staysSorted)
		{
			throw new InvalidOperationException($"'{replacement}' would leave the export name table unsorted.");
		}

		Encoding.ASCII.GetBytes(replacement).CopyTo(image, names[target].Offset);
	}

	private static int ToFileOffset(byte[] image, int sections, int sectionCount, uint rva)
	{
		for (int index = 0; index < sectionCount; index++)
		{
			ReadOnlySpan<byte> header = image.AsSpan(sections + (index * SectionHeaderSize), SectionHeaderSize);
			uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
			uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
			uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
			uint rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
			if (rva >= virtualAddress && rva < virtualAddress + Math.Max(virtualSize, rawSize))
			{
				return checked((int) (rva - virtualAddress + rawPointer));
			}
		}

		throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
			$"RVA 0x{rva:X} is in no section of the Lua fixture."));
	}
}
