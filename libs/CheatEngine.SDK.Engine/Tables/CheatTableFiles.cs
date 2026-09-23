using System;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Tables;

/// <summary>Protected bindings for loading and saving Cheat Engine table files.</summary>
/// <remarks>
///     <para>
///         CE 7.7.0.10621 x64 <c>celua.txt</c> documents <c>loadTable(filename, merge)</c> and <c>saveTable(filename)</c>
///         (lines 32-35). This API treats its path argument as opaque host input: it normalizes no path, applies no
///         file-root policy, and owns no file or CE object. It only preserves the exact Lua call shapes, protected failure
///         category, attach-epoch-aware function cache, and stack restoration. The trust policy (which paths may be
///         loaded, and whether a table may be loaded at all) belongs to the application; converting a file into a stream
///         must never be used to bypass it.
///     </para>
///     <para>
///         <b>Overload policy.</b> Only the file overloads are projected. The file overload of <c>loadTable</c> has no
///         option to suppress the Lua-script dialog, so a table that contains Lua scripts may prompt the user or execute
///         Lua. The stream overloads (<c>loadTable(stream, merge, ignoreluascriptdialog)</c>,
///         <c>saveTable(stream, ...)</c>)
///         and the <c>protect</c> / <c>dontDeactivateDesignerForms</c> options of <c>saveTable</c> are not projected until
///         a Cheat Engine stream projection exists; the Lua surface catalogue records them as deferred.
///     </para>
///     <para>
///         <b>Re-entrancy.</b> While <see cref="TryLoad" /> runs on a thread, the typed address-list mutations of
///         <see cref="AddressListMutations" /> issued on that thread (for example from a script of the table being loaded)
///         are refused with <see cref="MemoryRecordMutationProblem.TableLoadInProgress" /> before any Lua call: the record
///         identifiers they would resolve are being replaced.
///     </para>
/// </remarks>
public static partial class CheatTableFiles
{
	// Nesting depth of TryLoad on this thread; restored on every exit path of TryLoad.
	[ThreadStatic] private static int t_loadDepth;

	/// <summary>Gets whether <see cref="TryLoad" /> is running on the calling thread.</summary>
	internal static bool IsLoadInProgressOnCurrentThread => t_loadDepth != 0;

	/// <summary>Loads a Cheat Engine table file, optionally merging it into the current address list.</summary>
	/// <param name="path">The opaque path text passed directly to CE.</param>
	/// <param name="merge">Whether CE should merge instead of replacing the current table.</param>
	/// <returns>The protected binding outcome.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	/// <remarks>
	///     Calls <c>loadTable</c> with exactly the path and the merge flag. A table that contains Lua scripts may prompt or
	///     execute Lua during the call; address-list mutations issued from the same thread meanwhile are refused.
	/// </remarks>
	[RequiresPluginEnabled]
	public static LuaOperationStatus TryLoad(string path, bool merge)
	{
		t_loadDepth++;
		try
		{
			return TryLoadCore(path, merge);
		}
		finally
		{
			t_loadDepth--;
		}
	}

	/// <summary>Saves the current Cheat Engine table to a file.</summary>
	/// <param name="path">The opaque path text passed directly to CE.</param>
	/// <returns>The protected binding outcome.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[LuaGlobal("saveTable")]
	[RequiresPluginEnabled]
	public static partial LuaOperationStatus TrySave(string path);

	[LuaGlobal("loadTable")]
	private static partial LuaOperationStatus TryLoadCore(string path, bool merge);
}
