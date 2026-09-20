using System;
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Threading;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     A stateful, main-thread-only owner of one explicitly owned <see cref="MemScan" /> and its explicitly owned child
///     <see cref="FoundList" />. It serializes the CE sequence needed to scan and read results safely.
/// </summary>
/// <remarks>
///     <para>
///         The session is intentionally adopted, not created from CE globals. CE 7.7.0.10621 <c>celua.txt</c> says that
///         <c>createMemScan</c> and <c>createFoundList</c> return objects, but does not establish their destroy owner.
///         A caller may call <see cref="Adopt" /> only after a separately sourced binding has already established that it
///         owns both objects. This keeps a borrowed handle distinct from an owned resource instead of inferring ownership
///         from a Lua return value.
///     </para>
///     <para>
///         A session accepts <c>firstScan</c> only from <see cref="MemoryScanState.New" />, <c>nextScan</c> only from
///         <see cref="MemoryScanState.ResultsReady" />, and reads only after <c>waitTillDone</c> completed successfully
///         followed
///         by <c>FoundList.initialize</c>. It uses <c>FoundList.deinitialize</c> before its own next scan and reset to
///         release the readable view; this is a session invariant, not a claim that CE rejects every other raw sequence.
///     </para>
///     <para>
///         Dispose is idempotent and releases a ready found list before destroying the found-list child, then destroys the
///         scanner parent. It must run while the plugin is enabled and on the Cheat Engine main thread so the contained
///         <see cref="Owned{T}" /> values can reach <c>destroy()</c>. As with <see cref="Owned{T}" />, no finalizer runs
///         native cleanup from an arbitrary thread.
///     </para>
/// </remarks>
public sealed class MemoryScanSession : IDisposable
{
    // These are public exception identifiers. Keep CE's exact member names at the private call sites instead.
    private const string FirstScanOperation = "MemoryScan.FirstScan";
    private const string NextScanOperation = "MemoryScan.NextScan";
    private const string WaitForCompletionOperation = "MemoryScan.WaitForCompletion";
    private const string InitializeResultsOperation = "MemoryScan.InitializeResults";
    private const string DeinitializeResultsOperation = "MemoryScan.DeinitializeResults";
    private const string ResetOperation = "MemoryScan.Reset";
    private const string ResultCountOperation = "MemoryScan.ResultCount";
    private const string ResultAddressOperation = "MemoryScan.ResultAddress";
    private const string ResultValueOperation = "MemoryScan.ResultValue";
    private Owned<FoundList>? _foundList;
    private Owned<MemScan>? _scanner;

    private MemoryScanSession(Owned<MemScan> scanner, Owned<FoundList> foundList)
    {
        _scanner = scanner;
        _foundList = foundList;
        State = MemoryScanState.New;
    }

    /// <summary>Gets the session's conservative, managed state.</summary>
    public MemoryScanState State { get; private set; }

    /// <summary>
    ///     Gets the scanner as a borrowed handle. Direct raw operations on this value bypass the session's state checks;
    ///     prefer the session members for the scan lifecycle.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The session was disposed.</exception>
    public MemScan Scanner
    {
        get
        {
            ThrowIfDisposed();
            return _scanner!.Value;
        }
    }

    /// <summary>
    ///     Gets the attached found list as a borrowed handle, only after it is initialized for reading. Direct raw
    ///     operations on the returned value bypass the session's state checks.
    /// </summary>
    /// <exception cref="MemoryScanStateException">The results are not ready.</exception>
    /// <exception cref="ObjectDisposedException">The session was disposed.</exception>
    public FoundList Results
    {
        get
        {
            RequireState("Results", MemoryScanState.ResultsReady);
            return _foundList!.Value;
        }
    }

    /// <summary>Gets the number of readable results in the initialized found list.</summary>
    /// <remarks>
    ///     Cheat Engine stores the count as <c>UInt64</c>. The Lua boundary supplies a signed 64-bit integer, so this
    ///     property exposes its non-negative range as <see cref="ulong" /> and rejects an unrepresentable host result.
    ///     Result-reading methods intentionally retain CE's <see cref="int" /> index parameter and therefore address
    ///     only indices from zero through <c>Int32.MaxValue</c>.
    /// </remarks>
    /// <exception cref="MemoryScanStateException">The results are not ready.</exception>
    /// <exception cref="MemoryScanException">CE did not return a valid non-negative integer count.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
    [MainThreadOnly]
    [RequiresPluginEnabled]
    public ulong ResultCount
    {
        get
        {
            RequireEnabledMainThread();
            return ReadResultCount(RequireResults());
        }
    }

    /// <summary>
    ///     Releases the readable list when necessary, then destroys the owned found-list child before the owned scanner
    ///     parent. Never retries a destruction and is safe to call more than once.
    /// </summary>
    /// <remarks>
    ///     If the runtime cannot admit cleanup, this method throws without releasing either owner or changing the session
    ///     state, so the plugin can retry before its disable callback returns. Once destruction begins, it follows
    ///     <see cref="Owned{T}.Dispose" /> and does not retry a protected CE failure.
    /// </remarks>
    [MainThreadOnly]
    public void Dispose()
    {
        if (State == MemoryScanState.Disposed) return;

        // Detached cleanup deliberately follows Owned<T>: it marks the wrappers disposed and leaks the CE objects,
        // because no state can be acquired. An attached worker thread is different: attempting CE cleanup there is
        // unsafe, so reject it before changing local ownership or touching the Lua stack.
        if (LuaRuntime.IsAttached && !LuaRuntime.IsMainThread)
            throw new InvalidOperationException(
                "Memory scan disposal must run on Cheat Engine's main thread while the plugin is attached.");

        // Admit the complete cleanup before publishing any lifetime change. Owned<T> retains an owner when it cannot
        // begin destroy(), so this session must retain both owners in that case as well.
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var foundList = _foundList!;
        var scanner = _scanner!;

        if (State == MemoryScanState.ResultsReady)
        {
            using LuaFrame frame = new(state);
            _ = foundList.Value.Handle.TryCallMethod(state, "deinitialize"u8, 0, 0);
        }

        // Each frame removes a protected-call error before the next destroy. TryDestroy consumes an owner only once its
        // protected invocation begins; the outer admitted operation keeps the binding stable for both child and parent.
        using (LuaFrame frame = new(state))
        {
            _ = foundList.TryDestroy(state);
        }

        using (LuaFrame frame = new(state))
        {
            _ = scanner.TryDestroy(state);
        }

        _foundList = null;
        _scanner = null;
        State = MemoryScanState.Disposed;
    }

    /// <summary>
    ///     Transfers two explicit ownership wrappers into a session. The source wrappers become empty; the returned
    ///     session is then their only intended destroy owner.
    /// </summary>
    /// <param name="scanner">An owned scanner whose ownership has been proven by the caller's binding.</param>
    /// <param name="foundList">An owned result-list child attached to <paramref name="scanner" />.</param>
    /// <returns>A new session in <see cref="MemoryScanState.New" />.</returns>
    /// <exception cref="ArgumentNullException">Either ownership wrapper is <see langword="null" />.</exception>
    /// <exception cref="ObjectDisposedException">Either ownership wrapper was already released or disposed.</exception>
    /// <remarks>
    ///     The method deliberately does not call <c>createMemScan</c> or <c>createFoundList</c>. Until the CE source
    ///     matrix records ownership for those APIs, a convenience factory would turn an undocumented ownership assumption
    ///     into a public destruction contract.
    /// </remarks>
    public static MemoryScanSession Adopt(Owned<MemScan> scanner, Owned<FoundList> foundList)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(foundList);

        // Value validates each source wrapper before either Transfer changes it. The wrappers are deliberately
        // single-owner and unsynchronized, exactly like Owned<T>; callers must not concurrently dispose them.
        _ = scanner.Value;
        _ = foundList.Value;
        return new MemoryScanSession(scanner.Transfer(), foundList.Transfer());
    }

    /// <summary>Begins a CE first scan with all fourteen documented positional arguments.</summary>
    /// <param name="request">The complete first-scan request.</param>
    /// <exception cref="MemoryScanStateException">The session is not new.</exception>
    /// <exception cref="ArgumentException">The request contains an unsupported first-scan option or value type.</exception>
    /// <exception cref="ArgumentNullException">A required CE string argument is <see langword="null" />.</exception>
    /// <exception cref="MemoryScanException">The protected CE method call failed.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
    [MainThreadOnly]
    [RequiresPluginEnabled]
    public void StartFirstScan(in FirstScanRequest request)
    {
        RequireEnabledMainThread();
        RequireState("StartFirstScan", MemoryScanState.New);
        ValidateFirstRequest(in request);

        // A CE error may happen after it accepts some scan setup. Do not report the old New state after a partial call.
        State = MemoryScanState.Invalidated;
        CallFirstScan(_scanner!.Value, in request);
        State = MemoryScanState.Scanning;
    }

    /// <summary>Begins a CE next scan over the previous readable result set.</summary>
    /// <param name="request">The complete next-scan request.</param>
    /// <exception cref="MemoryScanStateException">The session has not completed a first or previous next scan.</exception>
    /// <exception cref="ArgumentException">The request contains an unsupported next-scan option.</exception>
    /// <exception cref="ArgumentNullException">A required CE string argument is <see langword="null" />.</exception>
    /// <exception cref="MemoryScanException">The protected CE method call failed.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
    [MainThreadOnly]
    [RequiresPluginEnabled]
    public void StartNextScan(in NextScanRequest request)
    {
        RequireEnabledMainThread();
        RequireState("StartNextScan", MemoryScanState.ResultsReady);
        ValidateNextRequest(in request);

        // The list stops being readable as soon as this session releases it. A failed deinitialize or nextScan leaves
        // the conservative Invalidated state, from which Reset is the only recovery.
        State = MemoryScanState.Invalidated;
        CallNoResult(_foundList!.Value.Handle, "deinitialize"u8, DeinitializeResultsOperation);
        CallNextScan(_scanner!.Value, in request);
        State = MemoryScanState.Scanning;
    }

    /// <summary>
    ///     Waits through CE's no-timeout <c>waitTillDone()</c> form, then initializes the attached found list only after
    ///     CE reports completion.
    /// </summary>
    /// <exception cref="MemoryScanStateException">The session is not scanning.</exception>
    /// <exception cref="MemoryScanException">The protected CE call failed.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
    [MainThreadOnly]
    [RequiresPluginEnabled]
    public void WaitForCompletion()
    {
        RequireEnabledMainThread();
        RequireState("WaitForCompletion", MemoryScanState.Scanning);

        try
        {
            CallWaitTillDone(_scanner!.Value);

            State = MemoryScanState.Invalidated;
            CallNoResult(_foundList!.Value.Handle, "initialize"u8, InitializeResultsOperation);
            State = MemoryScanState.ResultsReady;
        }
        catch
        {
            // A wait error may mean CE is still scanning, completed, or left a partially materialized result set.
            // Never leave the session in Scanning when it cannot safely decide which of those is true.
            State = MemoryScanState.Invalidated;
            throw;
        }
    }

    /// <summary>
    ///     Releases any initialized readable view and asks CE to clear the current scan results through
    ///     <c>MemScan.newScan</c>.
    /// </summary>
    /// <exception cref="MemoryScanStateException">The session is scanning or disposed.</exception>
    /// <exception cref="MemoryScanException">The protected CE method call failed.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
    [MainThreadOnly]
    [RequiresPluginEnabled]
    public void Reset()
    {
        RequireEnabledMainThread();
        ThrowIfDisposed();
        if (State == MemoryScanState.New) return;
        if (State == MemoryScanState.Scanning) ThrowWrongState("Reset");

        State = MemoryScanState.Invalidated;
        CallNoResult(_foundList!.Value.Handle, "deinitialize"u8, DeinitializeResultsOperation);
        CallNoResult(_scanner!.Value.Handle, "newScan"u8, ResetOperation);
        State = MemoryScanState.New;
    }

    /// <summary>Attempts to read the parsed target address at a zero-based result index.</summary>
    /// <param name="zeroBasedIndex">
    ///     The CE found-list index, beginning at zero. This API deliberately supports only the managed
    ///     <see cref="int" /> index range even when <see cref="ResultCount" /> is larger.
    /// </param>
    /// <param name="address">The parsed target address when the method returns <see langword="true" />.</param>
    /// <returns><see langword="false" /> when the index is outside the current result count.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
    /// <exception cref="MemoryScanStateException">The results are not ready.</exception>
    /// <exception cref="MemoryScanException">CE failed or returned an address string that cannot be parsed.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
    [MainThreadOnly]
    [RequiresPluginEnabled]
    public bool TryGetAddress(int zeroBasedIndex, out Address address)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
        RequireEnabledMainThread();
        var foundList = RequireResults();
        if ((ulong)zeroBasedIndex >= ReadResultCount(foundList))
        {
            address = default;
            return false;
        }

        var text = CallString(foundList.Handle, "getAddress"u8, ResultAddressOperation, zeroBasedIndex);
        if (Address.TryParse(text, out address)) return true;

        throw new MemoryScanException(MemoryScanFailureKind.UnexpectedResult, ResultAddressOperation,
            "The memory scan result address was not a hexadecimal target address.");
    }

    /// <summary>Attempts to read the exact value text at a zero-based result index.</summary>
    /// <param name="zeroBasedIndex">
    ///     The CE found-list index, beginning at zero. This API deliberately supports only the managed
    ///     <see cref="int" /> index range even when <see cref="ResultCount" /> is larger.
    /// </param>
    /// <param name="value">The copied CE value text when the method returns <see langword="true" />.</param>
    /// <returns><see langword="false" /> when the index is outside the current result count.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
    /// <exception cref="MemoryScanStateException">The results are not ready.</exception>
    /// <exception cref="MemoryScanException">The protected CE method call failed or did not return text.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the caller is not on its main thread.</exception>
    [MainThreadOnly]
    [RequiresPluginEnabled]
    public bool TryGetValue(int zeroBasedIndex, [NotNullWhen(true)] out string? value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
        RequireEnabledMainThread();
        var foundList = RequireResults();
        if ((ulong)zeroBasedIndex >= ReadResultCount(foundList))
        {
            value = null;
            return false;
        }

        value = CallString(foundList.Handle, "getValue"u8, ResultValueOperation, zeroBasedIndex);
        return true;
    }

    private FoundList RequireResults()
    {
        RequireState("results", MemoryScanState.ResultsReady);
        return _foundList!.Value;
    }

    private void RequireState(string operation, MemoryScanState expected)
    {
        ThrowIfDisposed();
        if (State != expected) ThrowWrongState(operation);
    }

    private void ThrowIfDisposed()
    {
        if (State == MemoryScanState.Disposed)
            throw new ObjectDisposedException(nameof(MemoryScanSession), "The memory-scan session was disposed.");
    }

    [DoesNotReturn]
    private void ThrowWrongState(string operation)
    {
        throw new MemoryScanStateException(operation, State);
    }

    private static void RequireEnabledMainThread()
    {
        if (!LuaRuntime.IsAttached)
            throw new InvalidOperationException(
                "The Cheat Engine plugin is not enabled, so the memory scan cannot acquire its Lua state.");
        if (!LuaRuntime.IsMainThread)
            throw new InvalidOperationException(
                "Memory scan operations must run on Cheat Engine's main thread; the session does not dispatch work implicitly.");
    }

    private static void ValidateFirstRequest(in FirstScanRequest request)
    {
        // A default-initialized request can contain null strings.  These are properties of the
        // value-type request rather than parameters of this helper, therefore its parameter name
        // must be the actual public argument ("request") instead of a local alias.
        ArgumentNullException.ThrowIfNull(request.Input1, nameof(request));
        ArgumentNullException.ThrowIfNull(request.Input2, nameof(request));
        ArgumentNullException.ThrowIfNull(request.ProtectionFlags, nameof(request));
        ArgumentNullException.ThrowIfNull(request.AlignmentParameter, nameof(request));

        if (request.ScanOption is < ScanOption.UnknownValue or > ScanOption.SmallerThan)
            throw new ArgumentException(
                "A first scan only accepts UnknownValue, ExactValue, ValueBetween, BiggerThan or SmallerThan.",
                nameof(request));
        // CE 7.7 celua.txt line 2587 lists vtGrouped in addition to the contiguous Byte..All range.
        if ((uint)request.VariableType > (uint)VariableType.All && request.VariableType != VariableType.Grouped)
            throw new ArgumentException("The CE 7.7 firstScan contract accepts Byte through All and Grouped.",
                nameof(request));
        if (request.RoundingType is < RoundingType.Rounded or > RoundingType.Truncated)
            throw new ArgumentException("The rounding type is not a CE 7.7 value.", nameof(request));
        if (request.FastScanMethod is < FastScanMethod.NotAligned or > FastScanMethod.LastDigits)
            throw new ArgumentException("The fast scan method is not a CE 7.7 value.", nameof(request));
    }

    private static void ValidateNextRequest(in NextScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Input1, nameof(request));
        ArgumentNullException.ThrowIfNull(request.Input2, nameof(request));
        if (request.ScanOption is < ScanOption.ExactValue or > ScanOption.Unchanged)
            throw new ArgumentException("A next scan only accepts ExactValue through Unchanged, never UnknownValue.",
                nameof(request));
        if (request.RoundingType is < RoundingType.Rounded or > RoundingType.Truncated)
            throw new ArgumentException("The rounding type is not a CE 7.7 value.", nameof(request));
    }

    private static void CallFirstScan(MemScan scanner, in FirstScanRequest request)
    {
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        using LuaFrame frame = new(state);
        EnumMarshaller<ScanOption>.Push(state, request.ScanOption);
        EnumMarshaller<VariableType>.Push(state, request.VariableType);
        EnumMarshaller<RoundingType>.Push(state, request.RoundingType);
        StringMarshaller.Push(state, request.Input1);
        StringMarshaller.Push(state, request.Input2);
        state.PushInteger(request.StartAddress.ToInt64());
        state.PushInteger(request.StopAddress.ToInt64());
        StringMarshaller.Push(state, request.ProtectionFlags);
        EnumMarshaller<FastScanMethod>.Push(state, request.FastScanMethod);
        StringMarshaller.Push(state, request.AlignmentParameter);
        BooleanMarshaller.Push(state, request.IsHexadecimalInput);
        BooleanMarshaller.Push(state, request.IsNotBinaryString);
        BooleanMarshaller.Push(state, request.IsUnicodeScan);
        BooleanMarshaller.Push(state, request.IsCaseSensitive);

        var status = scanner.Handle.TryCallMethod(state, "firstScan"u8, 14, 0);
        if (!status.IsOk) ThrowLua(state, status, FirstScanOperation);
    }

    private static void CallNextScan(MemScan scanner, in NextScanRequest request)
    {
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        using LuaFrame frame = new(state);
        EnumMarshaller<ScanOption>.Push(state, request.ScanOption);
        EnumMarshaller<RoundingType>.Push(state, request.RoundingType);
        StringMarshaller.Push(state, request.Input1);
        StringMarshaller.Push(state, request.Input2);
        BooleanMarshaller.Push(state, request.IsHexadecimalInput);
        BooleanMarshaller.Push(state, request.IsNotBinaryString);
        BooleanMarshaller.Push(state, request.IsUnicodeScan);
        BooleanMarshaller.Push(state, request.IsCaseSensitive);
        BooleanMarshaller.Push(state, request.IsPercentageScan);

        var argumentCount = 9;
        if (request.SavedResultName is not null)
        {
            StringMarshaller.Push(state, request.SavedResultName);
            argumentCount = 10;
        }

        var status = scanner.Handle.TryCallMethod(state, "nextScan"u8, argumentCount, 0);
        if (!status.IsOk) ThrowLua(state, status, NextScanOperation);
    }

    private static void CallWaitTillDone(MemScan scanner)
    {
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        using LuaFrame frame = new(state);
        var status = scanner.Handle.TryCallMethod(state, "waitTillDone"u8, 0, 0);
        if (!status.IsOk) ThrowLua(state, status, WaitForCompletionOperation);
    }

    private static ulong ReadResultCount(FoundList foundList)
    {
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        using LuaFrame frame = new(state);
        var status = foundList.Handle.TryCallMethod(state, "getCount"u8, 0, 1);
        if (!status.IsOk) ThrowLua(state, status, ResultCountOperation);
        if (!Int64Marshaller.TryRead(state, -1, out var count) || count < 0)
            throw new MemoryScanException(MemoryScanFailureKind.UnexpectedResult, ResultCountOperation,
                "The memory scan result count was not a non-negative 64-bit Lua integer.");

        return (ulong)count;
    }

    private static string CallString(CEObject target, ReadOnlySpan<byte> method, string operation, int index)
    {
        using var luaOperation = LuaRuntime.AcquireOperation();
        var state = luaOperation.State;
        using LuaFrame frame = new(state);
        state.PushInteger(index);
        var status = target.TryCallMethod(state, method, 1, 1);
        if (!status.IsOk) ThrowLua(state, status, operation);
        if (StringMarshaller.TryRead(state, -1, out var value)) return value;

        throw new MemoryScanException(MemoryScanFailureKind.UnexpectedResult, operation,
            "The memory scan operation did not return text.");
    }

    private static void CallNoResult(CEObject target, ReadOnlySpan<byte> method, string operation)
    {
        using var luaOperation = LuaRuntime.AcquireOperation();
        var state = luaOperation.State;
        using LuaFrame frame = new(state);
        var status = target.TryCallMethod(state, method, 0, 0);
        if (!status.IsOk) ThrowLua(state, status, operation);
    }

    [DoesNotReturn]
    private static void ThrowLua(LuaState state, LuaStatus status, string operation)
    {
        var error = LuaError.FromStack(state, status);
        throw new MemoryScanException(MemoryScanFailureKind.LuaError, operation,
            "The protected Lua call for memory scan operation '" + operation + "' failed.", new LuaException(error));
    }
}
