using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Lua.Registration;

/// <summary>One protected cleanup operation that could not complete for a named registration entry.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LuaRegistrationReleaseFailure(string Name, LuaStatus LuaStatus);
