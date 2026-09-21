using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Lua.Registration;

/// <summary>Identifies the entry and protected Lua status of one registration failure.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LuaRegistrationFailure(string Name, LuaStatus LuaStatus);
