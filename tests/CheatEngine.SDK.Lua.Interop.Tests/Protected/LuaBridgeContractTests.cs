using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CheatEngine.SDK.Lua.Interop.Tests.NativeProtection;

/// <summary>Structural tests for the native boundary that protects allocating Lua operations.</summary>
public sealed unsafe class LuaBridgeContractTests
{
    private const uint ContractMagic = 0x4345534B;
    private const ushort WindowsAmd64Machine = 0x8664;
    private static readonly uint s_exportTableSize = 20u * (uint)IntPtr.Size;
    private const ulong RequiredOperations = (1UL << 11) - 1;
    private static readonly string[] s_fixedExports =
    [
        "cheatengine_sdk_lua_bridge_abi_version",
        "cheatengine_sdk_lua_bridge_get_contract",
        "cheatengine_sdk_lua_bridge_source_fingerprint",
        "cheatengine_sdk_lua_protected"
    ];
    private static readonly string[] s_allowedImportedModules = ["KERNEL32.dll"];

    [Fact]
    public void Native_bridge_has_the_fixed_contract_and_no_Lua_import()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "cheatengine-sdk-lua-bridge.dll");
        Assert.True(File.Exists(path), $"The native Lua bridge was not copied to '{path}'.");
        Assert.Equal(WindowsAmd64Machine, ReadMachine(path));

        var module = NativeLibrary.Load(path);
        try
        {
            for (var i = 0; i < s_fixedExports.Length; i++)
                Assert.True(NativeLibrary.TryGetExport(module, s_fixedExports[i], out _), s_fixedExports[i]);

            var getContract = (delegate* unmanaged[Cdecl]<LuaBridgeContract*, nuint, int>)NativeLibrary.GetExport(
                module,
                "cheatengine_sdk_lua_bridge_get_contract");
            LuaBridgeContract contract = default;
            Assert.Equal(0, getContract(&contract, (nuint)Unsafe.SizeOf<LuaBridgeContract>() - 1));
            Assert.Equal(1, getContract(&contract, (nuint)Unsafe.SizeOf<LuaBridgeContract>()));

            Assert.Equal(ContractMagic, contract.Magic);
            Assert.Equal((uint)Unsafe.SizeOf<LuaBridgeContract>(), contract.ContractSize);
            Assert.Equal(1, contract.AbiMajor);
            Assert.True(contract.AbiMinor >= 1);
            Assert.Equal((byte)IntPtr.Size, contract.PointerSize);
            Assert.Equal((byte)sizeof(long), contract.LuaIntegerSize);
            Assert.Equal((byte)sizeof(nuint), contract.SizeTSize);
            Assert.Equal(s_exportTableSize, contract.ExportTableSize);
            Assert.Equal(RequiredOperations, contract.SupportedOperations & RequiredOperations);
            Assert.Equal(0, contract.Reserved);
        }
        finally
        {
            NativeLibrary.Free(module);
        }

        var imports = ReadImportedModules(path);
        Assert.Equal(s_allowedImportedModules, imports, StringComparer.OrdinalIgnoreCase);
        Assert.False(HasDelayImports(path), "The Lua bridge must not delay-load a host or Lua module.");

        var exports = ReadExportedNames(path);
        Assert.Equal(s_fixedExports, exports, StringComparer.Ordinal);
    }

    private static ushort ReadMachine(string path)
    {
        var image = File.ReadAllBytes(path);
        Require(image.Length >= 0x40 && image[0] == (byte)'M' && image[1] == (byte)'Z', "The native bridge has no DOS header.");

        var peOffset = checked((int)ReadUInt32(image, 0x3c));
        Require(ReadUInt32(image, peOffset) == 0x00004550, "The native bridge has no PE header.");
        return ReadUInt16(image, peOffset + 4);
    }

    private static bool HasDelayImports(string path)
    {
        var image = File.ReadAllBytes(path);
        Require(image.Length >= 0x40 && image[0] == (byte)'M' && image[1] == (byte)'Z', "The native bridge has no DOS header.");

        var peOffset = checked((int)ReadUInt32(image, 0x3c));
        Require(ReadUInt32(image, peOffset) == 0x00004550, "The native bridge has no PE header.");
        var optionalOffset = peOffset + 24;
        Require(ReadUInt16(image, optionalOffset) == 0x20b, "The native bridge is not PE32+ (Windows x64).");
        var directories = ReadUInt32(image, optionalOffset + 108);
        Require(directories > 13, "The native bridge has no delay-import directory slot.");

        var delayImportDirectoryOffset = optionalOffset + 112 + (13 * 8);
        return ReadUInt32(image, delayImportDirectoryOffset) != 0 ||
            ReadUInt32(image, delayImportDirectoryOffset + sizeof(uint)) != 0;
    }

    private static List<string> ReadExportedNames(string path)
    {
        var image = File.ReadAllBytes(path);
        Require(image.Length >= 0x40 && image[0] == (byte)'M' && image[1] == (byte)'Z', "The native bridge has no DOS header.");

        var peOffset = checked((int)ReadUInt32(image, 0x3c));
        Require(ReadUInt32(image, peOffset) == 0x00004550, "The native bridge has no PE header.");
        var sectionCount = ReadUInt16(image, peOffset + 6);
        var optionalSize = ReadUInt16(image, peOffset + 20);
        var optionalOffset = peOffset + 24;
        Require(ReadUInt16(image, optionalOffset) == 0x20b, "The native bridge is not PE32+ (Windows x64).");
        Require(ReadUInt32(image, optionalOffset + 108) > 0, "The native bridge has no export-directory slot.");

        var exportRva = ReadUInt32(image, optionalOffset + 112);
        var sectionOffset = optionalOffset + optionalSize;
        var sizeOfHeaders = ReadUInt32(image, optionalOffset + 60);
        List<string> result = [];
        if (exportRva == 0) return result;

        var exportOffset = RvaToFileOffset(image, exportRva, sizeOfHeaders, sectionOffset, sectionCount);
        Require(exportOffset <= image.Length - 40, "The native bridge has a truncated export directory.");
        var namesCount = ReadUInt32(image, exportOffset + 24);
        var namesRva = ReadUInt32(image, exportOffset + 32);
        var namesOffset = RvaToFileOffset(image, namesRva, sizeOfHeaders, sectionOffset, sectionCount);
        Require(namesCount <= (uint)((image.Length - namesOffset) / sizeof(uint)), "The native bridge has a truncated export-name table.");

        for (var i = 0u; i < namesCount; i++)
        {
            var nameRva = ReadUInt32(image, checked(namesOffset + ((int)i * sizeof(uint))));
            result.Add(ReadAsciiZ(image, RvaToFileOffset(image, nameRva, sizeOfHeaders, sectionOffset, sectionCount)));
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static List<string> ReadImportedModules(string path)
    {
        var image = File.ReadAllBytes(path);
        Require(image.Length >= 0x40 && image[0] == (byte)'M' && image[1] == (byte)'Z', "The native bridge has no DOS header.");

        var peOffset = checked((int)ReadUInt32(image, 0x3c));
        Require(ReadUInt32(image, peOffset) == 0x00004550, "The native bridge has no PE header.");
        var sectionCount = ReadUInt16(image, peOffset + 6);
        var optionalSize = ReadUInt16(image, peOffset + 20);
        var optionalOffset = peOffset + 24;
        Require(ReadUInt16(image, optionalOffset) == 0x20b, "The native bridge is not PE32+ (Windows x64).");
        Require(ReadUInt32(image, optionalOffset + 108) > 1, "The native bridge has no import-directory slot.");

        var importDirectoryOffset = optionalOffset + 112 + 8;
        var importRva = ReadUInt32(image, importDirectoryOffset);
        var sectionOffset = optionalOffset + optionalSize;
        var sizeOfHeaders = ReadUInt32(image, optionalOffset + 60);
        List<string> result = [];
        if (importRva == 0) return result;

        var descriptorOffset = RvaToFileOffset(image, importRva, sizeOfHeaders, sectionOffset, sectionCount);
        while (true)
        {
            Require(descriptorOffset <= image.Length - 20, "The native bridge has a truncated import descriptor.");
            var originalFirstThunk = ReadUInt32(image, descriptorOffset);
            var timeDateStamp = ReadUInt32(image, descriptorOffset + 4);
            var forwarderChain = ReadUInt32(image, descriptorOffset + 8);
            var nameRva = ReadUInt32(image, descriptorOffset + 12);
            var firstThunk = ReadUInt32(image, descriptorOffset + 16);
            if (originalFirstThunk == 0 && timeDateStamp == 0 && forwarderChain == 0 && nameRva == 0 && firstThunk == 0)
                return result;

            result.Add(ReadAsciiZ(image, RvaToFileOffset(image, nameRva, sizeOfHeaders, sectionOffset, sectionCount)));
            descriptorOffset += 20;
        }
    }

    private static int RvaToFileOffset(byte[] image, uint rva, uint sizeOfHeaders, int sectionOffset, ushort sectionCount)
    {
        if (rva < sizeOfHeaders) return checked((int)rva);

        for (var i = 0; i < sectionCount; i++)
        {
            var offset = checked(sectionOffset + (i * 40));
            Require(offset <= image.Length - 40, "The native bridge has a truncated section header.");
            var virtualSize = ReadUInt32(image, offset + 8);
            var virtualAddress = ReadUInt32(image, offset + 12);
            var rawSize = ReadUInt32(image, offset + 16);
            var rawOffset = ReadUInt32(image, offset + 20);
            var sectionSize = Math.Max(virtualSize, rawSize);
            if (rva < virtualAddress || (ulong)rva >= (ulong)virtualAddress + sectionSize) continue;

            var fileOffset = (ulong)rawOffset + (rva - virtualAddress);
            Require(fileOffset < (ulong)image.Length, "The native bridge has an import RVA outside its image.");
            return checked((int)fileOffset);
        }

        throw new InvalidDataException("The native bridge import RVA has no matching section.");
    }

    private static string ReadAsciiZ(byte[] image, int offset)
    {
        Require((uint)offset < (uint)image.Length, "The native bridge import name is outside its image.");
        var end = offset;
        while (end < image.Length && image[end] != 0) end++;
        Require(end < image.Length, "The native bridge has an unterminated import name.");
        return Encoding.ASCII.GetString(image, offset, end - offset);
    }

    private static ushort ReadUInt16(byte[] image, int offset)
    {
        Require(offset >= 0 && offset <= image.Length - sizeof(ushort), "The native bridge has a truncated integer.");
        return BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(offset));
    }

    private static uint ReadUInt32(byte[] image, int offset)
    {
        Require(offset >= 0 && offset <= image.Length - sizeof(uint), "The native bridge has a truncated integer.");
        return BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuaBridgeContract
    {
        internal uint Magic;
        internal uint ContractSize;
        internal ulong SupportedOperations;
        internal uint ExportTableSize;
        internal ushort AbiMajor;
        internal ushort AbiMinor;
        internal byte PointerSize;
        internal byte LuaIntegerSize;
        internal byte SizeTSize;
        internal byte Reserved;
    }
}
