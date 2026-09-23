using System;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>The Cheat Engine host facts that <c>RuntimeHostOperations.ObserveHost</c> reads, one flag per Lua global.</summary>
[Flags]
internal enum HostProbeFacts : byte
{
	/// <summary>No fact.</summary>
	None = 0,

	/// <summary><c>getCheatEngineFileVersion</c>.</summary>
	FileVersion = 1,

	/// <summary><c>getSystemArchitecture</c>.</summary>
	SystemArchitecture = 2,

	/// <summary><c>cheatEngineIs64Bit</c>.</summary>
	CheatEngineBitness = 4,

	/// <summary><c>getOperatingSystem</c>.</summary>
	OperatingSystem = 8
}
