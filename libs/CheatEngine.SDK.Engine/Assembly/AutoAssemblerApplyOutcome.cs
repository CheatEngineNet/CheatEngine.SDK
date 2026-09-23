using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>
///     The factual result of one
///     <see cref="AutoAssemblerPatcher.TryApplyWithOutcome(string, AutoAssemblerOptions, out AutoAssemblerPatch?)" />
///     activation attempt.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Effect" /> follows from <see cref="Kind" />: <see cref="AutoAssemblerApplyOutcomeKind.Applied" /> is
///         <see cref="EngineEffectState.Applied" />; <see cref="AutoAssemblerApplyOutcomeKind.GlobalUnavailable" /> and
///         <see cref="AutoAssemblerApplyOutcomeKind.TargetIdentityUnavailable" /> are
///         <see cref="EngineEffectState.NotStarted" />; every other kind is <see cref="EngineEffectState.Unknown" />. A
///         rejection by Cheat Engine does not prove that nothing changed (a script can apply part of its effects before
///         it fails), and a target change observed after the effect makes the effect uncertain, not rolled back.
///     </para>
///     <para>
///         The host text is copied only when <see cref="AutoAssemblerOptions.CaptureHostText" /> is set, bounded, and is
///         never parsed. <see cref="HasHostWarnings" /> is always reported when Cheat Engine returned a third result, even
///         when its text was not captured.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct AutoAssemblerApplyOutcome
{
	internal AutoAssemblerApplyOutcome(AutoAssemblerApplyOutcomeKind kind, LuaStatus luaStatus,
		TargetSelectionObservation targetObservation, AutoAssemblerHostTextCopy hostText,
		AutoAssemblerHostTextCopy hostWarnings, bool hasHostWarnings, TargetIdentityCheck? postEffectTargetCheck,
		AutoAssemblerDisableInfoSnapshot? disableInfo, TargetReleaseOutcome? compensation, bool hasPatch)
	{
		Kind = kind;
		LuaStatus = luaStatus;
		TargetObservation = targetObservation;
		HostText = hostText.Text;
		HostTextTruncated = hostText.Truncated;
		HostWarnings = hostWarnings.Text;
		HostWarningsTruncated = hostWarnings.Truncated;
		HasHostWarnings = hasHostWarnings;
		PostEffectTargetCheck = postEffectTargetCheck;
		DisableInfo = disableInfo;
		Compensation = compensation;
		HasPatch = hasPatch;
	}

	/// <summary>Gets the category of the attempt.</summary>
	public AutoAssemblerApplyOutcomeKind Kind
	{
		get;
	}

	/// <summary>Gets how far the activation went, derived from <see cref="Kind" /> only.</summary>
	public EngineEffectState Effect => Kind switch
	{
		AutoAssemblerApplyOutcomeKind.Applied => EngineEffectState.Applied,
		AutoAssemblerApplyOutcomeKind.GlobalUnavailable or AutoAssemblerApplyOutcomeKind.TargetIdentityUnavailable =>
			EngineEffectState.NotStarted,
		_ => EngineEffectState.Unknown
	};

	/// <summary>
	///     Gets the protected Lua status for <see cref="AutoAssemblerApplyOutcomeKind.ProtectedLuaFailure" />; otherwise
	///     <see cref="LuaStatus.Ok" />.
	/// </summary>
	public LuaStatus LuaStatus
	{
		get;
	}

	/// <summary>
	///     Gets Cheat Engine's bounded, unparsed rejection detail, only for
	///     <see cref="AutoAssemblerApplyOutcomeKind.Rejected" /> when host text was captured and the detail is a string.
	/// </summary>
	public string? HostText
	{
		get;
	}

	/// <summary>Gets whether <see cref="HostText" /> was cut at <see cref="AutoAssemblerOptions.MaxHostTextBytes" />.</summary>
	public bool HostTextTruncated
	{
		get;
	}

	/// <summary>Gets whether Cheat Engine returned a non-nil third result (compilation warnings).</summary>
	public bool HasHostWarnings
	{
		get;
	}

	/// <summary>
	///     Gets Cheat Engine's bounded, unparsed compilation warnings, only when host text was captured and the third
	///     result is a string.
	/// </summary>
	public string? HostWarnings
	{
		get;
	}

	/// <summary>Gets whether <see cref="HostWarnings" /> was cut at <see cref="AutoAssemblerOptions.MaxHostTextBytes" />.</summary>
	public bool HostWarningsTruncated
	{
		get;
	}

	/// <summary>Gets the target observation made before the activation.</summary>
	public TargetSelectionObservation TargetObservation
	{
		get;
	}

	/// <summary>
	///     Gets the validation of the captured target made after Cheat Engine applied the script, inside the same Lua
	///     operation; <see langword="null" /> when the script was not applied.
	/// </summary>
	public TargetIdentityCheck? PostEffectTargetCheck
	{
		get;
	}

	/// <summary>
	///     Gets the bounded copy of the disable information for an applied script (also when publication failed and the
	///     copy succeeded); otherwise <see langword="null" />.
	/// </summary>
	public AutoAssemblerDisableInfoSnapshot? DisableInfo
	{
		get;
	}

	/// <summary>
	///     Gets the result of the one compensating disable, set only for
	///     <see cref="AutoAssemblerApplyOutcomeKind.HandoffFailed" />.
	/// </summary>
	public TargetReleaseOutcome? Compensation
	{
		get;
	}

	/// <summary>Gets whether a patch owner was published through the <c>out</c> parameter.</summary>
	public bool HasPatch
	{
		get;
	}
}
