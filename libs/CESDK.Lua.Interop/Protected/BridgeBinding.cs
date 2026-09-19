namespace CESDK.Lua.Interop.Protected;

internal sealed class BridgeBinding(nint module, LuaProtectedExports exports)
{
    internal nint Module { get; } = module;
    internal LuaProtectedExports Exports { get; } = exports;
}
