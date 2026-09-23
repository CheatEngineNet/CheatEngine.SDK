namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

/// <summary>Spec-file texts reused across the parser, generator and end-to-end tests.</summary>
internal static class SpecSources
{
	/// <summary>
	///     The CE 7.7 evidence keys every spec file with entries declares after <c>namespace</c> and <c>type</c>
	///     (CESDK3003), with a fixture provenance. Each entry then also needs a <c>nil</c> key.
	/// </summary>
	public const string Ce77 =
		"contract: ce77\nprovenance: ExactInstalledFile: CE 7.7 celua.txt test fixture\nminimum-ce: 7.7.0.10621\narchitecture: x64\nthread: unknown\nownership: none\n";

	/// <summary>A ce77 header block for <paramref name="ns" /> and <paramref name="type" />, blank line included.</summary>
	public static string Ce77Header(string ns, string type)
	{
		return "namespace: " + ns + "\ntype: " + type + "\n" + Ce77 + "\n";
	}

	/// <summary>
	///     The nominal case: two scalar reads (Try form) and two scalar writes (throwing form, boolean return), sharing
	///     no global.
	/// </summary>
	public const string Memory = """
	                             namespace: Demo.Engine.Generated
	                             type: MemoryScalars
	                             contract: ce77
	                             provenance: ExactInstalledFile: CE 7.7 celua.txt scalar memory globals
	                             minimum-ce: 7.7.0.10621
	                             architecture: x64
	                             thread: unknown
	                             ownership: none

	                             global: readInteger
	                             method: TryReadInt32
	                             form: try
	                             arg: address:address
	                             fixed: boolean:true
	                             result: value:int32
	                             nil: absence
	                             doc: Reads a 32-bit integer from the target process at the given address.

	                             global: writeInteger
	                             method: WriteInt32
	                             form: throwing
	                             arg: address:address
	                             arg: value:int32
	                             return: boolean
	                             nil: none
	                             doc: Writes a 32-bit integer to the target process at the given address.

	                             global: readQword
	                             method: TryReadInt64
	                             form: try
	                             arg: address:address
	                             result: value:int64
	                             nil: absence
	                             doc: Reads a 64-bit integer from the target process at the given address.

	                             global: writeQword
	                             method: WriteInt64
	                             form: throwing
	                             arg: address:address
	                             arg: value:int64
	                             return: boolean
	                             nil: none
	                             doc: Writes a 64-bit integer to the target process at the given address.
	                             """;

	/// <summary>A minimal, single-entry spec: a Try form with one argument and one result.</summary>
	public const string SingleTry = """
	                                namespace: Demo.One
	                                type: One
	                                contract: ce77
	                                provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
	                                minimum-ce: 7.7.0.10621
	                                architecture: x64
	                                thread: unknown
	                                ownership: none

	                                global: readInteger
	                                method: TryReadInt32
	                                form: try
	                                arg: address:address
	                                result: value:int32
	                                nil: none
	                                doc: Reads a 32-bit integer.
	                                """;

	/// <summary>Two wrapper forms (Try and throwing) of the same global: they must share one cache field.</summary>
	public const string SharedGlobal = """
	                                   namespace: Demo.Shared
	                                   type: Shared
	                                   contract: ce77
	                                   provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
	                                   minimum-ce: 7.7.0.10621
	                                   architecture: x64
	                                   thread: unknown
	                                   ownership: none

	                                   global: readInteger
	                                   method: TryReadInt32
	                                   form: try
	                                   arg: address:address
	                                   result: value:int32
	                                   nil: none
	                                   doc: Reads a 32-bit integer, reporting failure.

	                                   global: readInteger
	                                   method: ReadInt32
	                                   form: throwing
	                                   arg: address:address
	                                   return: int32
	                                   nil: none
	                                   doc: Reads a 32-bit integer, raising on failure.
	                                   """;

	/// <summary>
	///     A minimal, unrelated single-entry spec (a different type and global from <see cref="SingleTry" />), for
	///     two-file pipeline tests.
	/// </summary>
	public const string BeepOnly = """
	                               namespace: Demo.Other
	                               type: Other
	                               contract: ce77
	                               provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
	                               minimum-ce: 7.7.0.10621
	                               architecture: x64
	                               thread: unknown
	                               ownership: none

	                               global: beep
	                               method: Beep
	                               form: throwing
	                               nil: none
	                               doc: Calls a global with no arguments and no result.
	                               """;

	/// <summary>Every wrapper shape the end-to-end test exercises: both Try results, both throwing forms, and a void call.</summary>
	public const string EndToEnd = """
	                               namespace: Demo.EndToEnd
	                               type: MemoryScalars
	                               contract: ce77
	                               provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
	                               minimum-ce: 7.7.0.10621
	                               architecture: x64
	                               thread: unknown
	                               ownership: none

	                               global: readInteger
	                               method: TryReadInt32
	                               form: try
	                               arg: address:address
	                               fixed: boolean:true
	                               result: value:int32
	                               nil: none
	                               doc: Reads a 32-bit integer from the target process at the given address.

	                               global: writeInteger
	                               method: WriteInt32
	                               form: throwing
	                               arg: address:address
	                               arg: value:int32
	                               return: boolean
	                               nil: none
	                               doc: Writes a 32-bit integer to the target process at the given address.

	                               global: readQword
	                               method: TryReadInt64
	                               form: try
	                               arg: address:address
	                               result: value:int64
	                               nil: none
	                               doc: Reads a 64-bit integer from the target process at the given address.

	                               global: writeQword
	                               method: WriteInt64
	                               form: throwing
	                               arg: address:address
	                               arg: value:int64
	                               return: boolean
	                               nil: none
	                               doc: Writes a 64-bit integer to the target process at the given address.

	                               global: beep
	                               method: Beep
	                               form: throwing
	                               nil: none
	                               doc: Calls a global with no arguments and no result.
	                               """;
}
