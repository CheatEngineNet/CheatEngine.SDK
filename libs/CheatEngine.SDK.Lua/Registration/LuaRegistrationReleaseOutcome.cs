using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Lua.Registration;

/// <summary>Reports the complete result of an ownership-guarded lease release or registration rollback.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LuaRegistrationReleaseOutcome
{
	private readonly IReadOnlyList<LuaRegistrationReleaseFailure>? _failures;

	internal LuaRegistrationReleaseOutcome(LuaRegistrationReleaseKind kind, int removedCount, int restoredCount,
		int replacementCount, int remainingCount, IReadOnlyList<LuaRegistrationReleaseFailure> failures)
	{
		Kind = kind;
		RemovedCount = removedCount;
		RestoredCount = restoredCount;
		ReplacementCount = replacementCount;
		RemainingCount = remainingCount;
		_failures = failures;
	}

	/// <summary>Gets the stable outcome category.</summary>
	public LuaRegistrationReleaseKind Kind
	{
		get;
	}

	/// <summary>Gets the number of globals this lease removed because no prior value existed.</summary>
	public int RemovedCount
	{
		get;
	}

	/// <summary>
	///     Gets the number of prior values this lease restored under
	///     <see cref="LuaRegistrationCollisionPolicy.ReplaceExisting" />.
	/// </summary>
	public int RestoredCount
	{
		get;
	}

	/// <summary>Gets the number of entries a later owner replaced and this lease deliberately did not write.</summary>
	public int ReplacementCount
	{
		get;
	}

	/// <summary>Gets the number of entries whose cleanup state remained unconfirmed after this attempt.</summary>
	public int RemainingCount
	{
		get;
	}

	/// <summary>Gets every independent protected cleanup failure, in registration order.</summary>
	public IReadOnlyList<LuaRegistrationReleaseFailure> Failures =>
		_failures ?? Array.Empty<LuaRegistrationReleaseFailure>();

	/// <summary>Gets whether no cleanup operation failed.</summary>
	public bool IsComplete => Kind is LuaRegistrationReleaseKind.NotAttempted or LuaRegistrationReleaseKind.Released
		or LuaRegistrationReleaseKind.AlreadyReleased or LuaRegistrationReleaseKind.Stale;

	internal static LuaRegistrationReleaseOutcome NotAttempted()
	{
		return new LuaRegistrationReleaseOutcome(LuaRegistrationReleaseKind.NotAttempted, 0, 0, 0, 0,
			Array.Empty<LuaRegistrationReleaseFailure>());
	}

	internal static LuaRegistrationReleaseOutcome AlreadyReleased()
	{
		return new LuaRegistrationReleaseOutcome(LuaRegistrationReleaseKind.AlreadyReleased, 0, 0, 0, 0,
			Array.Empty<LuaRegistrationReleaseFailure>());
	}

	internal static LuaRegistrationReleaseOutcome Stale(int remainingCount)
	{
		return new LuaRegistrationReleaseOutcome(LuaRegistrationReleaseKind.Stale, 0, 0, 0, remainingCount,
			Array.Empty<LuaRegistrationReleaseFailure>());
	}
}
