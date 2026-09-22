using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CheatEngine.SDK.Hosting.Bootstrap;

/// <summary>
///     Produces the NUL-terminated ANSI plugin name that <c>CheatEngine.SDK.Abi.Managed.PluginInitRecord.Name</c> and
///     <c>CheatEngine.SDK.Abi.Native.PluginVersion.PluginName</c> point at. Allocated in native memory once per process
///     and never freed: Cheat Engine reads through the pointer after the bootstrap call has returned, for as long as it
///     runs.
/// </summary>
/// <remarks>
///     <para>
///         <b>Encoding.</b> The factory supplies UTF-8; the record wants the process ANSI code page (Pascal <c>pchar</c>).
///         A pure-ASCII name is copied byte for byte, which is exact in every code page. Any other name is decoded and
///         re-encoded with the ANSI code page of the process (<see cref="Marshal.StringToHGlobalAnsi" />, i.e.
///         <c>WideCharToMultiByte(CP_ACP)</c>): characters the code page cannot represent become <c>?</c>, and the same
///         name shows differently on machines with different system locales. Plugin authors who want a name that survives
///         everywhere keep it ASCII. Whether Cheat Engine 7.7 really interprets the name as ANSI rather than UTF-8 is
///         not established; the ANSI reading follows the official bootstrap.
///     </para>
///     <para>
///         An embedded NUL ends the name early, as it would in any C string. An empty name yields a buffer holding one
///         NUL.
///     </para>
/// </remarks>
internal static unsafe class AnsiNameBuffer
{
	/// <summary>Allocates the buffer. Never freed by design; the pointer is valid for the rest of the process.</summary>
	/// <param name="utf8Name">The UTF-8 name without a terminating NUL.</param>
	/// <returns>The address of the first byte; never null.</returns>
	/// <exception cref="OutOfMemoryException">The native allocation failed.</exception>
	internal static byte* Allocate(ReadOnlySpan<byte> utf8Name)
	{
		int nulIndex = utf8Name.IndexOf((byte) 0);
		if (nulIndex >= 0)
		{
			utf8Name = utf8Name[..nulIndex];
		}

		if (Ascii.IsValid(utf8Name))
		{
			byte* buffer = (byte*) NativeMemory.Alloc((nuint) utf8Name.Length + 1);
			utf8Name.CopyTo(new Span<byte>(buffer, utf8Name.Length));
			buffer[utf8Name.Length] = 0;
			return buffer;
		}

		// Non-ASCII: the only correct target is the process ANSI code page, which the BCL exposes through this call
		// (it uses the system code page on Windows; UTF-8 elsewhere, where Cheat Engine does not run anyway).
		string decoded = Encoding.UTF8.GetString(utf8Name);
		IntPtr ansi = Marshal.StringToHGlobalAnsi(decoded);
		return (byte*) ansi;
	}

	/// <summary>Reads a buffer produced by <see cref="Allocate" /> back as bytes, without the NUL. For diagnostics and tests.</summary>
	/// <param name="buffer">The buffer; null yields an empty span.</param>
	/// <returns>The bytes before the first NUL.</returns>
	internal static ReadOnlySpan<byte> Read(byte* buffer)
	{
		return buffer is null ? default : MemoryMarshal.CreateReadOnlySpanFromNullTerminated(buffer);
	}
}
