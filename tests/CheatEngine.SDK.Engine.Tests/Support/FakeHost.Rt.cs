using System.Globalization;
using System.Text;

using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Tests.Support;

/// <summary>
///     S-RT stand-ins for Cheat Engine's runtime and target globals, with the values CE 7.7.0.10621 x64 reported in
///     spike C3 (Lua-only host observation, 2026-09-22; design input, never qualification evidence).
/// </summary>
/// <remarks>
///     Every stand-in reads a plain <c>rt_*</c> Lua global, so a test changes one fact with a one-line chunk such as
///     <c>rt_pointer_size = 4</c> and can replace any stand-in by redefining its function. Other lots' fixtures that model
///     a qualified local target reuse <see cref="LocalTargetBackendChunk" /> (shared-contracts section 6, OI-2).
/// </remarks>
internal static partial class FakeHost
{
	/// <summary>
	///     The CE 7.7 file version as <c>getCheatEngineFileVersion</c> packs it: major, minor, release and build, 16
	///     bits each (7.7.0.10621).
	/// </summary>
	public const long Ce77PackedFileVersion = 0x7_0007_0000_297DL;

	/// <summary>
	///     Defines <c>isConnectedToCEServer</c> returning <see langword="false" />, as CE 7.7 does for a local target
	///     (spike C3 D5). Every fixture that models a qualified local target needs it, because
	///     <c>TargetSelection</c> refuses local incarnation evidence when the probe is absent.
	/// </summary>
	public static ReadOnlySpan<byte> LocalTargetBackendChunk => "function isConnectedToCEServer() return false end"u8;

	/// <summary>
	///     The Lua global names that the SDK's runtime and target observations are allowed to read (audit A17-18, Q45).
	/// </summary>
	public static IReadOnlyList<string> ReadOnlyRuntimeGlobals
	{
		get;
	} =
	[
		"getOpenedProcessID", "isConnectedToCEServer", "targetIs64Bit", "targetIsX86", "targetIsArm",
		"targetIsAndroid", "getABI", "getPointerSize", "getCheatEngineFileVersion", "getSystemArchitecture",
		"cheatEngineIs64Bit", "getOperatingSystem"
	];

	/// <summary>
	///     Lua globals that change Cheat Engine or the target, or load a driver, which no runtime observation may call
	///     (audit A17-18, Q45).
	/// </summary>
	public static IReadOnlyList<string> ForbiddenRuntimeGlobals
	{
		get;
	} =
	[
		"openProcess", "openFileAsProcess", "setPointerSize", "setAssemblerMode", "dbk_initialize",
		"dbk_initialized", "dbvm_initialize", "dbvm_initialized", "dbk_useKernelmodeOpenProcess", "pause",
		"unpause"
	];

	/// <summary>
	///     Installs the target facts CE 7.7 reported for the x64 Tutorial: x86 family, 64-bit, not ARM, not Android,
	///     Windows ABI, pointer size 8, no CEServer connection (spike C3 D2/D5).
	/// </summary>
	public static void InstallCe77X64TargetFacts(LuaState state, int processId)
	{
		InstallTargetFacts(state, processId, true, 8);
	}

	/// <summary>
	///     Installs the target facts CE 7.7 reported for the i386 tutorial: x86 family, not 64-bit, pointer size 4
	///     (spike C3 D2).
	/// </summary>
	public static void InstallCe77X86TargetFacts(LuaState state, int processId)
	{
		InstallTargetFacts(state, processId, false, 4);
	}

	/// <summary>
	///     Installs the host facts CE 7.7.0.10621 x64 reported: the packed file version with its table, host
	///     architecture code 1 (x86_64), a 64-bit Cheat Engine and operating-system code 0 (spike C3 D5).
	/// </summary>
	public static void InstallCe77HostFacts(LuaState state)
	{
		string source =
			"rt_file_version = " + Ce77PackedFileVersion.ToString(CultureInfo.InvariantCulture) + "\n" +
			"""
			rt_file_version_table = { major = 7, minor = 7, release = 0, build = 10621, FileVersion = '7.7.0.10621',
			                          ProductVersion = '7.7', CompanyName = 'Cheat Engine' }
			rt_system_architecture = 1
			rt_cheat_engine_is_64bit = true
			rt_operating_system = 0
			function getCheatEngineFileVersion() return rt_file_version, rt_file_version_table end
			function getSystemArchitecture() return rt_system_architecture end
			function cheatEngineIs64Bit() return rt_cheat_engine_is_64bit end
			function getOperatingSystem() return rt_operating_system end
			""";
		EngineTest.Run(state, Encoding.UTF8.GetBytes(source));
	}

	/// <summary>
	///     Installs the CE 7.7 x64 host and target facts as recording functions that also assert they receive no
	///     argument, makes every <see cref="ForbiddenRuntimeGlobals" /> entry record and raise, and sets a <c>_G</c>
	///     metatable whose <c>__index</c> records every other global lookup. <see cref="RecordedRuntimeGlobals" /> reads
	///     the record back.
	/// </summary>
	public static void InstallRecordingRuntimeGlobals(LuaState state, int processId)
	{
		InstallCe77HostFacts(state);
		InstallCe77X64TargetFacts(state, processId);
		StringBuilder source = new();
		source.Append("rt_calls = {}\nrt_arity_violations = {}\n");
		source.Append("local function record(name) rt_calls[#rt_calls + 1] = name end\n");
		foreach (string name in ReadOnlyRuntimeGlobals)
		{
			source.Append("do local original = ").Append(name).Append('\n')
				.Append("  ").Append(name).Append(" = function(...)\n")
				.Append("    record('").Append(name).Append("')\n")
				.Append("    if select('#', ...) ~= 0 then rt_arity_violations[#rt_arity_violations + 1] = '")
				.Append(name).Append("' end\n")
				.Append("    return original()\n")
				.Append("  end\nend\n");
		}

		foreach (string name in ForbiddenRuntimeGlobals)
		{
			source.Append(name).Append(" = function() record('").Append(name)
				.Append("') error('forbidden runtime global called: ").Append(name).Append("') end\n");
		}

		source.Append("setmetatable(_G, { __index = function(_, key) record('?' .. tostring(key)) return nil end })\n");
		EngineTest.Run(state, Encoding.UTF8.GetBytes(source.ToString()));
	}

	/// <summary>Reads the global names recorded by <see cref="InstallRecordingRuntimeGlobals" />, in call order.</summary>
	public static IReadOnlyList<string> RecordedRuntimeGlobals(LuaState state)
	{
		return ReadStringSequence(state, "rt_calls"u8);
	}

	/// <summary>Reads the names of recorded read-only globals that were called with at least one argument.</summary>
	public static IReadOnlyList<string> RecordedArityViolations(LuaState state)
	{
		return ReadStringSequence(state, "rt_arity_violations"u8);
	}

	private static void InstallTargetFacts(LuaState state, int processId, bool is64Bit, int pointerSize)
	{
		string source =
			"rt_process_id = " + processId.ToString(CultureInfo.InvariantCulture) + "\n" +
			"rt_is_64bit = " + (is64Bit ? "true" : "false") + "\n" +
			"rt_pointer_size = " + pointerSize.ToString(CultureInfo.InvariantCulture) + "\n" +
			"""
			rt_ceserver = false
			rt_is_x86 = true
			rt_is_arm = false
			rt_is_android = false
			rt_abi = 0
			function getOpenedProcessID() return rt_process_id end
			function isConnectedToCEServer() return rt_ceserver end
			function targetIs64Bit() return rt_is_64bit end
			function targetIsX86() return rt_is_x86 end
			function targetIsArm() return rt_is_arm end
			function targetIsAndroid() return rt_is_android end
			function getABI() return rt_abi end
			function getPointerSize() return rt_pointer_size end
			""";
		EngineTest.Run(state, Encoding.UTF8.GetBytes(source));
	}

	private static List<string> ReadStringSequence(LuaState state, ReadOnlySpan<byte> global)
	{
		List<string> values = [];
		using LuaFrame frame = new(state);
		Assert.True(state.TryGetGlobal(global).IsOk);
		Assert.True(state.IsTable(-1));
		int table = state.AbsoluteIndex(-1);
		for (long index = 1;; index++)
		{
			if (state.RawGetIndex(table, index) == LuaType.Nil)
			{
				break;
			}

			values.Add(EngineTest.ReadString(state, -1));
			state.Pop(1);
		}

		return values;
	}
}
