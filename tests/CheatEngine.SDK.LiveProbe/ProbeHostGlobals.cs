using CheatEngine.SDK.Annotations.Lua;

namespace LiveProbe;

/// <summary>Minimal CE Lua globals required only to verify the operator's target attachment.</summary>
internal static partial class ProbeHostGlobals
{
    /// <summary>Reads CE's current selected process identifier.</summary>
    [LuaGlobal("getOpenedProcessID")]
    internal static partial long GetOpenedProcessId();
}
