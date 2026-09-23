namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>The operating system Cheat Engine reports it runs on, decoded from <c>getOperatingSystem</c>.</summary>
/// <remarks>
///     <para>
///         Values are semantic, not the raw Lua integers: <see cref="RuntimeInfo.TryDecodeOperatingSystem" /> maps the
///         codes documented by the CE 7.7 Lua catalogue (<c>celua.txt:14</c>: 0, 1, 2 for Windows, macOS and Linux).
///         The public CE source at ec45d5f returns 0 on Windows and 1 on every other build (
///         <c>LuaHandler.pas:14863-14867</c>,
///         ObservedSource), so a non-Windows code is catalogue evidence only.
///     </para>
///     <para>
///         Only <see cref="Windows" /> is part of the qualified profile (code 0, observed on CE 7.7.0.10621 x64 by spike
///         C3, a Lua-only design input). Another value is a reported fact, not a supported host.
///     </para>
/// </remarks>
public enum CheatEngineOperatingSystem : byte
{
	/// <summary>No operating-system fact is available.</summary>
	Unknown = 0,

	/// <summary>Cheat Engine reported Windows (code 0).</summary>
	Windows = 1,

	/// <summary>Cheat Engine reported macOS (code 1).</summary>
	MacOS = 2,

	/// <summary>Cheat Engine reported Linux (code 2).</summary>
	Linux = 3
}
