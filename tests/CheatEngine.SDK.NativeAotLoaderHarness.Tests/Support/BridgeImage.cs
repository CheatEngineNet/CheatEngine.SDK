using System.Buffers.Binary;
using System.Text;

namespace CheatEngine.SDK.NativeAotLoaderHarness.Tests.Support;

/// <summary>
///     Copies of the checked-in Lua protection bridge DLL (a real PE32+ AMD64 image with four named exports), patched in
///     memory to produce the shapes the harness must refuse. A patched image is only ever parsed as bytes: nothing here
///     writes it to disk, maps it or loads it.
/// </summary>
internal static class BridgeImage
{
	private const string ResourceName = "CheatEngine.SDK.NativeAotLoaderHarness.Tests.Bridge.dll";
	private const int PeHeaderPointerOffset = 0x3C;
	private const int OptionalHeaderOffsetFromSignature = 24;
	private const int ExportDirectoryOffsetInOptionalHeader = 112;
	private const ushort Pe32PlusMagic = 0x20B;

	/// <summary>The bridge's named exports, in the order the export directory lists them.</summary>
	public static IReadOnlyList<string> ExportNames
	{
		get;
	} =
	[
		"cheatengine_sdk_lua_bridge_abi_version",
		"cheatengine_sdk_lua_bridge_get_contract",
		"cheatengine_sdk_lua_bridge_source_fingerprint",
		"cheatengine_sdk_lua_protected"
	];

	/// <summary>A fresh copy of the embedded image.</summary>
	public static byte[] Load()
	{
		using Stream stream = typeof(BridgeImage).Assembly.GetManifestResourceStream(ResourceName)
		                      ?? throw new InvalidOperationException(
			                      $"The embedded resource {ResourceName} is missing.");
		using MemoryStream copy = new();
		stream.CopyTo(copy);
		return copy.ToArray();
	}

	/// <summary>Rewrites the COFF <c>Machine</c> field.</summary>
	public static void SetMachine(byte[] image, ushort machine)
	{
		BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(SignatureOffset(image) + 4), machine);
	}

	/// <summary>Zeroes the RVA and size of the export data directory (entry 0 of the PE32+ optional header).</summary>
	public static void ClearExportDirectory(byte[] image)
	{
		int optionalHeader = SignatureOffset(image) + OptionalHeaderOffsetFromSignature;
		if (BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(optionalHeader)) != Pe32PlusMagic)
		{
			throw new InvalidOperationException("The bridge image is not PE32+.");
		}

		image.AsSpan(optionalHeader + ExportDirectoryOffsetInOptionalHeader, 8).Clear();
	}

	/// <summary>
	///     Overwrites the NUL-terminated export name <paramref name="existing" /> in place with
	///     <paramref name="replacement" /> (never longer), padding with NUL bytes.
	/// </summary>
	public static void RenameExport(byte[] image, string existing, string replacement)
	{
		if (replacement.Length > existing.Length)
		{
			throw new ArgumentException("A replacement name may not be longer than the original.", nameof(replacement));
		}

		byte[] needle = [.. Encoding.ASCII.GetBytes(existing), 0];
		int offset = image.AsSpan().IndexOf(needle);
		if (offset < 0)
		{
			throw new InvalidOperationException($"The export name '{existing}' is not in the image.");
		}

		Span<byte> target = image.AsSpan(offset, existing.Length);
		target.Clear();
		Encoding.ASCII.GetBytes(replacement).CopyTo(target);
	}

	private static int SignatureOffset(byte[] image)
	{
		return BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(PeHeaderPointerOffset));
	}
}
