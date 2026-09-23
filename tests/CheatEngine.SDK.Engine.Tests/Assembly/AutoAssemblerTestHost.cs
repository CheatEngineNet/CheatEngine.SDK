using System.Globalization;
using System.Text;

using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Tests.Assembly;

/// <summary>
///     A Lua double of Cheat Engine's <c>autoAssemble</c> and <c>autoAssembleCheck</c> that keeps CE 7.7's documented
///     shapes: apply returns <c>(true, disableInfo[, warnings])</c> or <c>(false, detail)</c>, disable takes the script
///     and the disable-info table, and the check returns <c>(true)</c> or <c>(false, message)</c>. The script text selects
///     the behavior; argument counts and calls are recorded in globals.
/// </summary>
/// <remarks>
///     Scripts: <c>success</c>, <c>apply-false</c> (rejected with a detail string), <c>apply-reject-french</c>,
///     <c>apply-reject-long</c> (the detail in <c>aa_long_detail</c>), <c>apply-reject-nonstring</c>,
///     <c>apply-alloc-impossible</c> (syntax accepted, allocation refused), <c>apply-raise</c>,
///     <c>apply-warnings</c> (warnings in <c>aa_warnings</c>), <c>apply-true-no-table</c>, <c>apply-non-boolean</c>,
///     <c>apply-switch-target</c> (the target changes during the call), <c>disable-false</c>, <c>disable-raise</c>.
///     A successful apply returns <c>aa_disable_info</c> when it is set, otherwise a fresh table.
/// </remarks>
internal static class AutoAssemblerTestHost
{
	public const string RejectionDetail = "rejected by host";

	private static ReadOnlySpan<byte> Model => """
	                                           auto_assembler_apply_count = 0
	                                           auto_assembler_disable_count = 0
	                                           auto_assembler_disable_received_info = false
	                                           auto_assembler_apply_arguments = -1
	                                           auto_assembler_disable_arguments = -1
	                                           auto_assembler_check_count = 0
	                                           auto_assembler_check_arguments = -1
	                                           auto_assembler_check_enable = nil
	                                           aa_warnings = nil
	                                           aa_disable_info = nil
	                                           aa_long_detail = nil

	                                           autoAssemble = function(...)
	                                             local count = select("#", ...)
	                                             local script, disableInfo = ...
	                                             if count == 1 then
	                                               auto_assembler_apply_arguments = count
	                                               if script == "apply-false" then return false, "rejected by host" end
	                                               if script == "apply-reject-french" then return false, "rejeté par l'hôte" end
	                                               if script == "apply-reject-long" then return false, aa_long_detail end
	                                               if script == "apply-reject-nonstring" then return false, 42 end
	                                               if script == "apply-alloc-impossible" then return false, "allocation failure for newmem" end
	                                               if script == "apply-raise" then error("apply failure") end
	                                               if script == "apply-non-boolean" then return 1, {} end
	                                               auto_assembler_apply_count = auto_assembler_apply_count + 1
	                                               if script == "apply-true-no-table" then return true, nil end
	                                               if script == "apply-switch-target" then fake_target_pid = 0 end
	                                               local info = aa_disable_info or { sequence = auto_assembler_apply_count }
	                                               if script == "apply-warnings" then return true, info, aa_warnings end
	                                               return true, info
	                                             end

	                                             auto_assembler_disable_arguments = count
	                                             auto_assembler_disable_count = auto_assembler_disable_count + 1
	                                             auto_assembler_disable_received_info = type(disableInfo) == "table"
	                                             if script == "disable-false" then return false end
	                                             if script == "disable-raise" then error("disable failure") end
	                                             return true
	                                           end

	                                           autoAssembleCheck = function(...)
	                                             auto_assembler_check_count = auto_assembler_check_count + 1
	                                             auto_assembler_check_arguments = select("#", ...)
	                                             local script, enable = ...
	                                             auto_assembler_check_enable = enable
	                                             if script == "check-false" then return false, "syntax error at line 1" end
	                                             if script == "check-raise" then error("check failure") end
	                                             if script == "check-non-boolean" then return "yes" end
	                                             return true
	                                           end
	                                           """u8;

	/// <summary>Installs a qualified local target, then the Auto Assembler double.</summary>
	public static void Install(LuaState state)
	{
		FakeHost.InstallQualifiedLocalTarget(state);
		EngineTest.Run(state, Model);
	}

	/// <summary>Sets the current target PID the fixture reports (0 means no target).</summary>
	public static void SelectTarget(LuaState state, int processId)
	{
		EngineTest.Run(state, Encoding.UTF8.GetBytes(
			"fake_target_pid = " + processId.ToString(CultureInfo.InvariantCulture)));
	}

	/// <summary>Reads an integer global.</summary>
	public static long ReadCounter(LuaState state, string name)
	{
		using LuaFrame frame = new(state);
		Assert.True(state.TryGetGlobal(Encoding.UTF8.GetBytes(name)).IsOk);
		return EngineTest.ReadInteger(state, -1);
	}

	/// <summary>Reads a boolean global.</summary>
	public static bool ReadBoolean(LuaState state, string name)
	{
		using LuaFrame frame = new(state);
		Assert.True(state.TryGetGlobal(Encoding.UTF8.GetBytes(name)).IsOk);
		Assert.Equal(LuaType.Boolean, state.TypeOf(-1));
		return state.ToBoolean(-1);
	}

	/// <summary>The default disable-info tracker: roots the table on top.</summary>
	public static LuaRef CreateDisableInfo(LuaState state)
	{
		return state.CreateRef();
	}
}
