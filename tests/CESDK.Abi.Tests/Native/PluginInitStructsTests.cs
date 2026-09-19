using CESDK.Abi.Native;
using CESDK.Abi.Tests.Support;

namespace CESDK.Abi.Tests.Native;

/// <summary>Layout of the nine registration records of the classic (native) path.</summary>
public sealed unsafe class PluginInitStructsTests
{
    [Fact]
    public void AddressListPluginInit_on_64_bit_is_name_then_callback()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        AddressListPluginInit init = default;
        void* origin = &init;

        Assert.Equal(16, Layout.SizeOf<AddressListPluginInit>());
        Assert.Equal(0, Layout.OffsetOf(origin, &init.Name));
        Assert.Equal(8, Layout.OffsetOf(origin, &init.Callback));
    }

    [Fact]
    public void MemoryViewPluginInit_on_64_bit_is_name_callback_shortcut()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        MemoryViewPluginInit init = default;
        void* origin = &init;

        Assert.Equal(24, Layout.SizeOf<MemoryViewPluginInit>());
        Assert.Equal(0, Layout.OffsetOf(origin, &init.Name));
        Assert.Equal(8, Layout.OffsetOf(origin, &init.Callback));
        Assert.Equal(16, Layout.OffsetOf(origin, &init.Shortcut));
    }

    [Fact]
    public void DebugEventPluginInit_on_64_bit_is_a_single_callback()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        DebugEventPluginInit init = default;

        Assert.Equal(8, Layout.SizeOf<DebugEventPluginInit>());
        Assert.Equal(0, Layout.OffsetOf(&init, &init.Callback));
    }

    [Fact]
    public void ProcessWatcherPluginInit_on_64_bit_is_a_single_untyped_callback()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        ProcessWatcherPluginInit init = default;

        Assert.Equal(8, Layout.SizeOf<ProcessWatcherPluginInit>());
        Assert.Equal(0, Layout.OffsetOf(&init, &init.Callback));
    }

    [Fact]
    public void FunctionPointerChangePluginInit_on_64_bit_is_a_single_untyped_callback()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        FunctionPointerChangePluginInit init = default;

        Assert.Equal(8, Layout.SizeOf<FunctionPointerChangePluginInit>());
        Assert.Equal(0, Layout.OffsetOf(&init, &init.Callback));
    }

    [Fact]
    public void MainMenuPluginInit_on_64_bit_is_name_callback_shortcut()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        MainMenuPluginInit init = default;
        void* origin = &init;

        Assert.Equal(24, Layout.SizeOf<MainMenuPluginInit>());
        Assert.Equal(0, Layout.OffsetOf(origin, &init.Name));
        Assert.Equal(8, Layout.OffsetOf(origin, &init.Callback));
        Assert.Equal(16, Layout.OffsetOf(origin, &init.Shortcut));
    }

    [Fact]
    public void DisassemblerContextPluginInit_on_64_bit_is_name_callback_popup_shortcut()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        DisassemblerContextPluginInit init = default;
        void* origin = &init;

        Assert.Equal(32, Layout.SizeOf<DisassemblerContextPluginInit>());
        Assert.Equal(0, Layout.OffsetOf(origin, &init.Name));
        Assert.Equal(8, Layout.OffsetOf(origin, &init.Callback));
        Assert.Equal(16, Layout.OffsetOf(origin, &init.CallbackOnPopup));
        Assert.Equal(24, Layout.OffsetOf(origin, &init.Shortcut));
    }

    [Fact]
    public void DisassemblerRenderLinePluginInit_on_64_bit_is_a_single_callback()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        DisassemblerRenderLinePluginInit init = default;

        Assert.Equal(8, Layout.SizeOf<DisassemblerRenderLinePluginInit>());
        Assert.Equal(0, Layout.OffsetOf(&init, &init.Callback));
    }

    [Fact]
    public void AutoAssemblerPluginInit_on_64_bit_is_a_single_callback()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        AutoAssemblerPluginInit init = default;

        Assert.Equal(8, Layout.SizeOf<AutoAssemblerPluginInit>());
        Assert.Equal(0, Layout.OffsetOf(&init, &init.Callback));
    }
}
