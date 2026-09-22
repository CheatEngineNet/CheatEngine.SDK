namespace LiveProbe;

/// <summary>The lifecycle stage at which the fault-injection switch throws, if any.</summary>
internal enum LiveProbeFaultStage
{
	/// <summary>No fault: the switch is absent, ignored or explicitly <c>None</c>.</summary>
	None,

	/// <summary>The plugin factory throws before it constructs the plugin (the enable fails and is retried later).</summary>
	FactoryCreate,

	/// <summary><c>OnEnable</c> throws after the console commands are registered.</summary>
	OnEnable,

	/// <summary><c>OnDisable</c> throws after the console commands are unregistered.</summary>
	OnDisable
}
