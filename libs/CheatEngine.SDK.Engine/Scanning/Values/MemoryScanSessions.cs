using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Threading;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Creates plugin-owned <see cref="MemoryScanSession" /> instances through Cheat Engine's scan factories.</summary>
/// <remarks>
///     <para>
///         Cheat Engine 7.7's <c>createMemScan()</c> creates the scanner and <c>createFoundList(memscan)</c> creates
///         its result-list child. This factory is the only production path that turns either returned host object into
///         an <see cref="Owned{T}" />. It therefore establishes the parent/child ownership relationship before exposing
///         the stateful session to managed code.
///     </para>
///     <para>
///         Both globals are resolved in one held <see cref="LuaRuntimeOperation" />. If creating, decoding, or publishing
///         either owner fails after the scanner was created, the factory invokes <c>destroy()</c> on the still-unpublished
///         child first and then on its scanner before it returns failure. A successfully created session owns the child
///         before the parent and preserves
///         <see cref="MemoryScanSession" />'s child-before-parent disposal order. A caller cannot construct an
///         <see cref="Owned{T}" /> for either handle from a borrowed value because that constructor remains internal to
///         the SDK.
///     </para>
/// </remarks>
public static class MemoryScanSessions
{
	private static readonly LuaRef SCreateFoundList = new();
	private static readonly LuaRef SCreateMemScan = new();

	/// <summary>Creates one new plugin-owned scanner and its attached plugin-owned found-list child.</summary>
	/// <param name="session">The new session on success; <see langword="null" /> otherwise.</param>
	/// <returns>
	///     <see langword="true" /> when both documented factory calls returned valid host objects. Returns
	///     <see langword="false" /> when either global is unavailable, either protected call fails, or a result is not
	///     a non-null host object. In every failure after scanner creation, the child (if present) is rolled back before
	///     the scanner, including a failure while publishing either managed owner.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	///     The plugin is not enabled, the caller has no host Lua state, the host cannot push objects, or the caller is
	///     not on Cheat Engine's main thread.
	/// </exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public static bool TryCreate([NotNullWhen(true)] out MemoryScanSession? session)
	{
		return TryCreateDetailed(out session) == MemoryScanCreationStatus.Success;
	}

	/// <summary>Creates a scanner/found-list pair and reports the factual creation category.</summary>
	/// <param name="session">
	///     The new context-bound session only when the returned status is
	///     <see cref="MemoryScanCreationStatus.Success" />.
	/// </param>
	/// <returns>
	///     The factory result without parsing a CE error message. A documented <see langword="nil" /> result, a
	///     protected Lua failure and a malformed non-null result remain distinct. A detached runtime or a worker-thread
	///     caller still throws because no safe Lua operation can begin.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	///     The plugin is not enabled, the caller has no host Lua state, the host cannot push objects, or the caller is
	///     not on Cheat Engine's main thread.
	/// </exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public static MemoryScanCreationStatus TryCreateDetailed([NotNullWhen(true)] out MemoryScanSession? session)
	{
		return TryCreateWithOutcome(out session).Status;
	}

	/// <summary>
	///     Creates a scanner/found-list pair and returns the pre-acquisition target observation with the factual factory
	///     result.
	/// </summary>
	/// <param name="session">
	///     The new context-bound session only when <see cref="MemoryScanCreationOutcome.Status" /> is
	///     <see cref="MemoryScanCreationStatus.Success" />.
	/// </param>
	/// <returns>
	///     The factory result and the exact target observation made before <c>createMemScan()</c>. An unqualified
	///     observation returns <see cref="MemoryScanCreationStatus.TargetIdentityUnavailable" /> and invokes neither CE
	///     factory.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	///     The plugin is not enabled, the caller has no host Lua state, the host cannot push objects, or the caller is
	///     not on Cheat Engine's main thread.
	/// </exception>
	[MainThreadOnly]
	[RequiresPluginEnabled]
	public static MemoryScanCreationOutcome TryCreateWithOutcome([NotNullWhen(true)] out MemoryScanSession? session)
	{
		MemoryScanCreationStatus status = TryCreateDetailedCore(out session, CreateSession,
			out TargetSelectionObservation targetObservation);
		return new MemoryScanCreationOutcome(status, targetObservation);
	}

	// Tests use this seam to prove that an ownership-transfer failure rolls the child back before its parent. The raw
	// handles remain available until their wrapper is constructed, so even an allocation failure during publication
	// has one direct rollback authority. This is internal deliberately: callers can select neither the owner
	// construction nor a different adoption policy.
	internal static bool TryCreateCore([NotNullWhen(true)] out MemoryScanSession? session,
		MemoryScanSessionAdopter adopter)
	{
		return TryCreateDetailedCore(out session, adopter, out _) == MemoryScanCreationStatus.Success;
	}

	private static MemoryScanCreationStatus TryCreateDetailedCore([NotNullWhen(true)] out MemoryScanSession? session,
		MemoryScanSessionAdopter adopter, out TargetSelectionObservation targetObservation)
	{
		ArgumentNullException.ThrowIfNull(adopter);
		RequireEnabledMainThread();

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		Owned<MemScan>? scanner = null;
		Owned<FoundList>? foundList = null;
		CEObject scannerHandle = CEObject.Null;
		CEObject foundListHandle = CEObject.Null;
		MemoryScanCreationStatus status = MemoryScanCreationStatus.Success;
		MemoryScanSessionContext context = MemoryScanSessionContext.Capture(state);
		targetObservation = context.TargetObservation;
		session = null;
		try
		{
			// A MemScan/FoundList pair acts against CE's ambient target. Do not acquire either caller-owned resource
			// until the observation can identify a process incarnation for every later session operation and cleanup.
			if (!context.TargetObservation.IsQualified)
			{
				return MemoryScanCreationStatus.TargetIdentityUnavailable;
			}

			status = TryCreateScanner(state, out scanner, out scannerHandle);
			if (status == MemoryScanCreationStatus.Success)
			{
				status = TryCreateFoundList(state, scanner!.Value, out foundList, out foundListHandle);
			}

			if (status == MemoryScanCreationStatus.Success)
			{
				session = adopter(scanner!, foundList!);
				session.Bind(context);
			}
		}
		finally
		{
			// Adoption transfers and empties both wrappers. Every other exit after construction must release the child
			// before the parent while the original operation is still admitted. Swallowing a protected destroy failure
			// avoids hiding the factory failure and, like Owned<T>.Dispose, never retries an unknown native state.
			bool foundListRollbackFailed = !TryRollback(state, foundList, foundListHandle);
			bool scannerRollbackFailed = !TryRollback(state, scanner, scannerHandle);
			if ((foundListRollbackFailed || scannerRollbackFailed)
			    && session is null
			    && status != MemoryScanCreationStatus.Success)
			{
				status = MemoryScanCreationStatus.RollbackUnconfirmed;
			}
		}

		return status;
	}

	private static MemoryScanCreationStatus TryCreateScanner(LuaState state, out Owned<MemScan>? scanner,
		out CEObject scannerHandle)
	{
		scanner = null;
		scannerHandle = CEObject.Null;
		LuaGlobalPushStatus global = LuaGlobalFunctions.TryPushWithStatus(state, SCreateMemScan, "createMemScan"u8);
		if (global == LuaGlobalPushStatus.Unavailable)
		{
			return MemoryScanCreationStatus.GlobalUnavailable;
		}

		if (global != LuaGlobalPushStatus.Success || !state.TryCall(0, 1).IsOk)
		{
			return MemoryScanCreationStatus.LuaFailure;
		}

		if (state.IsNil(-1))
		{
			return MemoryScanCreationStatus.NoScannerResult;
		}

		if (!CEObject.TryRead(state, -1, out scannerHandle))
		{
			return MemoryScanCreationStatus.InvalidScannerResult;
		}

		scanner = new Owned<MemScan>(MemScan.FromHandle(scannerHandle));
		scannerHandle = CEObject.Null;
		return MemoryScanCreationStatus.Success;
	}

	private static MemoryScanCreationStatus TryCreateFoundList(LuaState state, MemScan scanner,
		out Owned<FoundList>? foundList, out CEObject foundListHandle)
	{
		foundList = null;
		foundListHandle = CEObject.Null;
		LuaGlobalPushStatus global = LuaGlobalFunctions.TryPushWithStatus(state, SCreateFoundList, "createFoundList"u8);
		if (global == LuaGlobalPushStatus.Unavailable)
		{
			return MemoryScanCreationStatus.GlobalUnavailable;
		}

		if (global != LuaGlobalPushStatus.Success)
		{
			return MemoryScanCreationStatus.LuaFailure;
		}

		scanner.Handle.Push(state);
		if (!state.TryCall(1, 1).IsOk)
		{
			return MemoryScanCreationStatus.LuaFailure;
		}

		if (state.IsNil(-1))
		{
			return MemoryScanCreationStatus.NoFoundListResult;
		}

		if (!CEObject.TryRead(state, -1, out foundListHandle))
		{
			return MemoryScanCreationStatus.InvalidFoundListResult;
		}

		if (foundListHandle == scanner.Handle)
		{
			foundListHandle = CEObject.Null;
			return MemoryScanCreationStatus.AliasedFoundList;
		}

		foundList = new Owned<FoundList>(FoundList.FromHandle(foundListHandle));
		foundListHandle = CEObject.Null;
		return MemoryScanCreationStatus.Success;
	}

	private static bool TryRollback<T>(LuaState state, Owned<T>? owner, CEObject unpublishedHandle)
		where T : struct, ICEObject<T>
	{
		using LuaFrame rollbackFrame = new(state);
		if (owner is not null && !owner.IsDisposed)
		{
			return owner.TryDestroy(state).IsOk;
		}

		if (!unpublishedHandle.IsNull)
		{
			return unpublishedHandle.TryDestroy(state).IsOk;
		}

		return true;
	}

	private static MemoryScanSession CreateSession(Owned<MemScan> scanner, Owned<FoundList> foundList)
	{
		return MemoryScanSession.AdoptUnbound(scanner, foundList);
	}

	private static void RequireEnabledMainThread()
	{
		if (!LuaRuntime.IsAttached)
		{
			throw new InvalidOperationException(
				"The Cheat Engine plugin is not enabled, so a memory scan session cannot be created.");
		}

		if (!LuaRuntime.IsMainThread)
		{
			throw new InvalidOperationException(
				"Memory scan session creation must run on Cheat Engine's main thread.");
		}
	}
}
