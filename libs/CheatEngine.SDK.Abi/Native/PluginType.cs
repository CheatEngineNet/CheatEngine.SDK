namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     The kind of plugin function a native plugin registers through the <c>RegisterFunction</c> slot of the classic
///     exported-functions table. Each kind has its own registration record, named after it in this namespace.
/// </summary>
/// <remarks>
///     <para>
///         <b>Evidence (verified, two sources agree on names and values):</b> the plugin-type enumeration of
///         <c>cepluginsdk.h</c> and of <c>cepluginsdk.pas</c> (CE 7.7.0.10621). The members below keep the upstream
///         names without their <c>pt</c> prefix; the upstream spelling is given on each member.
///     </para>
///     <para>
///         <b>Width.</b> 4 bytes here, matching the C enumeration. The Pascal unit may compile its enumeration to a single
///         byte (<i>inferred</i> from the compiler mode the unit selects), which is harmless because the value only ever
///         travels by value in a register or a full stack slot, never inside a structure.
///     </para>
///     <para>Native (Native AOT) load path only. A managed plugin has no <c>RegisterFunction</c> slot.</para>
/// </remarks>
public enum PluginType
{
    /// <summary>
    ///     Upstream <c>ptAddressList</c>: context-menu entry of the address list. Record:
    ///     <see cref="AddressListPluginInit" />.
    /// </summary>
    AddressList = 0,

    /// <summary>
    ///     Upstream <c>ptMemoryView</c>: menu entry of the memory view window. Record:
    ///     <see cref="MemoryViewPluginInit" />.
    /// </summary>
    MemoryView = 1,

    /// <summary>Upstream <c>ptOnDebugEvent</c>: debug event filter. Record: <see cref="DebugEventPluginInit" />.</summary>
    OnDebugEvent = 2,

    /// <summary>
    ///     Upstream <c>ptProcesswatcherEvent</c>: process creation/termination notification. Record:
    ///     <see cref="ProcessWatcherPluginInit" />.
    /// </summary>
    ProcessWatcherEvent = 3,

    /// <summary>
    ///     Upstream <c>ptFunctionPointerchange</c>: notification that an API hook slot changed. Record:
    ///     <see cref="FunctionPointerChangePluginInit" />.
    /// </summary>
    FunctionPointerChange = 4,

    /// <summary>Upstream <c>ptMainMenu</c>: entry in the main window's plugin menu. Record: <see cref="MainMenuPluginInit" />.</summary>
    MainMenu = 5,

    /// <summary>
    ///     Upstream <c>ptDisassemblerContext</c>: context-menu entry of the disassembler view. Record:
    ///     <see cref="DisassemblerContextPluginInit" />.
    /// </summary>
    DisassemblerContext = 6,

    /// <summary>
    ///     Upstream <c>ptDisassemblerRenderLine</c>: per-line rendering hook of the disassembler view. Record:
    ///     <see cref="DisassemblerRenderLinePluginInit" />.
    /// </summary>
    DisassemblerRenderLine = 7,

    /// <summary>
    ///     Upstream <c>ptAutoAssembler</c>: auto-assembler line preprocessor. Record:
    ///     <see cref="AutoAssemblerPluginInit" />.
    /// </summary>
    AutoAssembler = 8
}
