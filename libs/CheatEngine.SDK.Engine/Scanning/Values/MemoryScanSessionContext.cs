using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>SDK-internal immutable context captured for one owned scan-session pair.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MemoryScanSessionContext(
	LuaStateIdentity RuntimeIdentity,
	TargetSelectionObservation TargetObservation)
{
	internal static MemoryScanSessionContext Capture(LuaState state)
	{
		int top = state.Top;
		try
		{
			return new MemoryScanSessionContext(LuaRuntime.CurrentStateIdentity, TargetSelection.ObserveCurrent(state));
		}
		finally
		{
			state.SetTop(top);
		}
	}
}
