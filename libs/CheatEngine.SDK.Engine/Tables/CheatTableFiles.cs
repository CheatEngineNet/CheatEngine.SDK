using System;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Tables;

/// <summary>Protected bindings for loading and saving Cheat Engine table files.</summary>
/// <remarks>
///     CE 7.7.0.10621 x64 <c>celua.txt</c> documents <c>loadTable(filename, merge)</c> and
///     <c>saveTable(filename)</c>. This API treats its path argument as opaque host input: it normalizes no path,
///     applies no file-root policy, and owns no file or CE object. It only preserves the exact Lua call shapes,
///     protected failure category, attach-epoch-aware function cache, and stack restoration.
/// </remarks>
public static partial class CheatTableFiles
{
    /// <summary>Loads a Cheat Engine table file, optionally merging it into the current address list.</summary>
    /// <param name="path">The opaque path text passed directly to CE.</param>
    /// <param name="merge">Whether CE should merge instead of replacing the current table.</param>
    /// <returns>The protected binding outcome.</returns>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [LuaGlobal("loadTable")]
    [RequiresPluginEnabled]
    public static partial LuaOperationStatus TryLoad(string path, bool merge);

    /// <summary>Saves the current Cheat Engine table to a file.</summary>
    /// <param name="path">The opaque path text passed directly to CE.</param>
    /// <returns>The protected binding outcome.</returns>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [LuaGlobal("saveTable")]
    [RequiresPluginEnabled]
    public static partial LuaOperationStatus TrySave(string path);
}
