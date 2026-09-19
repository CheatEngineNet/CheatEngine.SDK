using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using CESDK.Lua.Calls;
using CESDK.Lua.State;

namespace CESDK.Lua.CompilerServices;

/// <summary>
///     Generator-facing: the cold exits of a generated call body. A generated <c>Try*</c> method keeps its success path
///     straight (record top, push, call, read, restore) and jumps here on every failure, so that the failure handling is
///     one call and the hot method stays small. Not meant to be called by hand.
/// </summary>
/// <remarks>
///     A body has three exits besides success: the bound global could not be resolved, the protected call failed, and
///     the call succeeded but a result is not of the expected kind (Cheat Engine's <c>nil</c> for "failed", or a wrong
///     type). A <c>Try*</c> wrapper uses <see cref="Fail(LuaState, int)" /> / <see cref="Fail{TResult}" /> for all three;
///     a throwing wrapper uses <see cref="ThrowUnresolvedGlobal" />, <see cref="Throw" /> and
///     <see cref="ThrowUnexpectedResult" />.
///     The result type of <see cref="Fail{TResult}" /> deliberately does not allow <see langword="ref" />
///     <see langword="struct" />s:
///     a body restores the stack before it returns, and a <c>ReadOnlySpan&lt;byte&gt;</c> read from a popped Lua string
///     would point at memory Lua may already have freed. String results are copied out (
///     <see cref="LuaState.TryCopyUtf8" />)
///     or read as <see cref="string" />.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class LuaCallSupport
{
    /// <summary>
    ///     Restores the stack to <paramref name="top" /> (discarding an error value or a partial result) and returns
    ///     <see langword="false" />.
    /// </summary>
    /// <param name="state">The state the body ran on.</param>
    /// <param name="top">The top recorded at the start of the body.</param>
    /// <returns><see langword="false" />, always.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Fail(LuaState state, int top)
    {
        state.SetTop(top);
        return false;
    }

    /// <summary>
    ///     Restores the stack to <paramref name="top" />, defaults the <see langword="out" /> result and returns
    ///     <see langword="false" />.
    /// </summary>
    /// <typeparam name="TResult">
    ///     The result type of the generated method: a value, a <see cref="string" />, or the
    ///     <c>written</c> count of a copy-out result; never a span into Lua's memory.
    /// </typeparam>
    /// <param name="state">The state the body ran on.</param>
    /// <param name="top">The top recorded at the start of the body.</param>
    /// <param name="result">Set to <see langword="default" />.</param>
    /// <returns><see langword="false" />, always.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Fail<TResult>(LuaState state, int top, out TResult result)
    {
        state.SetTop(top);
        result = default!;
        return false;
    }

    /// <summary>
    ///     Restores the stack to <paramref name="top" /> and throws a <see cref="LuaException" /> describing the error value
    ///     that was on top of the stack: the exit of a generated throwing wrapper when the protected call failed.
    /// </summary>
    /// <param name="state">The state the body ran on.</param>
    /// <param name="top">The top recorded at the start of the body.</param>
    /// <param name="status">The failure status.</param>
    /// <exception cref="LuaException">Always.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    public static void Throw(LuaState state, int top, LuaStatus status)
    {
        var error = LuaError.FromStack(state, status);
        state.SetTop(top);
        LuaException.Throw(error);
    }

    /// <summary>
    ///     Restores the stack to <paramref name="top" /> and throws because a bound global could not be resolved (undefined
    ///     or not a function): the exit of a generated throwing wrapper when <see cref="LuaGlobalFunctions.TryPush" /> fails.
    /// </summary>
    /// <param name="state">The state the body ran on.</param>
    /// <param name="top">The top recorded at the start of the body.</param>
    /// <param name="globalName">The name the body tried to bind.</param>
    /// <exception cref="LuaException">Always.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    public static void ThrowUnresolvedGlobal(LuaState state, int top, string globalName)
    {
        state.SetTop(top);
        throw new LuaException("The Lua global '" + globalName + "' is undefined or is not a function.");
    }

    /// <summary>
    ///     Names the Lua type of the value at <paramref name="index" />, restores the stack to <paramref name="top" /> and
    ///     throws: the exit of a generated throwing wrapper when the call succeeded but a result could not be read as
    ///     the declared type (<c>The Lua global 'readInteger' returned a nil value, not an integer.</c>).
    /// </summary>
    /// <param name="state">The state the body ran on.</param>
    /// <param name="top">The top recorded at the start of the body.</param>
    /// <param name="index">The index of the offending result, still on the stack (<c>-1</c> for a single result).</param>
    /// <param name="globalName">The name of the bound global.</param>
    /// <param name="expected">What the wrapper expected, with its article: <c>"an integer"</c>, <c>"a string"</c>.</param>
    /// <exception cref="LuaException">Always.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    public static void ThrowUnexpectedResult(LuaState state, int top, int index, string globalName, string expected)
    {
        var typeName = Encoding.UTF8.GetString(state.TypeName(index));
        state.SetTop(top);
        throw new LuaException("The Lua global '" + globalName + "' returned a " + typeName + " value, not " +
                               expected + ".");
    }
}
