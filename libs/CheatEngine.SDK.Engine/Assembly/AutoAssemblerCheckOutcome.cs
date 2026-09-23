using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>The factual result of one <see cref="AutoAssemblerPatcher.TryCheck(string, bool)" /> syntax check.</summary>
/// <remarks>
///     A syntax check is not proof that an activation will succeed: the target or its symbols may change before the
///     activation, and allocations or injections are not attempted. A check never creates an owner.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct AutoAssemblerCheckOutcome
{
	internal AutoAssemblerCheckOutcome(AutoAssemblerCheckOutcomeKind kind, LuaStatus luaStatus, string? hostText,
		bool hostTextTruncated)
	{
		Kind = kind;
		LuaStatus = luaStatus;
		HostText = hostText;
		HostTextTruncated = hostTextTruncated;
	}

	/// <summary>Gets the category of the check.</summary>
	public AutoAssemblerCheckOutcomeKind Kind
	{
		get;
	}

	/// <summary>
	///     Gets the protected Lua status for <see cref="AutoAssemblerCheckOutcomeKind.ProtectedLuaFailure" />; otherwise
	///     <see cref="LuaStatus.Ok" />.
	/// </summary>
	public LuaStatus LuaStatus
	{
		get;
	}

	/// <summary>
	///     Gets Cheat Engine's bounded, unparsed error text for <see cref="AutoAssemblerCheckOutcomeKind.Rejected" />, only
	///     when <see cref="AutoAssemblerOptions.CaptureHostText" /> was set and Cheat Engine returned a string; otherwise
	///     <see langword="null" />.
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

	/// <summary>Gets whether Cheat Engine accepted the script section.</summary>
	public bool IsAccepted => Kind == AutoAssemblerCheckOutcomeKind.Accepted;
}
