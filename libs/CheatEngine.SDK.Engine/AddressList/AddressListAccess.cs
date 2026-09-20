using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Lua.References;

namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>Entry points for Cheat Engine's one GUI address list.</summary>
/// <remarks>
///     The wrapped global is exactly <c>getAddressList()</c>. Its function reference is cached only for the active
///     <see cref="CheatEngine.SDK.Lua.Runtime.LuaRuntime.Epoch" />; a disable/re-enable invalidates the cache through
///     <see cref="LuaRef" />'s epoch guard. The returned object is owned by Cheat Engine, not by the plugin.
/// </remarks>
public static class AddressListAccess
{
    private static readonly LuaRef SGetAddressList = new();

    /// <summary>Gets Cheat Engine's address-list object.</summary>
    /// <param name="addressList">A borrowed, Cheat-Engine-owned address list; default on failure.</param>
    /// <returns>
    ///     <see langword="true" /> when the <c>getAddressList</c> global resolved, completed, and returned host userdata;
    ///     otherwise <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     Evidence: exact installed CE 7.7.0.10621 x64 <c>celua.txt</c>, SHA-256
    ///     <c>AA1342B4A5D5D5C65B255FB3A8FD7B6BCBBAC1CD138961669D9F37F43E0B9C00</c>, line 774. The GUI-thread restriction is
    ///     inferred from the returned <c>Addresslist</c> panel. This wrapper deliberately does not annotate the call as
    ///     <c>MainThreadOnly</c> until a CE 7.7 dispatcher probe establishes an enforceable check.
    /// </remarks>
    [RequiresPluginEnabled]
    public static bool TryGetCurrent([CEOwned] out AddressList addressList)
    {
        return AddressListCalls.TryGetGlobal<AddressList, AddressList>(SGetAddressList, "getAddressList"u8,
            out addressList);
    }
}
