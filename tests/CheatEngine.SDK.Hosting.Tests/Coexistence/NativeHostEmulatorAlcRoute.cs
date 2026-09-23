namespace CheatEngine.SDK.Hosting.Tests.Coexistence;

/// <summary>The hostfxr assembly-load-context route the emulator drives a scenario with.</summary>
internal enum NativeHostEmulatorAlcRoute
{
	/// <summary>The documented <c>hdt_load_assembly_and_get_function_pointer</c> route: one isolated ALC per assembly path.</summary>
	Component,

	/// <summary>The undocumented <c>hdt_load_assembly</c> + <c>hdt_get_function_pointer</c> route into the default ALC.</summary>
	Default,
}
