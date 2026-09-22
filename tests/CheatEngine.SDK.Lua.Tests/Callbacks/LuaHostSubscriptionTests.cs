using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Callbacks;

/// <summary>
///     Deterministic ownership tests for the internal host-subscription lifecycle primitive. These tests intentionally
///     use local delegates, not a claimed CE timer or hotkey mapping: those host paths remain live-unqualified.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class LuaHostSubscriptionTests
{
	[Fact]
	public void Registration_failure_leaves_the_callback_inert_and_does_not_unregister()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		Action? hostCallback = null;
		int calls = 0;
		int unregisters = 0;

		bool registered = LuaHostSubscription.TryRegister(L, () => calls++,
			(registrationState, callback) =>
			{
				hostCallback = callback;
				return null;
			}, out LuaHostSubscription? subscription);

		Assert.False(registered);
		Assert.Null(subscription);
		Assert.NotNull(hostCallback);
		hostCallback!();
		Assert.Equal(0, calls);
		Assert.Equal(0, unregisters);
		Assert.Equal(0, LuaHostSubscriptionRegistry.Count);
	}

	[Fact]
	public void Registrar_exception_leaves_its_captured_callback_inert()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		Action? hostCallback = null;
		int calls = 0;

		Assert.Throws<InvalidOperationException>(() => LuaHostSubscription.TryRegister(L, () => calls++,
			(registrationState, callback) =>
			{
				hostCallback = callback;
				throw new InvalidOperationException("registration failure");
			}, out _));

		hostCallback!();
		Assert.Equal(0, calls);
		Assert.Equal(0, LuaHostSubscriptionRegistry.Count);
	}

	[Fact]
	public void Detach_unregisters_lifo_once_and_late_callbacks_are_inert()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		List<string> releases = [];
		Action? firstCallback = null;
		Action? lastCallback = null;

		Assert.True(LuaHostSubscription.TryRegister(L, static () =>
			{
			},
			(registrationState, callback) =>
			{
				firstCallback = callback;
				return releaseState => releases.Add("timer-a");
			}, out LuaHostSubscription? first));
		Assert.True(LuaHostSubscription.TryRegister(L, static () =>
			{
			},
			(registrationState, callback) => releaseState => releases.Add("hotkey-b"),
			out LuaHostSubscription? second));
		Assert.True(LuaHostSubscription.TryRegister(L, static () =>
			{
			},
			(registrationState, callback) =>
			{
				lastCallback = callback;
				return releaseState => releases.Add("timer-c");
			}, out LuaHostSubscription? third));

		Assert.NotNull(first);
		Assert.NotNull(second);
		Assert.NotNull(third);
		Assert.Equal(3, LuaHostSubscriptionRegistry.Count);

		LuaRuntime.Detach();

		Assert.Equal(["timer-c", "hotkey-b", "timer-a"], releases);
		Assert.True(first.IsDisposed);
		Assert.True(second.IsDisposed);
		Assert.True(third.IsDisposed);
		Assert.Equal(0, LuaHostSubscriptionRegistry.Count);

		firstCallback!();
		lastCallback!();
		first.Dispose();
		second.Dispose();
		third.Dispose();
		Assert.Equal(["timer-c", "hotkey-b", "timer-a"], releases);
	}

	[Fact]
	public void Reset_and_reenable_leave_the_old_subscription_callback_inert()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		Action? hostCallback = null;
		int calls = 0;
		int unregisters = 0;

		Assert.True(LuaHostSubscription.TryRegister(L, () => calls++,
			(registrationState, callback) =>
			{
				hostCallback = callback;
				return releaseState => unregisters++;
			}, out LuaHostSubscription? subscription));
		Assert.NotNull(subscription);
		LuaStateIdentity oldIdentity = subscription.Identity;

		using (LuaRuntime.BeginStateReset())
		{
		}

		Assert.True(subscription.IsDisposed);
		Assert.Equal(1, unregisters);
		Assert.NotEqual(oldIdentity, LuaRuntime.CurrentStateIdentity);
		hostCallback!();
		Assert.Equal(0, calls);

		LuaRuntime.Attach(scope.Binding);
		hostCallback();
		Assert.Equal(0, calls);
		Assert.Equal(1, unregisters);
	}

	[Fact]
	public void Reset_reopens_registration_admission_for_new_host_subscriptions()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		int oldUnregisters = 0;
		int newUnregisters = 0;

		Assert.True(LuaHostSubscription.TryRegister(L, static () =>
			{
			},
			(registrationState, callback) => releaseState => oldUnregisters++,
			out LuaHostSubscription? oldSubscription));
		Assert.NotNull(oldSubscription);

		using (LuaRuntime.BeginStateReset())
		{
		}

		Assert.True(oldSubscription.IsDisposed);
		Assert.Equal(1, oldUnregisters);

		Assert.True(LuaHostSubscription.TryRegister(L, static () =>
			{
			},
			(registrationState, callback) => releaseState => newUnregisters++,
			out LuaHostSubscription? newSubscription));
		Assert.NotNull(newSubscription);
		Assert.Equal(1, LuaHostSubscriptionRegistry.Count);

		newSubscription.Dispose();
		Assert.True(newSubscription.IsDisposed);
		Assert.Equal(1, newUnregisters);
		Assert.Equal(0, LuaHostSubscriptionRegistry.Count);
	}

	[Fact]
	public async Task Disable_admission_waits_for_an_entered_callback_and_rejects_a_late_one()
	{
		LuaTest.RequireNativeLua();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		using ManualResetEventSlim entered = new(false);
		using ManualResetEventSlim allowReturn = new(false);
		using ManualResetEventSlim admissionClosed = new(false);
		Action? hostCallback = null;
		int calls = 0;
		int unregisters = 0;

		Assert.True(LuaHostSubscription.TryRegister(L, () =>
		{
			calls++;
			entered.Set();
			if (!allowReturn.Wait(TimeSpan.FromSeconds(5), cancellationToken))
			{
				throw new TimeoutException("The subscription callback barrier timed out.");
			}
		}, (registrationState, callback) =>
		{
			hostCallback = callback;
			return releaseState => unregisters++;
		}, out LuaHostSubscription? subscription));
		Assert.NotNull(subscription);
		LuaHostSubscriptionRegistry.CallbackAdmissionClosedForTesting = admissionClosed.Set;

		try
		{
			Task enteredCall = Task.Factory.StartNew(hostCallback!, cancellationToken, TaskCreationOptions.LongRunning,
				TaskScheduler.Default);
			Assert.True(entered.Wait(TimeSpan.FromSeconds(5), cancellationToken),
				"The host callback did not enter its handler.");

			Task close = Task.Factory.StartNew(LuaRuntime.CloseHostSubscriptionAdmissionAndDrain, cancellationToken,
				TaskCreationOptions.LongRunning, TaskScheduler.Default);
			Assert.True(admissionClosed.Wait(TimeSpan.FromSeconds(5), cancellationToken),
				"Subscription admission did not close.");
			Assert.False(close.IsCompleted, "Admission close completed while a callback was still admitted.");

			hostCallback!();
			Assert.Equal(1, calls);

			allowReturn.Set();
			await enteredCall.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
			await close.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

			LuaRuntime.Detach();
			Assert.Equal(1, unregisters);
			Assert.True(subscription.IsDisposed);
		}
		finally
		{
			LuaHostSubscriptionRegistry.CallbackAdmissionClosedForTesting = null;
			allowReturn.Set();
		}
	}

	[Fact]
	public async Task Disable_drain_does_not_hold_the_registry_gate_against_an_admitted_callback()
	{
		LuaTest.RequireNativeLua();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		using DisableDrainRace race = new(L, cancellationToken);
		Assert.True(race.Register());
		Assert.NotNull(race.FirstSubscription);
		Assert.NotNull(race.SecondSubscription);
		LuaHostSubscription.CallbackDrainStartedForTesting = race.CallbackDrainStarted.Set;

		try
		{
			Task enteredCall = Task.Factory.StartNew(race.HostCallback!, cancellationToken,
				TaskCreationOptions.LongRunning,
				TaskScheduler.Default);
			Assert.True(race.Entered.Wait(TimeSpan.FromSeconds(5), cancellationToken),
				"The host callback did not enter its handler.");

			Task close = Task.Factory.StartNew(LuaRuntime.CloseHostSubscriptionAdmissionAndDrain, cancellationToken,
				TaskCreationOptions.LongRunning, TaskScheduler.Default);
			Assert.True(race.CallbackDrainStarted.Wait(TimeSpan.FromSeconds(5), cancellationToken),
				"The subscription drain did not begin.");

			race.AllowCallbackActions.Set();
			Assert.True(race.CallbackActionsCompleted.Wait(TimeSpan.FromSeconds(5), cancellationToken),
				"The admitted callback was blocked while registering and disposing during the drain.");
			await enteredCall.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
			await close.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

			Assert.Null(race.CallbackFailure);
			Assert.Equal(1, race.RejectedRegistrationUnregisters);
			Assert.True(race.SecondSubscription.IsDisposed);
			Assert.Equal(1, race.SecondUnregisters);
			Assert.False(race.FirstSubscription.IsDisposed);

			LuaRuntime.Detach();
			Assert.True(race.FirstSubscription.IsDisposed);
		}
		finally
		{
			LuaHostSubscription.CallbackDrainStartedForTesting = null;
			race.AllowCallbackActions.Set();
		}
	}

	[Fact]
	public void Callback_and_unregister_failures_are_contained_and_unregister_is_single_use()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		Action? hostCallback = null;
		int unregisters = 0;

		Assert.True(LuaHostSubscription.TryRegister(L,
			static () => throw new InvalidOperationException("handler failure"),
			(registrationState, callback) =>
			{
				hostCallback = callback;
				return releaseState =>
				{
					unregisters++;
					throw new InvalidOperationException("unregister failure");
				};
			}, out LuaHostSubscription? subscription));
		Assert.NotNull(subscription);

		hostCallback!();
		Assert.IsType<InvalidOperationException>(subscription.LastCallbackException);

		subscription.Dispose();
		subscription.Dispose();
		Assert.Equal(1, unregisters);
		Assert.IsType<InvalidOperationException>(subscription.LastUnregisterException);
		Assert.True(subscription.IsDisposed);
		Assert.Equal(0, LuaHostSubscriptionRegistry.Count);
	}

	[Fact]
	public void A_callback_cannot_dispose_its_own_host_registration()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaHostSubscription? subscription = null;
		Action? hostCallback = null;
		int unregisters = 0;

		Assert.True(LuaHostSubscription.TryRegister(L, () => subscription!.Dispose(),
			(registrationState, callback) =>
			{
				hostCallback = callback;
				return releaseState => unregisters++;
			}, out subscription));
		Assert.NotNull(subscription);

		hostCallback!();

		Assert.IsType<InvalidOperationException>(subscription.LastCallbackException);
		Assert.False(subscription.IsDisposed);
		Assert.Equal(0, unregisters);

		subscription.Dispose();
		Assert.True(subscription.IsDisposed);
		Assert.Equal(1, unregisters);
	}

	private sealed class DisableDrainRace(LuaState state, CancellationToken cancellationToken) : IDisposable
	{
		public ManualResetEventSlim Entered
		{
			get;
		} = new(false);

		public ManualResetEventSlim AllowCallbackActions
		{
			get;
		} = new(false);

		public ManualResetEventSlim CallbackDrainStarted
		{
			get;
		} = new(false);

		public ManualResetEventSlim CallbackActionsCompleted
		{
			get;
		} = new(false);

		public Action? HostCallback
		{
			get;
			private set;
		}

		public LuaHostSubscription? FirstSubscription
		{
			get;
			private set;
		}

		public LuaHostSubscription? SecondSubscription
		{
			get;
			private set;
		}

		public Exception? CallbackFailure
		{
			get;
			private set;
		}

		public int RejectedRegistrationUnregisters
		{
			get;
			private set;
		}

		public int SecondUnregisters
		{
			get;
			private set;
		}

		public void Dispose()
		{
			Entered.Dispose();
			AllowCallbackActions.Dispose();
			CallbackDrainStarted.Dispose();
			CallbackActionsCompleted.Dispose();
		}

		public bool Register()
		{
			if (!LuaHostSubscription.TryRegister(state, RunAdmittedCallback,
				    (registrationState, callback) =>
				    {
					    HostCallback = callback;
					    return static _ =>
					    {
					    };
				    }, out LuaHostSubscription? firstSubscription))
			{
				return false;
			}

			FirstSubscription = firstSubscription;
			return LuaHostSubscription.TryRegister(state, static () =>
				       {
				       },
				       (registrationState, callback) => releaseState => SecondUnregisters++,
				       out LuaHostSubscription? secondSubscription)
			       && (SecondSubscription = secondSubscription) is not null;
		}

		private void RunAdmittedCallback()
		{
			Entered.Set();
			try
			{
				if (!AllowCallbackActions.Wait(TimeSpan.FromSeconds(5), cancellationToken))
				{
					throw new TimeoutException("The admitted callback action barrier timed out.");
				}

				bool registered = LuaHostSubscription.TryRegister(state, static () =>
					{
					},
					(registrationState, callback) => releaseState => RejectedRegistrationUnregisters++,
					out LuaHostSubscription? rejectedSubscription);
				if (registered || rejectedSubscription is not null)
				{
					throw new InvalidOperationException("Registration unexpectedly succeeded after admission closed.");
				}

				SecondSubscription!.Dispose();
			}
			catch (Exception exception)
			{
				CallbackFailure = exception;
			}
			finally
			{
				CallbackActionsCompleted.Set();
			}
		}
	}
}
