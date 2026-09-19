using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CheatEngine.SDK.Hosting.Diagnostics;

/// <summary>
///     The default <see cref="IHostLogSink" />: writes each entry to the Windows debug output stream
///     (<c>OutputDebugStringW</c>), where a debugger attached to the Cheat Engine process or a tool such as DebugView
///     shows it. No dependency, no file, no console; a no-op on other operating systems.
/// </summary>
/// <remarks>
///     Format: <c>[CheatEngine.SDK.Hosting] Error: message</c>, followed by the exception's text on the next lines when
///     there is one. Thread-safe (the OS call is), never throws, allocates the formatted string per entry (error paths only).
/// </remarks>
public sealed partial class DebugOutputLogSink : IHostLogSink
{
    private DebugOutputLogSink()
    {
    }

    /// <summary>Gets the shared instance.</summary>
    public static DebugOutputLogSink Instance { get; } = new();

    /// <inheritdoc />
    public void Write(HostLogLevel level, string message, Exception? exception)
    {
        if (!OperatingSystem.IsWindows()) return;

        var text = exception is null
            ? "[CheatEngine.SDK.Hosting] " + level + ": " + message + "\n"
            : "[CheatEngine.SDK.Hosting] " + level + ": " + message + "\n" + exception + "\n";
        WriteToDebugOutput(text);
    }

    [SupportedOSPlatform("windows")]
    private static unsafe void WriteToDebugOutput(string text)
    {
        fixed (char* p = text)
        {
            OutputDebugStringW(p);
        }
    }

    // void OutputDebugStringW(LPCWSTR lpOutputString); the string is read during the call only.
    [LibraryImport("kernel32", EntryPoint = "OutputDebugStringW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SupportedOSPlatform("windows")]
    private static unsafe partial void OutputDebugStringW(char* lpOutputString);
}
