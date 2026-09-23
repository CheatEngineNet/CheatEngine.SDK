namespace CheatEngine.SDK.Hosting.Bootstrap;

/// <summary>
///     The stable and transitional states of the one managed plugin hosted by this loaded
///     <c>CheatEngine.SDK.Hosting</c> assembly instance. Whether that equals one per assembly load context, or one per
///     plugin, is decided by the host loader profile (Q09: C2 host-emulator facts, C4 receipts), not assumed here.
/// </summary>
/// <remarks>
///     Cheat Engine drives the transitions through the generated bootstrap's native callbacks. <see cref="Enabled" />
///     is the only state for which <see cref="PluginHost.IsEnabled" /> is <see langword="true" />. A context is
///     published during <see cref="Enabling" /> and remains available during <see cref="Disabling" /> so lifecycle
///     callbacks can use the attached Lua runtime, but ordinary new main-thread dispatches are not admitted while the
///     host is stopping.
/// </remarks>
public enum PluginHostLifecyclePhase
{
	/// <summary>No factory has been registered by <see cref="PluginHost.InitializeManaged{TFactory}" />.</summary>
	Uninitialized = 0,

	/// <summary>A factory is registered and the host may request an enable.</summary>
	Registered = 1,

	/// <summary>The host is binding Lua, creating the plugin, or running <c>OnEnable</c>.</summary>
	Enabling = 2,

	/// <summary>The plugin completed <c>OnEnable</c> and accepts ordinary work.</summary>
	Enabled = 3,

	/// <summary>The host closed admission, signalled shutdown, or is running <c>OnDisable</c> and cleanup.</summary>
	Disabling = 4
}
