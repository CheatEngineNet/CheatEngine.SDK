using System;

namespace CESDK.Hosting.Threading;

/// <summary>A <see cref="MainThreadWorkItem" /> over a <see cref="Func{T,TResult}" />, its explicit state and its result.</summary>
/// <typeparam name="TState">The state the function receives.</typeparam>
/// <typeparam name="TResult">The result it produces.</typeparam>
internal sealed class FuncWorkItem<TState, TResult> : MainThreadWorkItem
{
    private readonly Func<TState, TResult> _function;
    private readonly TState _state;

    internal FuncWorkItem(Func<TState, TResult> function, TState state)
    {
        _function = function;
        _state = state;
    }

    /// <summary>Gets the result; <see langword="default" /> until the function ran successfully.</summary>
    internal TResult? Result { get; private set; }

    protected override void Run()
    {
        Result = _function(_state);
    }
}
