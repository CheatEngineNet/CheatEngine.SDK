namespace CheatEngine.SDK.Hosting.Tests.Coexistence;

/// <summary>One completed run of <c>ce-host-emulator.exe</c>: its process exit code and its parsed facts file.</summary>
internal sealed record NativeHostEmulatorResult(int ExitCode, IReadOnlyDictionary<string, string> Facts);
