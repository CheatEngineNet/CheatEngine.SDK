using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace CheatEngine.SDK.Hosting.Threading;

/// <summary>
///     One unit of work handed to the main thread by <see cref="MainThread" />: runs on the main thread inside the
///     dispatch thunk, captures the outcome, and rethrows it on the calling thread afterwards. The object travels
///     through Lua as a <c>GCHandle&lt;object&gt;</c> in the closure's upvalue.
/// </summary>
/// <remarks>
///     The outcome is written on the main thread and read on the calling thread once the host's <c>synchronize</c> has
///     returned; the host's own wait provides the ordering, and the volatile accesses make the hand-off independent of it.
/// </remarks>
internal abstract class MainThreadWorkItem
{
	private int _claimed;
	private Exception? _failure;
	private int _hasRun;

	/// <summary>Gets a value indicating whether the work ran (successfully or not).</summary>
	internal bool HasRun => Volatile.Read(ref _hasRun) != 0;

	/// <summary>Runs the work, capturing any exception. Called on the main thread; never throws.</summary>
	internal void Execute()
	{
		if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0)
		{
			return;
		}

		try
		{
			Run();
		}
		catch (Exception exception)
		{
			_failure = exception;
		}
		finally
		{
			Volatile.Write(ref _hasRun, 1);
		}
	}

	/// <summary>Completes the item without running it because the host violated the dispatch thread contract.</summary>
	internal void Reject(Exception failure)
	{
		ArgumentNullException.ThrowIfNull(failure);
		if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0)
		{
			return;
		}

		_failure = failure;
		Volatile.Write(ref _hasRun, 1);
	}

	/// <summary>Rethrows, with its original stack trace, the exception the work raised on the main thread.</summary>
	/// <exception cref="InvalidOperationException">The work never ran: the host did not invoke the function it was given.</exception>
	internal void ThrowIfFailed()
	{
		if (!HasRun)
		{
			throw new InvalidOperationException(
				"The host's synchronize function returned without running the dispatched work.");
		}

		Exception? failure = _failure;
		if (failure is not null)
		{
			ExceptionDispatchInfo.Throw(failure);
		}
	}

	/// <summary>The work itself.</summary>
	protected abstract void Run();
}
