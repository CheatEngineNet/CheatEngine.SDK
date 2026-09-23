using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Protected bindings for CE's user-defined symbol registry and address-name formatter.</summary>
/// <remarks>
///     <para>
///         <b>Provenance and compatibility.</b> This surface binds exactly the Client source-record call shapes for
///         <c>registerSymbol</c>, <c>unregisterSymbol</c>, and <c>getNameFromAddress</c>. It deliberately does not add
///         unqualified optional arguments or a managed name-selection policy. Registration keeps Cheat Engine's own
///         behavior for an existing name: the SDK adds no collision preflight (a consumer that needs a collision policy
///         resolves the name first with <see cref="EngineInspection.ResolveAddress" />).
///     </para>
///     <para>
///         <b>Lifetime and ownership.</b> Lookup returns a newly allocated managed string and owns no Lua reference or
///         CE object. Registering a symbol mutates CE's host-wide symbol table; it does not yield an independently owned
///         CE resource, and this class intentionally does not claim exclusive ownership of a name.
///         <see cref="TryRegisterOwned" /> returns a lease that records the registered address and, before its one
///         unregister, verifies that the name still resolves to it (best effort, not atomic: CE has no registration
///         token). Cheat Engine's global that deletes every registered symbol at once is deliberately not bound: it is
///         never a per-plugin cleanup path, because it also removes the symbols of scripts, tables and other plugins.
///     </para>
///     <para>
///         Calls are generated through the SDK's protected, attach-epoch-aware Lua binding path. They restore the Lua
///         stack and return <see cref="LuaOperationStatus" /> without parsing error text.
///     </para>
/// </remarks>
public static partial class SymbolRegistry
{
	private static readonly Lock SOwnedRegistrationGate = new();
	private static readonly Dictionary<SymbolName, SymbolRegistrationLease> SOwnedRegistrations = new();

	/// <summary>
	///     Gets the status to report for a release step that made no Lua call at all: <see langword="null" />. A no-call
	///     outcome is never represented by <see langword="default" />(<see cref="LuaOperationStatus" />), because
	///     <see cref="LuaOperationStatusKind" /> currently numbers <see cref="LuaOperationStatusKind.Success" /> as its
	///     zero value, which would make <see cref="LuaOperationStatus.IsSuccess" /> read <see langword="true" /> for a
	///     release that never called Cheat Engine (A08-26).
	/// </summary>
	private static LuaOperationStatus? NoBindingCall => null;

	/// <summary>Gets CE's formatted name for a target-process address with CE's default name sources.</summary>
	/// <param name="address">The target-process address passed to CE as the sole argument.</param>
	/// <param name="name">A copied managed string only when the returned status is successful.</param>
	/// <returns>The protected binding outcome.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[LuaGlobal("getNameFromAddress")]
	[RequiresPluginEnabled]
	public static partial LuaOperationStatus TryGetName([LuaMarshaller(typeof(Address))] Address address,
		out string? name);

	/// <summary>Registers a user-defined symbol at a target-process address.</summary>
	/// <param name="name">The registration name CE will add to its symbol handler.</param>
	/// <param name="address">The target-process address associated with <paramref name="name" />.</param>
	/// <param name="options">The persistence option for the registration, forwarded unchanged as CE's <c>donotsave</c>.</param>
	/// <returns>The protected binding outcome.</returns>
	/// <exception cref="ArgumentException"><paramref name="name" /> is default or otherwise invalid.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static LuaOperationStatus Register(SymbolName name, Address address,
		SymbolRegistrationOptions options = default)
	{
		ValidateName(name);
		lock (SOwnedRegistrationGate)
		{
			LuaOperationStatus status = RegisterCore(name, address, options.DoNotSave);
			if (OperationCouldHaveStarted(status))
			{
				SupersedeCurrentLease(name);
			}

			return status;
		}
	}

	/// <summary>Removes a user-defined symbol name from CE's symbol handler.</summary>
	/// <param name="name">The registration name to remove.</param>
	/// <returns>The protected binding outcome.</returns>
	/// <exception cref="ArgumentException"><paramref name="name" /> is default or otherwise invalid.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static LuaOperationStatus Unregister(SymbolName name)
	{
		ValidateName(name);
		lock (SOwnedRegistrationGate)
		{
			LuaOperationStatus status = UnregisterCore(name);
			if (OperationCouldHaveStarted(status))
			{
				SupersedeCurrentLease(name);
			}

			return status;
		}
	}

	/// <summary>Registers a symbol and returns an explicit, replacement-checking cleanup lease on success.</summary>
	/// <param name="name">The registration name CE will add to its symbol handler.</param>
	/// <param name="address">The target-process address associated with <paramref name="name" />.</param>
	/// <param name="options">The persistence option for the registration, forwarded unchanged as CE's <c>donotsave</c>.</param>
	/// <returns>The protected registration status and a lease only on successful registration.</returns>
	/// <exception cref="ArgumentException"><paramref name="name" /> is default or otherwise invalid.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <exception cref="SymbolRegistrationHandoffException">
	///     CE registered the name but managed lease construction or publication failed; the exception records the one
	///     coordinator-qualified compensation attempt.
	/// </exception>
	/// <remarks>
	///     The lease prevents older leases from unregistering a newer registration made through this coordinator, and
	///     skips its unregister when the name was removed or now resolves to another address (a third-party
	///     replacement). CE has no registration token, so that check is best effort and not atomic. A registration over
	///     an existing name keeps Cheat Engine's behavior: when CE refuses it, no lease is created and nothing is
	///     unregistered. A successful CE registration is compensated once if its managed lease cannot be published;
	///     callers must inspect <see cref="SymbolRegistrationHandoffException.CleanupOutcome" /> rather than retrying by
	///     name.
	/// </remarks>
	[RequiresPluginEnabled]
	public static SymbolRegistrationAcquireOutcome TryRegisterOwned(SymbolName name, Address address,
		SymbolRegistrationOptions options = default)
	{
		return TryRegisterOwnedCore(name, address, options, CreateLease, PublishLease);
	}

	// The seams are internal test infrastructure. They make the otherwise exceptional interval between a successful CE
	// registration and managed lease publication deterministic, without allowing consumers to choose a different
	// ownership or cleanup policy.
	internal static SymbolRegistrationAcquireOutcome TryRegisterOwnedCore(SymbolName name, Address address,
		SymbolRegistrationOptions options, SymbolRegistrationLeaseFactory leaseFactory,
		SymbolRegistrationLeasePublisher leasePublisher)
	{
		ValidateName(name);
		ArgumentNullException.ThrowIfNull(leaseFactory);
		ArgumentNullException.ThrowIfNull(leasePublisher);
		lock (SOwnedRegistrationGate)
		{
			LuaStateIdentity identity = LuaRuntime.CurrentStateIdentity;
			LuaOperationStatus status = RegisterCore(name, address, options.DoNotSave);
			if (!status.IsSuccess)
			{
				if (OperationCouldHaveStarted(status))
				{
					SupersedeCurrentLease(name);
				}

				return new SymbolRegistrationAcquireOutcome(status, null);
			}

			if (identity != LuaRuntime.CurrentStateIdentity)
			{
				SupersedeCurrentLease(name);
				return new SymbolRegistrationAcquireOutcome(LuaOperationStatus.GlobalUnavailable, null);
			}

			SupersedeCurrentLease(name);
			try
			{
				SymbolRegistrationLease lease = leaseFactory(name, address, options, identity);
				if (lease is null)
				{
					throw new InvalidOperationException("The symbol-registration lease factory returned no lease.");
				}

				leasePublisher(name, lease);
				return new SymbolRegistrationAcquireOutcome(status, lease);
			}
			catch (Exception exception)
			{
				SymbolRegistrationReleaseOutcome cleanupOutcome = CompensateFailedPublication(name, address, identity);
				throw new SymbolRegistrationHandoffException(cleanupOutcome, exception);
			}
		}
	}

	[LuaGlobal("registerSymbol")]
	private static partial LuaOperationStatus RegisterCore([LuaMarshaller(typeof(SymbolName))] SymbolName name,
		[LuaMarshaller(typeof(Address))] Address address, bool doNotSave);

	[LuaGlobal("unregisterSymbol")]
	private static partial LuaOperationStatus UnregisterCore([LuaMarshaller(typeof(SymbolName))] SymbolName name);

	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification =
			"ReleaseOwned is the single atomic lease-cleanup transaction and must preserve its state ordering.")]
	internal static SymbolRegistrationReleaseOutcome ReleaseOwned(SymbolRegistrationLease lease)
	{
		lock (SOwnedRegistrationGate)
		{
			if (lease.ObserveTerminalKind() is { } terminalKind)
			{
				return new SymbolRegistrationReleaseOutcome(terminalKind, NoBindingCall);
			}

			if (!LuaRuntime.IsAttached || lease.Identity != LuaRuntime.CurrentStateIdentity)
			{
				return MarkStaleRuntime(lease, NoBindingCall);
			}

			if (!SOwnedRegistrations.TryGetValue(lease.Name, out SymbolRegistrationLease? current) ||
			    !ReferenceEquals(current, lease))
			{
				lease.MarkTerminalAndObserve(SymbolRegistrationReleaseKind.Superseded);
				return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.Superseded, NoBindingCall);
			}

			ReplacementCheck check = CheckRegistration(lease.Name, lease.Address, lease.Identity);
			switch (check.Kind)
			{
				case SymbolRegistrationReleaseKind.Released:
					return ReleaseCurrentLease(lease);
				case SymbolRegistrationReleaseKind.StaleRuntime:
					return MarkStaleRuntime(lease, NoBindingCall);
				case SymbolRegistrationReleaseKind.Replaced or SymbolRegistrationReleaseKind.ExternallyRemoved:
					RemoveCurrentLease(lease);
					lease.MarkTerminalAndObserve(check.Kind);
					return new SymbolRegistrationReleaseOutcome(check.Kind, check.Status);
				default:
					return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupUnavailable,
						check.Status);
			}
		}
	}

	private static SymbolRegistrationReleaseOutcome ReleaseCurrentLease(SymbolRegistrationLease lease)
	{
		LuaOperationStatus status;
		try
		{
			status = UnregisterCore(lease.Name);
		}
		catch (InvalidOperationException) when (!LuaRuntime.IsAttached ||
		                                        lease.Identity != LuaRuntime.CurrentStateIdentity)
		{
			return MarkStaleRuntime(lease, NoBindingCall);
		}
		catch (InvalidOperationException)
		{
			return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupUnavailable,
				LuaOperationStatus.GlobalUnavailable);
		}
		catch (Exception)
		{
			RemoveCurrentLease(lease);
			lease.MarkTerminalAndObserve(SymbolRegistrationReleaseKind.CleanupIndeterminate);
			return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupIndeterminate,
				LuaOperationStatus.InvalidResult);
		}

		if (status.IsSuccess)
		{
			RemoveCurrentLease(lease);
			if (lease.Identity != LuaRuntime.CurrentStateIdentity)
			{
				return MarkStaleRuntime(lease, status);
			}

			lease.MarkTerminalAndObserve(SymbolRegistrationReleaseKind.Released);
			return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.Released, status);
		}

		if (status.Kind is LuaOperationStatusKind.GlobalUnavailable or LuaOperationStatusKind.StackUnavailable)
		{
			return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupUnavailable, status);
		}

		RemoveCurrentLease(lease);
		lease.MarkTerminalAndObserve(SymbolRegistrationReleaseKind.CleanupIndeterminate);
		return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupIndeterminate, status);
	}

	private static SymbolRegistrationReleaseOutcome MarkStaleRuntime(SymbolRegistrationLease lease,
		LuaOperationStatus? status)
	{
		RemoveCurrentLease(lease);
		lease.MarkTerminalAndObserve(SymbolRegistrationReleaseKind.StaleRuntime);
		return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.StaleRuntime, status);
	}

	// Verifies, before an unregister by name, that the name still resolves to the registered address. The result kind is
	// Released when the unregister may proceed, Replaced/ExternallyRemoved when it must be skipped, StaleRuntime when the
	// runtime detached or changed, and CleanupUnavailable when ownership cannot be verified. Best effort: CE has no
	// registration token, and the lookup is not atomic with the following unregister.
	private static ReplacementCheck CheckRegistration(SymbolName name, Address address, LuaStateIdentity identity)
	{
		InspectionStatus lookup;
		Address current;
		try
		{
			lookup = EngineInspection.ResolveAddress(new SymbolExpression(name.Value), default, out current);
		}
		catch (InvalidOperationException) when (!LuaRuntime.IsAttached || identity != LuaRuntime.CurrentStateIdentity)
		{
			return new ReplacementCheck(SymbolRegistrationReleaseKind.StaleRuntime, NoBindingCall);
		}
		catch (InvalidOperationException)
		{
			return new ReplacementCheck(SymbolRegistrationReleaseKind.CleanupUnavailable,
				LuaOperationStatus.GlobalUnavailable);
		}

		return lookup switch
		{
			InspectionStatus.Success when current == address => new ReplacementCheck(
				SymbolRegistrationReleaseKind.Released, LuaOperationStatus.Success),
			InspectionStatus.Success => new ReplacementCheck(SymbolRegistrationReleaseKind.Replaced,
				LuaOperationStatus.Success),
			InspectionStatus.NotFound => new ReplacementCheck(SymbolRegistrationReleaseKind.ExternallyRemoved,
				LuaOperationStatus.NilResult),
			InspectionStatus.GlobalUnavailable => new ReplacementCheck(SymbolRegistrationReleaseKind.CleanupUnavailable,
				LuaOperationStatus.GlobalUnavailable),
			InspectionStatus.LuaFailure => new ReplacementCheck(SymbolRegistrationReleaseKind.CleanupUnavailable,
				LuaOperationStatus.LuaFailure(LuaStatus.RuntimeError)),
			_ => new ReplacementCheck(SymbolRegistrationReleaseKind.CleanupUnavailable,
				LuaOperationStatus.InvalidResult)
		};
	}

	private static void ValidateName(SymbolName name)
	{
		if (string.IsNullOrWhiteSpace(name.Value))
		{
			throw new ArgumentException("A symbol name must not be default, empty or white space.", nameof(name));
		}
	}

	private static SymbolRegistrationLease CreateLease(SymbolName name, Address address,
		SymbolRegistrationOptions options, LuaStateIdentity identity)
	{
		return new SymbolRegistrationLease(name, address, options, identity);
	}

	private static void PublishLease(SymbolName name, SymbolRegistrationLease lease)
	{
		SOwnedRegistrations.Add(name, lease);
	}

	// Publication runs while the coordinator gate is held, so an SDK-coordinated replacement cannot interleave between
	// the successful register and this one compensation attempt. The identity check prevents an old registration from
	// being unregistered through a replacement Lua runtime, and the replacement check prevents removing a name that no
	// longer maps to the address just registered.
	private static SymbolRegistrationReleaseOutcome CompensateFailedPublication(SymbolName name, Address address,
		LuaStateIdentity identity)
	{
		if (!LuaRuntime.IsAttached || identity != LuaRuntime.CurrentStateIdentity)
		{
			return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.StaleRuntime, NoBindingCall);
		}

		ReplacementCheck check = CheckRegistration(name, address, identity);
		if (check.Kind != SymbolRegistrationReleaseKind.Released)
		{
			return new SymbolRegistrationReleaseOutcome(check.Kind, check.Status);
		}

		LuaOperationStatus status;
		try
		{
			status = UnregisterCore(name);
		}
		catch (InvalidOperationException) when (!LuaRuntime.IsAttached ||
		                                        identity != LuaRuntime.CurrentStateIdentity)
		{
			return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.StaleRuntime, NoBindingCall);
		}
		catch (InvalidOperationException)
		{
			return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupUnavailable,
				LuaOperationStatus.GlobalUnavailable);
		}
		catch (Exception)
		{
			return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupIndeterminate,
				LuaOperationStatus.InvalidResult);
		}

		if (status.IsSuccess)
		{
			return identity == LuaRuntime.CurrentStateIdentity
				? new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.Released, status)
				: new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.StaleRuntime, status);
		}

		return status.Kind is LuaOperationStatusKind.GlobalUnavailable or LuaOperationStatusKind.StackUnavailable
			? new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupUnavailable, status)
			: new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupIndeterminate, status);
	}

	private static void SupersedeCurrentLease(SymbolName name)
	{
		if (!SOwnedRegistrations.Remove(name, out SymbolRegistrationLease? existing))
		{
			return;
		}

		existing.MarkTerminal(SymbolRegistrationReleaseKind.Superseded);
	}

	private static void RemoveCurrentLease(SymbolRegistrationLease lease)
	{
		if (SOwnedRegistrations.TryGetValue(lease.Name, out SymbolRegistrationLease? current) &&
		    ReferenceEquals(current, lease))
		{
			SOwnedRegistrations.Remove(lease.Name);
		}
	}

	private static bool OperationCouldHaveStarted(LuaOperationStatus status)
	{
		return status.Kind is not (LuaOperationStatusKind.GlobalUnavailable or LuaOperationStatusKind.StackUnavailable);
	}

	/// <summary>The result of the pre-unregister name lookup.</summary>
	[StructLayout(LayoutKind.Auto)]
	private readonly record struct ReplacementCheck(SymbolRegistrationReleaseKind Kind, LuaOperationStatus? Status);
}
