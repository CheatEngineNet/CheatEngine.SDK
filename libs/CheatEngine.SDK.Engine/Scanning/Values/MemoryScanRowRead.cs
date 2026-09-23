namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>The SDK-internal result of reading one found-list row for a bounded AOB route.</summary>
internal enum MemoryScanRowRead : byte
{
	/// <summary>No row read has been observed.</summary>
	Unknown = 0,

	/// <summary>CE returned the row's address text and it parsed as a target address.</summary>
	Read = 1,

	/// <summary>The protected row call raised.</summary>
	LuaFailure = 2,

	/// <summary>CE returned something that is not hexadecimal address text.</summary>
	InvalidResult = 3
}
