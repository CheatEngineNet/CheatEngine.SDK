using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Abi.Native;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     Exercises only the qualified callback projections of classic registration records. Real <c>stdcall</c>
///     functions are stored and invoked only through those typed slots; conflicting slots are merely round-tripped as
///     opaque addresses.
/// </summary>
public sealed unsafe class PluginCallbackShapeTests
{
    private static int s_mainMenuCalls;

    [Fact]
    public void AddressList_callback_slot_stays_opaque_until_a_live_canary_qualifies_the_selection_record()
    {
        AddressListPluginInit init = default;
        delegate* unmanaged[Stdcall]<void> function = &FakeMainMenu;
        init.Callback = function;

        Assert.Equal((nint)function, (nint)init.Callback);
    }

    [Fact]
    public void MemoryView_callback_takes_three_in_out_addresses_and_returns_Bool32()
    {
        MemoryViewPluginInit init = default;
        init.Callback = &FakeMemoryView;
        nuint disassembler = 1;
        nuint selected = 2;
        nuint hexView = 0x7FFF_0000;

        var result = init.Callback(&disassembler, &selected, &hexView);

        Assert.True(result.IsTrue);
        Assert.Equal(hexView, disassembler);
        Assert.Equal((nuint)2, selected);
    }

    [Fact]
    public void DebugEvent_callback_takes_an_event_pointer_and_returns_int()
    {
        DebugEventPluginInit init = default;
        init.Callback = &FakeDebugEvent;

        Assert.Equal(1, init.Callback((void*)0x20));
        Assert.Equal(0, init.Callback(null));
    }

    [Fact]
    public void MainMenu_callback_takes_nothing_and_returns_nothing()
    {
        MainMenuPluginInit init = default;
        init.Callback = &FakeMainMenu;
        var before = s_mainMenuCalls;

        init.Callback();

        Assert.Equal(before + 1, s_mainMenuCalls);
    }

    [Fact]
    public void DisassemblerContext_click_slot_stays_opaque_until_a_live_canary_qualifies_its_boolean_width()
    {
        DisassemblerContextPluginInit init = default;
        delegate* unmanaged[Stdcall]<void> function = &FakeMainMenu;
        init.Callback = function;

        Assert.Equal((nint)function, (nint)init.Callback);
    }

    [Fact]
    public void DisassemblerContext_popup_slot_stays_opaque_until_a_live_canary_establishes_its_shape()
    {
        DisassemblerContextPluginInit init = default;
        delegate* unmanaged[Stdcall]<void> function = &FakeMainMenu;
        init.CallbackOnPopup = function;

        Assert.Equal((nint)function, (nint)init.CallbackOnPopup);
    }

    [Fact]
    public void DisassemblerRenderLine_callback_takes_address_four_texts_and_a_colour()
    {
        DisassemblerRenderLinePluginInit init = default;
        init.Callback = &FakeRenderLine;
        byte* addressText = null;
        byte* bytesText = null;
        byte* opcodeText = null;
        byte* specialText = null;
        uint colour = 0;

        init.Callback(0x40_0000, &addressText, &bytesText, &opcodeText, &specialText, &colour);

        Assert.Equal(1, (nint)addressText);
        Assert.Equal(2, (nint)bytesText);
        Assert.Equal(3, (nint)opcodeText);
        Assert.Equal(4, (nint)specialText);
        Assert.Equal(0x00FF_00FFu, colour);
    }

    [Fact]
    public void AutoAssembler_callback_takes_line_phase_and_id()
    {
        AutoAssemblerPluginInit init = default;
        init.Callback = &FakeAutoAssembler;
        byte* line = null;

        init.Callback(&line, AutoAssemblerPhase.Phase2, 77);

        Assert.Equal(((nint)AutoAssemblerPhase.Phase2 << 16) | 77, (nint)line);
    }

    /// <summary>
    ///     The two disputed callbacks stay untyped: the slot accepts any function address, nothing is implied about its
    ///     shape.
    /// </summary>
    [Fact]
    public void Untyped_callbacks_round_trip_a_function_address()
    {
        delegate* unmanaged[Stdcall]<void> function = &FakeMainMenu;
        ProcessWatcherPluginInit processWatcher = default;
        FunctionPointerChangePluginInit pointerChange = default;

        processWatcher.Callback = function;
        pointerChange.Callback = function;

        Assert.Equal((nint)function, (nint)processWatcher.Callback);
        Assert.Equal((nint)function, Unsafe.ReadUnaligned<nint>(&pointerChange));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool32 FakeMemoryView(nuint* disassemblerAddress, nuint* selectedDisassemblerAddress,
        nuint* hexViewAddress)
    {
        _ = selectedDisassemblerAddress;
        *disassemblerAddress = *hexViewAddress;
        return Bool32.True;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FakeDebugEvent(void* debugEvent)
    {
        return debugEvent is null ? 0 : 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void FakeMainMenu()
    {
        s_mainMenuCalls++;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void FakeRenderLine(nuint address, byte** addressText, byte** bytesText, byte** opcodeText,
        byte** specialText, uint* textColour)
    {
        _ = address;
        *addressText = (byte*)1;
        *bytesText = (byte*)2;
        *opcodeText = (byte*)3;
        *specialText = (byte*)4;
        *textColour = 0x00FF_00FF;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void FakeAutoAssembler(byte** line, AutoAssemblerPhase phase, int id)
    {
        *line = (byte*)(((nint)phase << 16) | id);
    }
}
