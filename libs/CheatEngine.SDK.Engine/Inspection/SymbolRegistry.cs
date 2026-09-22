using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
///         unqualified optional arguments or a managed name-selection policy.
///     </para>
///     <para>
///         <b>Lifetime and ownership.</b> Lookup returns a newly allocated managed string and owns no Lua reference or
///         CE object. Registering a symbol mutates CE's host-wide symbol table; it does not yield an independently owned
///         CE resource, and this class intentionally does not claim exclusive ownership of a name.
///         <see cref="TryRegisterOwned" /> adds only a same-SDK cleanup coordinator: CE's name-only unregister cannot
///         prove that an external script or plugin has replaced a registration.
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
	/// <param name="options">The persistence option for the registration.</param>
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

	/// <summary>Registers a symbol and returns an explicit, coordinator-qualified cleanup lease on success.</summary>
	/// <param name="name">The registration name CE will add to its symbol handler.</param>
	/// <param name="address">The target-process address associated with <paramref name="name" />.</param>
	/// <param name="options">The persistence option for the registration.</param>
	/// <returns>The protected registration status and a lease only on successful registration.</returns>
	/// <exception cref="ArgumentException"><paramref name="name" /> is default or otherwise invalid.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     The lease prevents older leases from unregistering a newer registration made through this coordinator. CE has
	///     no registration token, so this method makes no claim about replacements made outside that coordinator.
	/// </remarks>
	[RequiresPluginEnabled]
	public static SymbolRegistrationAcquireOutcome TryRegisterOwned(SymbolName name, Address address,
		SymbolRegistrationOptions options = default)
	{
		ValidateName(name);
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
			SymbolRegistrationLease lease = new(name, options, identity);
			SOwnedRegistrations.Add(name, lease);
			return new SymbolRegistrationAcquireOutcome(status, lease);
		}
	}

	[LuaGlobal("registerSymbol")]
	private static partial LuaOperationStatus RegisterCore([LuaMarshaller(typeof(SymbolName))] SymbolName name,
		[LuaMarshaller(typeof(Address))] Address address, bool doNotSave);

	[LuaGlobal("unregisterSymbol")]
	private static partial LuaOperationStatus UnregisterCore([LuaMarshaller(typeof(SymbolName))] SymbolName name);

	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification = "ReleaseOwned is the single atomic lease-cleanup transaction and must preserve its state ordering.")]
	internal static SymbolRegistrationReleaseOutcome ReleaseOwned(SymbolRegistrationLease lease)
	{
		lock (SOwnedRegistrationGate)
		{
			if (lease.ObserveTerminalKind() is { } terminalKind)
			{
				return new SymbolRegistrationReleaseOutcome(terminalKind, LuaOperationStatus.Success);
			}

			if (!LuaRuntime.IsAttached || lease.Identity != LuaRuntime.CurrentStateIdentity)
			{
				RemoveCurrentLease(lease);
				lease.MarkTerminalAndObserve(SymbolRegistrationReleaseKind.StaleRuntime);
				return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.StaleRuntime,
					LuaOperationStatus.Success);
			}

			if (!SOwnedRegistrations.TryGetValue(lease.Name, out SymbolRegistrationLease? current) ||
			    !ReferenceEquals(current, lease))
			{
				lease.MarkTerminalAndObserve(SymbolRegistrationReleaseKind.Superseded);
				return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.Superseded,
					LuaOperationStatus.Success);
			}

			LuaOperationStatus status;
			try
			{
				status = UnregisterCore(lease.Name);
			}
			catch (InvalidOperationException) when (!LuaRuntime.IsAttached ||
			                                        lease.Identity != LuaRuntime.CurrentStateIdentity)
			{
				RemoveCurrentLease(lease);
				lease.MarkTerminalAndObserve(SymbolRegistrationReleaseKind.StaleRuntime);
				return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.StaleRuntime,
					LuaOperationStatus.Success);
			}
			catch (InvalidOperationException)
			{
				return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.CleanupUnavailable,
					LuaOperationStatus.GlobalUnavailable);
			}

			if (status.IsSuccess)
			{
				RemoveCurrentLease(lease);
				if (lease.Identity != LuaRuntime.CurrentStateIdentity)
				{
					lease.MarkTerminalAndObserve(SymbolRegistrationReleaseKind.StaleRuntime);
					return new SymbolRegistrationReleaseOutcome(SymbolRegistrationReleaseKind.StaleRuntime, status);
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
	}

	private static void ValidateName(SymbolName name)
	{
		if (string.IsNullOrWhiteSpace(name.Value))
		{
			throw new ArgumentException("A symbol name must not be default, empty or white space.", nameof(name));
		}
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
}
