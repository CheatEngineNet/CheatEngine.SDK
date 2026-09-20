using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     Reads the small, security-relevant PE surface of a native asset without loading it. The reader deliberately
///     validates every RVA before dereferencing it, so a damaged package produces a test failure rather than causing
///     the test host to execute its <c>DllMain</c>.
/// </summary>
internal sealed class PortableExecutableInspector
{
    private readonly PEHeaders _headers;
    private readonly byte[] _image;

    private PortableExecutableInspector(byte[] image, PEHeaders headers)
    {
        _image = image;
        _headers = headers;
    }

    /// <summary>The machine architecture declared by the COFF header.</summary>
    public Machine Machine => _headers.CoffHeader.Machine;

    /// <summary>Whether the PE characteristics identify this image as a DLL.</summary>
    public bool IsDll => _headers.IsDll;

    /// <summary>The optional-header format.</summary>
    public PEMagic Magic => GetRequiredPeHeader().Magic;

    /// <summary>Whether the image has a delay-load import directory.</summary>
    public bool HasDelayImports
    {
        get
        {
            var delayImports = GetRequiredPeHeader().DelayImportTableDirectory;
            return delayImports.RelativeVirtualAddress != 0 || delayImports.Size != 0;
        }
    }

    /// <summary>Reads a PE image without loading it into the current process.</summary>
    public static PortableExecutableInspector Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var image = File.ReadAllBytes(path);
        using var stream = new MemoryStream(image, writable: false);
        using var reader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);

        if (reader.PEHeaders.IsCoffOnly)
            throw new InvalidDataException($"'{path}' is a COFF object, not a PE image.");

        return new PortableExecutableInspector(image, reader.PEHeaders);
    }

    /// <summary>Returns all named exports and their implementation RVAs.</summary>
    public IReadOnlyList<PortableExecutableExport> GetExports()
    {
        var directory = GetRequiredPeHeader().ExportTableDirectory;
        if (directory.RelativeVirtualAddress == 0 || directory.Size == 0)
            throw new InvalidDataException("The PE image has no export directory.");

        var directoryOffset = MapRva(GetDirectoryRva(directory, "export"), 40u);
        var numberOfFunctions = ReadUInt32(directoryOffset + 20);
        var numberOfNames = ReadUInt32(directoryOffset + 24);
        var functionsRva = ReadUInt32(directoryOffset + 28);
        var namesRva = ReadUInt32(directoryOffset + 32);
        var ordinalsRva = ReadUInt32(directoryOffset + 36);

        var functionsOffset = MapRva(functionsRva, CheckedByteCount(numberOfFunctions, sizeof(uint)));
        var namesOffset = MapRva(namesRva, CheckedByteCount(numberOfNames, sizeof(uint)));
        var ordinalsOffset = MapRva(ordinalsRva, CheckedByteCount(numberOfNames, sizeof(ushort)));
        var exports = new List<PortableExecutableExport>(checked((int)numberOfNames));

        for (var index = 0u; index < numberOfNames; index++)
        {
            var nameRva = ReadUInt32(namesOffset + checked((int)(index * sizeof(uint))));
            var ordinalIndex = ReadUInt16(ordinalsOffset + checked((int)(index * sizeof(ushort))));
            if (ordinalIndex >= numberOfFunctions)
                throw new InvalidDataException("An export ordinal points outside the export address table.");

            var implementationRva = ReadUInt32(functionsOffset + checked(ordinalIndex * sizeof(uint)));
            exports.Add(new PortableExecutableExport(ReadAsciiZ(nameRva), implementationRva));
        }

        return exports;
    }

    /// <summary>Returns all normal import modules and their named or ordinal imports.</summary>
    public IReadOnlyList<PortableExecutableImport> GetImports()
    {
        var header = GetRequiredPeHeader();
        var directory = header.ImportTableDirectory;
        if (directory.RelativeVirtualAddress == 0 && directory.Size == 0)
            return [];
        if (directory.RelativeVirtualAddress == 0 || directory.Size == 0)
            throw new InvalidDataException("The PE import directory has an incomplete RVA/size pair.");

        var directorySize = GetDirectorySize(directory, "import");
        var directoryOffset = MapRva(GetDirectoryRva(directory, "import"), directorySize);
        var directoryEnd = checked(directoryOffset + (int)directorySize);
        var imports = new List<PortableExecutableImport>();

        for (var descriptorOffset = directoryOffset; descriptorOffset <= directoryEnd - 20; descriptorOffset += 20)
        {
            var originalFirstThunk = ReadUInt32(descriptorOffset);
            var timeDateStamp = ReadUInt32(descriptorOffset + 4);
            var forwarderChain = ReadUInt32(descriptorOffset + 8);
            var nameRva = ReadUInt32(descriptorOffset + 12);
            var firstThunk = ReadUInt32(descriptorOffset + 16);
            if (originalFirstThunk == 0 && timeDateStamp == 0 && forwarderChain == 0 && nameRva == 0 && firstThunk == 0)
                return imports;

            if (nameRva == 0)
                throw new InvalidDataException("An import descriptor has no module name.");

            var lookupTableRva = originalFirstThunk != 0 ? originalFirstThunk : firstThunk;
            if (lookupTableRva == 0)
                throw new InvalidDataException("An import descriptor has no lookup table.");

            imports.Add(new PortableExecutableImport(ReadAsciiZ(nameRva), ReadImportSymbols(lookupTableRva)));
        }

        throw new InvalidDataException("The PE import directory is missing its null descriptor.");
    }

    /// <summary>Reads a NUL-terminated ANSI payload from a named export.</summary>
    public string ReadExportedAsciiZ(string exportName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportName);

        var exports = GetExports();
        for (var index = 0; index < exports.Count; index++)
            if (string.Equals(exports[index].Name, exportName, StringComparison.Ordinal))
                return ReadAsciiZ(exports[index].RelativeVirtualAddress);

        throw new MissingMethodException($"The PE image does not export '{exportName}'.");
    }

    private List<string> ReadImportSymbols(uint lookupTableRva)
    {
        var symbols = new List<string>();
        var thunkRva = lookupTableRva;
        while (true)
        {
            var thunkOffset = MapRva(thunkRva, sizeof(ulong));
            var thunk = ReadUInt64(thunkOffset);
            if (thunk == 0)
                return symbols;

            const ulong ordinalMask = 0x8000000000000000UL;
            if ((thunk & ordinalMask) != 0)
            {
                symbols.Add($"#{thunk & 0xFFFFUL}");
            }
            else
            {
                if (thunk > uint.MaxValue)
                    throw new InvalidDataException("A PE32+ import name RVA exceeds 32 bits.");

                var hintNameRva = (uint)thunk;
                _ = MapRva(hintNameRva, sizeof(ushort));
                symbols.Add(ReadAsciiZ(checked(hintNameRva + sizeof(ushort))));
            }

            thunkRva = checked(thunkRva + sizeof(ulong));
        }
    }

    private string ReadAsciiZ(uint rva)
    {
        var offset = MapRva(rva, 1u, out var bytesAvailable);
        var end = offset;
        var maximum = checked(offset + bytesAvailable);
        while (end < maximum && _image[end] != 0)
            end++;

        if (end == maximum)
            throw new InvalidDataException("A PE ASCII string is not NUL-terminated inside its mapped section.");

        return Encoding.ASCII.GetString(_image, offset, end - offset);
    }

    private int MapRva(uint rva, uint byteCount)
    {
        return MapRva(rva, byteCount, out _);
    }

    private int MapRva(uint rva, uint byteCount, out int bytesAvailable)
    {
        var header = GetRequiredPeHeader();
        if (header.SizeOfHeaders < 0)
            throw new InvalidDataException("The PE image has a negative SizeOfHeaders value.");

        var headerSize = (uint)header.SizeOfHeaders;
        if (rva < headerSize)
        {
            if ((ulong)rva + byteCount > headerSize || (ulong)rva + byteCount > (uint)_image.Length)
                throw new InvalidDataException("An RVA range extends beyond the PE headers.");

            bytesAvailable = checked((int)(headerSize - rva));
            return checked((int)rva);
        }

        foreach (var section in _headers.SectionHeaders)
        {
            if (section.VirtualAddress < 0 || section.SizeOfRawData < 0 || section.PointerToRawData < 0)
                throw new InvalidDataException("The PE image has a negative section field.");

            var sectionRva = (uint)section.VirtualAddress;
            var rawSize = (uint)section.SizeOfRawData;
            var virtualSize = section.VirtualSize < 0 ? 0u : (uint)section.VirtualSize;
            var mappedSize = Math.Max(rawSize, virtualSize);
            if ((ulong)rva < sectionRva || rva >= (ulong)sectionRva + mappedSize)
                continue;

            var delta = checked(rva - sectionRva);
            if ((ulong)delta + byteCount > rawSize)
                throw new InvalidDataException("An RVA range extends past raw bytes in its PE section.");

            var fileOffset = checked((uint)section.PointerToRawData + delta);
            if ((ulong)fileOffset + byteCount > (uint)_image.Length)
                throw new InvalidDataException("An RVA maps past the end of the PE image.");

            bytesAvailable = checked((int)(rawSize - delta));
            return checked((int)fileOffset);
        }

        throw new InvalidDataException("An RVA does not map to a PE section.");
    }

    private PEHeader GetRequiredPeHeader()
    {
        return _headers.PEHeader ?? throw new InvalidDataException("The image has no PE optional header.");
    }

    private uint ReadUInt32(int offset)
    {
        EnsureImageRange(offset, sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(_image.AsSpan(offset, sizeof(uint)));
    }

    private ushort ReadUInt16(int offset)
    {
        EnsureImageRange(offset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(_image.AsSpan(offset, sizeof(ushort)));
    }

    private ulong ReadUInt64(int offset)
    {
        EnsureImageRange(offset, sizeof(ulong));
        return BinaryPrimitives.ReadUInt64LittleEndian(_image.AsSpan(offset, sizeof(ulong)));
    }

    private void EnsureImageRange(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > _image.Length - length)
            throw new InvalidDataException("A PE field lies outside the image.");
    }

    private static uint CheckedByteCount(uint count, int elementSize)
    {
        return checked(count * (uint)elementSize);
    }

    private static uint GetDirectoryRva(DirectoryEntry directory, string name)
    {
        if (directory.RelativeVirtualAddress <= 0)
            throw new InvalidDataException($"The PE {name} directory has no positive RVA.");

        return (uint)directory.RelativeVirtualAddress;
    }

    private static uint GetDirectorySize(DirectoryEntry directory, string name)
    {
        if (directory.Size <= 0)
            throw new InvalidDataException($"The PE {name} directory has no positive size.");

        return (uint)directory.Size;
    }
}
