using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.References;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Internal patch-publication seam used only for deterministic post-effect failure tests.</summary>
internal delegate AutoAssemblerPatch AutoAssemblerPatchFactory(string script, LuaRef disableInfo,
    TargetProcessIncarnation targetIncarnation);
