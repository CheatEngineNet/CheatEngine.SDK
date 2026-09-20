using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Scanning;

/// <summary>
///     Contract tests for the stateful MemScan/FoundList slice. The fixture is a Lua model with the exact CE member
///     names and argument counts, not a live Cheat Engine process; CE 7.7 live ownership remains separately opt-in.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class MemoryScanSessionTests
{
    [Fact]
    public void First_scan_wait_and_read_follow_the_documented_CE_sequence()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State);

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        Assert.Equal(MemoryScanState.Scanning, session.State);
        session.WaitForCompletion();
        Assert.Equal(MemoryScanState.ResultsReady, session.State);
        Assert.Equal(2UL, session.ResultCount);

        Assert.True(session.TryGetAddress(0, out var first));
        Assert.Equal(new Address(0x1234), first);
        Assert.True(session.TryGetAddress(1, out var second));
        Assert.Equal(new Address(0xFFFF_FFFF_FFFF_FFFF), second);
        Assert.False(session.TryGetAddress(2, out _));
        Assert.True(session.TryGetValue(0, out var value));
        Assert.Equal("100", value);
        Assert.Equal("scan.first:14,scan.wait,list.initialize,results.getCount,results.getCount,results.getAddress:0,results.getCount,results.getAddress:1,results.getCount,results.getCount,results.getValue:0",
            ReadTrace(scope.State));
    }

    [Fact]
    public void Next_scan_is_rejected_until_a_first_scan_completed_and_omits_an_unspecified_optional_argument()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State);

        var beforeFirst = Assert.Throws<MemoryScanStateException>(() =>
            session.StartNextScan(NextScanRequest.ExactValue("90")));
        Assert.Equal(MemoryScanState.New, beforeFirst.State);

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        session.WaitForCompletion();
        EngineTest.Run(scope.State, "trace = {}"u8);

        session.StartNextScan(NextScanRequest.ExactValue("90"));

        Assert.Equal(MemoryScanState.Scanning, session.State);
        Assert.Equal("list.deinitialize,scan.next:9", ReadTrace(scope.State));
    }

    [Fact]
    public void Next_scan_passes_a_present_saved_result_name_as_the_tenth_argument()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State);

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        session.WaitForCompletion();
        EngineTest.Run(scope.State, "trace = {}"u8);

        session.StartNextScan(new NextScanRequest(
            ScanOption.ExactValue,
            RoundingType.Rounded,
            "90",
            string.Empty,
            false,
            false,
            false,
            false,
            false,
            "baseline"));

        Assert.Equal(MemoryScanState.Scanning, session.State);
        Assert.Equal("list.deinitialize,scan.next:10", ReadTrace(scope.State));
    }

    [Fact]
    public void Wait_error_invalidates_the_session_so_reset_can_recover()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State, waitRaises: true);

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Grouped, "100"));

        var failure = Assert.Throws<MemoryScanException>(session.WaitForCompletion);

        Assert.Equal(MemoryScanFailureKind.LuaError, failure.FailureKind);
        Assert.Equal("MemoryScan.WaitForCompletion", failure.Operation);
        Assert.Equal(MemoryScanState.Invalidated, session.State);
        session.Reset();
        Assert.Equal(MemoryScanState.New, session.State);
        Assert.Equal("scan.first:14,scan.wait,list.deinitialize,scan.new", ReadTrace(scope.State));
    }

    [Fact]
    public void Dispose_releases_and_destroys_the_child_before_the_parent()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var session = CreateSession(scope.State);

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        session.WaitForCompletion();
        EngineTest.Run(scope.State, "trace = {}"u8);

        session.Dispose();
        session.Dispose();

        Assert.Equal(MemoryScanState.Disposed, session.State);
        Assert.Equal("list.deinitialize,list.destroy,scan.destroy", ReadTrace(scope.State));
        Assert.Throws<ObjectDisposedException>(() => _ = session.Scanner);
    }

    [Fact]
    public void Dispose_while_detached_retains_owned_objects_for_a_retry()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State);

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        session.WaitForCompletion();
        var scanner = session.Scanner.Handle;
        var foundList = session.Results.Handle;

        LuaRuntime.Detach();
        try
        {
            Assert.Throws<InvalidOperationException>(session.Dispose);
            Assert.Equal(MemoryScanState.ResultsReady, session.State);
            Assert.Equal(scanner, session.Scanner.Handle);
            Assert.Equal(foundList, session.Results.Handle);
            Assert.False(FakeHost.IsDestroyed(scope.State, scanner));
            Assert.False(FakeHost.IsDestroyed(scope.State, foundList));
        }
        finally
        {
            LuaRuntime.Attach(scope.Binding);
        }

        session.Dispose();

        Assert.Equal(MemoryScanState.Disposed, session.State);
        Assert.True(FakeHost.IsDestroyed(scope.State, foundList));
        Assert.True(FakeHost.IsDestroyed(scope.State, scanner));
    }

    [Fact]
    public void A_large_found_list_count_is_preserved_without_widening_row_indices()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State, resultCountLiteral: "3000000000");

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        session.WaitForCompletion();

        Assert.Equal(3_000_000_000UL, session.ResultCount);
        Assert.True(session.Results.TryGetCount(out var rawCount));
        Assert.Equal(3_000_000_000UL, rawCount);
        Assert.True(session.TryGetValue(0, out var value));
        Assert.Equal("100", value);
    }

    [Fact]
    public void A_negative_found_list_count_is_rejected_by_raw_and_session_reads()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State, resultCountLiteral: "-1");

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        session.WaitForCompletion();

        Assert.False(session.Results.TryGetCount(out var rawCount));
        Assert.Equal(0UL, rawCount);
        var failure = Assert.Throws<MemoryScanException>(() => _ = session.ResultCount);
        Assert.Equal(MemoryScanFailureKind.UnexpectedResult, failure.FailureKind);
        Assert.Equal("MemoryScan.ResultCount", failure.Operation);
    }

    [Fact]
    public void An_empty_found_list_does_not_call_address_or_value_methods_out_of_range()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State, resultCountLiteral: "0");

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        session.WaitForCompletion();

        Assert.Equal(0UL, session.ResultCount);
        Assert.True(session.Results.TryGetCount(out var rawCount));
        Assert.Equal(0UL, rawCount);
        EngineTest.Run(scope.State, "trace = {}"u8);

        Assert.False(session.TryGetAddress(0, out var address));
        Assert.Equal(default, address);
        Assert.False(session.TryGetValue(0, out var value));
        Assert.Null(value);
        Assert.Equal("results.getCount,results.getCount", ReadTrace(scope.State));
    }

    [Fact]
    public void Wait_for_completion_requests_zero_Lua_results_from_wait_till_done()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State);

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        using var probe = FakeHost.ReplaceWaitTillDoneWithPCallProbe(scope.State, session.Scanner.Handle);

        session.WaitForCompletion();

        Assert.Equal(MemoryScanState.ResultsReady, session.State);
        Assert.Equal(1, probe.WaitCallCount);
        Assert.Equal(0, probe.WaitArgumentCount);
        Assert.Equal(0, probe.WaitResultCount);
    }

    [Fact]
    public void Failed_protected_scan_invalidates_the_session_until_reset_succeeds()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State, firstScanRaises: true);

        var failure = Assert.Throws<MemoryScanException>(() =>
            session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100")));

        Assert.Equal(MemoryScanFailureKind.LuaError, failure.FailureKind);
        Assert.Equal("MemoryScan.FirstScan", failure.Operation);
        Assert.DoesNotContain("first scan rejected", failure.Message, StringComparison.Ordinal);
        Assert.IsType<CheatEngine.SDK.Lua.Calls.LuaException>(failure.InnerException);
        Assert.Equal(MemoryScanState.Invalidated, session.State);
        Assert.Throws<MemoryScanStateException>(() =>
            session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100")));

        session.Reset();

        Assert.Equal(MemoryScanState.New, session.State);
        Assert.Equal("scan.first:14,list.deinitialize,scan.new", ReadTrace(scope.State));
    }

    [Fact]
    public void A_main_thread_only_scan_operation_is_rejected_before_it_touches_CE()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State);

        var failure = EngineTest.RunOnWorker(() =>
            session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100")));

        var exception = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("main thread", exception.Message, StringComparison.Ordinal);
        Assert.Equal(MemoryScanState.New, session.State);
        Assert.Equal(string.Empty, ReadTrace(scope.State));
    }

    [Fact]
    public void Disposal_on_a_worker_is_rejected_before_child_or_parent_cleanup()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var session = CreateSession(scope.State);

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        session.WaitForCompletion();
        EngineTest.Run(scope.State, "trace = {}"u8);

        var failure = EngineTest.RunOnWorker(session.Dispose);

        var exception = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("main thread", exception.Message, StringComparison.Ordinal);
        Assert.Equal(MemoryScanState.ResultsReady, session.State);
        Assert.Equal(string.Empty, ReadTrace(scope.State));

        session.Dispose();
    }

    [Fact]
    public void Adopt_transfers_the_source_owners_and_keeps_borrowed_handle_identity()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var scan = FakeHost.CreateObject(scope.State, "Object", ScanInitializer(false, false));
        var foundList = FakeHost.CreateObject(scope.State, "Object", FoundListInitializer());
        var scanOwner = new Owned<MemScan>(MemScan.FromHandle(scan));
        var foundListOwner = new Owned<FoundList>(FoundList.FromHandle(foundList));

        using var session = MemoryScanSession.Adopt(scanOwner, foundListOwner);

        Assert.True(scanOwner.IsDisposed);
        Assert.True(foundListOwner.IsDisposed);
        Assert.Equal(scan, session.Scanner.Handle);
    }

    [Fact]
    public void A_non_hexadecimal_address_text_is_a_stable_unexpected_host_result()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        using var session = CreateSession(scope.State, invalidAddress: true);

        session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
        session.WaitForCompletion();

        var failure = Assert.Throws<MemoryScanException>(() => session.TryGetAddress(0, out _));

        Assert.Equal(MemoryScanFailureKind.UnexpectedResult, failure.FailureKind);
        Assert.Equal("MemoryScan.ResultAddress", failure.Operation);
    }

    private static MemoryScanSession CreateSession(LuaState state, bool firstScanRaises = false,
        bool invalidAddress = false, bool waitRaises = false, string resultCountLiteral = "2")
    {
        EngineTest.Run(state, "trace = {}"u8);
        var scan = FakeHost.CreateObject(state, "Object",
            ScanInitializer(firstScanRaises, waitRaises));
        var foundList = FakeHost.CreateObject(state, "Object", FoundListInitializer(invalidAddress, resultCountLiteral));
        return MemoryScanSession.Adopt(
            new Owned<MemScan>(MemScan.FromHandle(scan)),
            new Owned<FoundList>(FoundList.FromHandle(foundList)));
    }

    private static string ScanInitializer(bool firstScanRaises, bool waitRaises)
    {
        var raiseFirstScan = firstScanRaises ? "; error('first scan rejected')" : string.Empty;
        var raiseWait = waitRaises ? "; error('wait rejected')" : string.Empty;
        return "o.props.firstScan = function(...) local n = select('#', ...); if n ~= 14 then error('firstScan argument count') end; local scanoption, vartype, roundingtype, input1, input2, startAddress, stopAddress, protectionflags, alignmenttype, alignmentparam, hexadecimal, nonbinary, unicode, casesensitive = ...; if scanoption ~= 1 or (vartype ~= 2 and vartype ~= 14) or roundingtype ~= 0 or input1 ~= '100' or input2 ~= '' or startAddress ~= 0 or stopAddress ~= -1 or protectionflags ~= '' or alignmenttype ~= 0 or alignmentparam ~= '' or hexadecimal ~= false or nonbinary ~= false or unicode ~= false or casesensitive ~= false then error('firstScan argument values') end; table.insert(trace, 'scan.first:' .. n)" + raiseFirstScan + " end\n" +
               "o.props.nextScan = function(...) local n = select('#', ...); if n ~= 9 and n ~= 10 then error('nextScan argument count') end; local scanoption, roundingtype, input1, input2, hexadecimal, nonbinary, unicode, casesensitive, percentage, savedresultname = ...; if scanoption ~= 1 or roundingtype ~= 0 or input1 ~= '90' or input2 ~= '' or hexadecimal ~= false or nonbinary ~= false or unicode ~= false or casesensitive ~= false or percentage ~= false or (n == 9 and savedresultname ~= nil) or (n == 10 and savedresultname ~= 'baseline') then error('nextScan argument values') end; table.insert(trace, 'scan.next:' .. n) end\n" +
               "o.props.waitTillDone = function() table.insert(trace, 'scan.wait')" + raiseWait + " end\n" +
               "o.props.newScan = function() table.insert(trace, 'scan.new') end\n" +
               "o.getters.destroy = function(o) return function() o.destroyed = true; table.insert(trace, 'scan.destroy') end end";
    }

    private static string FoundListInitializer(bool invalidAddress = false, string resultCountLiteral = "2")
    {
        var firstAddress = invalidAddress ? "'not-an-address'" : "'00001234'";
        return "o.props.initialize = function() table.insert(trace, 'list.initialize') end\n" +
               "o.props.deinitialize = function() table.insert(trace, 'list.deinitialize') end\n" +
               "o.props.Count = " + resultCountLiteral + "\n" +
               "o.props.getCount = function() table.insert(trace, 'results.getCount'); return o.props.Count end\n" +
               "o.props.getAddress = function(index) table.insert(trace, 'results.getAddress:' .. index); if index == 0 then return " + firstAddress + " end; return 'FFFFFFFFFFFFFFFF' end\n" +
               "o.props.getValue = function(index) table.insert(trace, 'results.getValue:' .. index); return '100' end\n" +
               "o.getters.destroy = function(o) return function() o.destroyed = true; table.insert(trace, 'list.destroy') end end";
    }

    private static string ReadTrace(LuaState state)
    {
        using LuaFrame frame = new(state);
        EngineTest.Run(state, "return table.concat(trace, ',')"u8, 1);
        return EngineTest.ReadString(state, -1);
    }
}
