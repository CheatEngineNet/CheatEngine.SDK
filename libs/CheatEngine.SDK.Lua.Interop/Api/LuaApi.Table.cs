using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Lua.Interop.Api;

public static partial class LuaApi
{
	/// <summary>
	///     The function-pointer table. One field per export, named like the export; the fields and the code that fills
	///     them live next to the forwarding methods, in the partial file of their area.
	/// </summary>
	/// <remarks>
	///     <c>LayoutKind.Auto</c> because native code never sees the struct and it is declared across several files, where
	///     a sequential layout would have no defined field order (CS0282). A struct rather than loose static fields so
	///     that a bind attempt fills a local copy and publishes it only when it is complete.
	/// </remarks>
	[StructLayout(LayoutKind.Auto)]
	internal partial struct Table
	{
		/// <summary>Resolves every export of the table; names that are not found are recorded by <paramref name="exports" />.</summary>
		internal void Load(ref ExportResolver exports)
		{
			LoadState(ref exports);
			LoadStack(ref exports);
			LoadAccess(ref exports);
			LoadOperators(ref exports);
			LoadPush(ref exports);
			LoadGet(ref exports);
			LoadSet(ref exports);
			LoadCalls(ref exports);
			LoadCoroutines(ref exports);
			LoadMisc(ref exports);
			LoadDebug(ref exports);
			LoadAuxiliary(ref exports);
			LoadLibraries(ref exports);
		}
	}
}
