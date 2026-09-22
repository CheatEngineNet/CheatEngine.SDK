using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.Targets;

/// <summary>A copied target-incarnation validation result for a target-bound owner.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TargetIdentityCheck
{
	internal TargetIdentityCheck(TargetIdentityCheckKind kind, TargetSelectionObservation observed)
	{
		Kind = kind;
		Observed = observed;
	}

	/// <summary>Gets the stable validation category.</summary>
	public TargetIdentityCheckKind Kind
	{
		get;
	}

	/// <summary>Gets the current target observation used for validation.</summary>
	public TargetSelectionObservation Observed
	{
		get;
	}

	/// <summary>Gets whether the current qualified target matches the owner-bound incarnation.</summary>
	public bool IsCurrent => Kind == TargetIdentityCheckKind.Current;
}
