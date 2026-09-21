using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Internal disable-info rooting seam used only for deterministic post-effect failure tests.</summary>
internal delegate LuaRef AutoAssemblerDisableInfoTracker(LuaState state);
