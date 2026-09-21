using System;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Copies the qualified physical prefix of a classic exported-functions table from caller-bounded bytes.
/// </summary>
/// <remarks>
///     This is not a classic-host facade. It neither retains the source bytes nor invokes, dereferences, or assigns
///     any slot in the copied table. A future host integration must establish the source buffer's lifetime separately.
/// </remarks>
internal static class ClassicExportedFunctionsPrefixReader
{
    /// <summary>Number of bytes occupied by the table's declared-size field.</summary>
    internal const int DeclaredSizeByteCount = sizeof(int);

    /// <summary>Number of bytes in the only physically mapped classic table prefix.</summary>
    internal const int DirectPrefixByteCount = 144;

    /// <summary>
    ///     Attempts to copy the mapped prefix when both its declaration and the caller-provided physical buffer prove
    ///     that all prefix bytes are available.
    /// </summary>
    internal static bool TryCopy(ReadOnlySpan<byte> tableBytes, out ExportedFunctionsPrefix prefix)
    {
        prefix = default;
        if (tableBytes.Length < DeclaredSizeByteCount) return false;

        var declaredSize = MemoryMarshal.Read<int>(tableBytes);
        if (declaredSize < DirectPrefixByteCount || tableBytes.Length < DirectPrefixByteCount) return false;

        prefix = MemoryMarshal.Read<ExportedFunctionsPrefix>(tableBytes);
        return true;
    }
}
