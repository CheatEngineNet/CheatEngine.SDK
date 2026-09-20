using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Threading;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;

namespace CheatEngine.SDK.Hosting.Threading;

/// <summary>
///     Cheat Engine's main (GUI) thread as seen by a plugin: identity, the two message-loop operations of the exports
///     record, and synchronous dispatch of work to that thread.
/// </summary>
/// <remarks>
///     <para>
///         <b>Identity.</b> The thread that ran the enable callback is the main thread of that enable (captured in
///         <see cref="PluginContext.MainThreadId" />). <see cref="IsMainThread" /> is a volatile read and a comparison,
///         taken
///         before any lock on every path here: the dispatcher short-circuits on the main thread without locking.
///     </para>
///     <para>
///         <b>Dispatch.</b> <see cref="Invoke{TState}" /> runs the work inline when called on the main thread and
///         otherwise hands it to Cheat Engine's Lua <c>synchronize</c> global, which must run it on the captured main
///         thread and return when it has completed; an exception thrown by the work is rethrown on the caller with its
///         original stack trace. The dispatch thunk rejects a host that invokes it on any other managed thread.
///         <b>
///             The
///             actual Cheat Engine 7.7 hop remains unverified live
///         </b>
///         ; unit tests deliberately prove that an inline stand-in
///         is rejected. Deadlock rule, the
///         host's: a main thread that blocks on a worker which itself calls <see cref="Invoke{TState}" /> deadlocks unless
///         the main thread pumps queued calls with <see cref="CheckSynchronize" /> while it waits. A fire-and-forget form
///         (<c>queue</c>) and a <see cref="System.Threading.SynchronizationContext" /> are not offered until the
///         synchronous
///         path is confirmed live.
///     </para>
///     <para>
///         <b>Preconditions.</b> Everything here needs the plugin to be enabled (<see cref="InvalidOperationException" />
///         otherwise). <see cref="ProcessMessages" /> and <see cref="CheckSynchronize" /> additionally require the main
///         thread and throw when called from another one rather than pumping a foreign thread's queue.
///     </para>
/// </remarks>
public static unsafe class MainThread
{
    /// <summary>
    ///     Gets a value indicating whether the calling thread is the main thread of the current enable.
    ///     <see langword="false" /> while the plugin is disabled.
    /// </summary>
    public static bool IsMainThread
    {
        get
        {
            var context = PluginHost.Context;
            return context is not null && context.IsMainThread;
        }
    }

    /// <summary>
    ///     Pumps Cheat Engine's pending window messages (the host's <c>ProcessMessages</c> export), so that a long
    ///     operation on the main thread keeps the GUI responsive. Re-entrant: message handlers run inside the call.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     The plugin is not enabled, the calling thread is not the main thread, or
    ///     the host supplied no <c>ProcessMessages</c> function.
    /// </exception>
    [RequiresPluginEnabled]
    [MainThreadOnly]
    public static void ProcessMessages()
    {
        var context = RequireMainThreadContext();
        var pump = context.Exports.ProcessMessages;
        if (pump is null) ThrowMissingSlot("ProcessMessages");

        pump();
    }

    /// <summary>
    ///     Runs the calls other threads queued for the main thread (the host's <c>CheckSynchronize</c> export), waiting
    ///     up to <paramref name="timeoutMilliseconds" /> for one to arrive. For a main thread that blocks on a worker
    ///     which synchronizes with the GUI.
    /// </summary>
    /// <param name="timeoutMilliseconds">How long to wait for a queued call, in milliseconds; 0 returns at once.</param>
    /// <returns><see langword="true" /> when at least one queued call was executed (inferred from the public 7.5 host source).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeoutMilliseconds" /> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The plugin is not enabled, the calling thread is not the main thread, or
    ///     the host supplied no <c>CheckSynchronize</c> function.
    /// </exception>
    [RequiresPluginEnabled]
    [MainThreadOnly]
    public static bool CheckSynchronize(int timeoutMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeoutMilliseconds);
        var context = RequireMainThreadContext();
        var check = context.Exports.CheckSynchronize;
        if (check is null) ThrowMissingSlot("CheckSynchronize");

        return check(timeoutMilliseconds).IsTrue;
    }

    /// <summary>
    ///     Runs <paramref name="action" /> on the main thread and returns when it has completed: inline when already
    ///     there, through the host's <c>synchronize</c> otherwise.
    /// </summary>
    /// <typeparam name="TState">
    ///     The state passed to the action; pass what the action needs so that it can be a <see langword="static" />
    ///     lambda.
    /// </typeparam>
    /// <param name="action">The work.</param>
    /// <param name="state">Its argument.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The plugin is not enabled, the host's <c>synchronize</c> is unavailable or failed, or the host omitted
    ///     <c>CheckSynchronize</c> so Hosting cannot guarantee a shutdown drain for worker work.
    /// </exception>
    /// <remarks>
    ///     An exception thrown by <paramref name="action" /> on the main thread is rethrown here with its original stack
    ///     trace. See the type remarks for the deadlock rule. The cross-thread path (a call from a thread other than the
    ///     main one) is unverified in a live Cheat Engine: only its mechanics are tested, against a Lua stand-in.
    /// </remarks>
    [RequiresPluginEnabled]
    public static void Invoke<TState>(Action<TState> action, TState state)
    {
        ArgumentNullException.ThrowIfNull(action);
        var context = PluginHost.RequireContext();
        if (context.IsMainThread)
        {
            action(state);
            return;
        }

        if (!context.HasCheckSynchronize)
            throw new InvalidOperationException(
                "The host's exports record has no CheckSynchronize function; cross-thread dispatch cannot guarantee shutdown drain.");

        ActionWorkItem<TState> item = new(action, state);
        using var admission = PluginHost.AdmitMainThreadWork(context);
        MainThreadDispatcher.Dispatch(item);
        item.ThrowIfFailed();
    }

    /// <summary>
    ///     Runs <paramref name="function" /> on the main thread and returns its result; see <see cref="Invoke{TState}" />
    ///     .
    /// </summary>
    /// <typeparam name="TState">The state passed to the function.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="function">The work.</param>
    /// <param name="state">Its argument.</param>
    /// <returns>What <paramref name="function" /> returned on the main thread.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="function" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The plugin is not enabled, the host's <c>synchronize</c> is unavailable or failed, or the host omitted
    ///     <c>CheckSynchronize</c> so Hosting cannot guarantee a shutdown drain for worker work.
    /// </exception>
    /// <remarks>
    ///     Same caveat as <see cref="Invoke{TState}" />: the cross-thread path through <c>synchronize</c> is unverified
    ///     in a live Cheat Engine.
    /// </remarks>
    [RequiresPluginEnabled]
    public static TResult Invoke<TState, TResult>(Func<TState, TResult> function, TState state)
    {
        ArgumentNullException.ThrowIfNull(function);
        var context = PluginHost.RequireContext();
        if (context.IsMainThread) return function(state);

        if (!context.HasCheckSynchronize)
            throw new InvalidOperationException(
                "The host's exports record has no CheckSynchronize function; cross-thread dispatch cannot guarantee shutdown drain.");

        FuncWorkItem<TState, TResult> item = new(function, state);
        using var admission = PluginHost.AdmitMainThreadWork(context);
        MainThreadDispatcher.Dispatch(item);
        item.ThrowIfFailed();
        return item.Result!;
    }

    private static PluginContext RequireMainThreadContext()
    {
        var context = PluginHost.RequireContext();
        if (!context.IsMainThread) ThrowNotMainThread();

        return context;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNotMainThread()
    {
        throw new InvalidOperationException(
            "This operation pumps Cheat Engine's main thread and must be called from it; use MainThread.Invoke to get there.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMissingSlot(string slot)
    {
        throw new InvalidOperationException("The host's exports record has no " + slot + " function.");
    }
}
