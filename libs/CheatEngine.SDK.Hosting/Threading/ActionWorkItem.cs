using System;

namespace CheatEngine.SDK.Hosting.Threading;

/// <summary>A <see cref="MainThreadWorkItem" /> over an <see cref="Action{T}" /> and its explicit state.</summary>
/// <typeparam name="TState">
///     The state the action receives; passing it explicitly keeps the lambda
///     <see langword="static" />.
/// </typeparam>
internal sealed class ActionWorkItem<TState> : MainThreadWorkItem
{
    private readonly Action<TState> _action;
    private readonly TState _state;

    internal ActionWorkItem(Action<TState> action, TState state)
    {
        _action = action;
        _state = state;
    }

    protected override void Run()
    {
        _action(_state);
    }
}
