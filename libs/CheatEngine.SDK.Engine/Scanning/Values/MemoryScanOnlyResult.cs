namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>The SDK-internal result of reading <c>MemScan.getOnlyResult()</c> for the first-found AOB route.</summary>
internal enum MemoryScanOnlyResult : byte
{
	/// <summary>No read has been observed.</summary>
	Unknown = 0,

	/// <summary>CE returned an integer address.</summary>
	Found = 1,

	/// <summary>CE returned no value or <c>nil</c>: nothing was found.</summary>
	NotFound = 2,

	/// <summary>CE returned a value that is not an integer (a float, string, boolean or object).</summary>
	InvalidResult = 3,

	/// <summary>The protected call raised.</summary>
	LuaFailure = 4
}
