namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Controls the optional <c>shallow</c> argument of Cheat Engine's <c>getAddressSafe</c>.</summary>
/// <remarks>
///     Target and host symbol spaces have separate result types. <see cref="Shallow" /> is forwarded without managed
///     reinterpretation; <see cref="EngineInspection.ResolveAddress" /> always queries the target table and
///     <see cref="EngineInspection.ResolveHostAddress" /> always queries the host table.
/// </remarks>
public readonly record struct AddressResolutionOptions
{
	/// <summary>Creates options for the optional CE <c>shallow</c> argument.</summary>
	public AddressResolutionOptions(bool Shallow = false)
	{
		this.Shallow = Shallow;
	}

	/// <summary>Value for CE's optional <c>shallow</c> argument.</summary>
	public bool Shallow
	{
		get;
		init;
	}
}
