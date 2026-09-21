using System.Reflection;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Assembly;

/// <summary>
///     Exercises the SDK Auto Assembler owner against a Lua double that preserves CE's two-call disable-info protocol.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class AutoAssemblerPatcherTests
{
    [Fact]
    public void Apply_on_success_retains_the_disable_info_until_Release_completes()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var patch = AutoAssemblerPatcher.Apply("success");

        Assert.True(patch.IsEnabled);
        Assert.False(patch.IsDisposed);
        Assert.False(patch.RequiresManualRecovery);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_apply_count"));
        Assert.Equal(0, ReadCounter(scope.State, "auto_assembler_disable_count"));

        patch.Release();

        Assert.True(patch.IsDisposed);
        Assert.False(patch.IsEnabled);
        Assert.False(patch.RequiresManualRecovery);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.True(ReadBoolean(scope.State, "auto_assembler_disable_received_info"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void TryApply_when_CE_rejects_the_script_returns_false_without_an_owner()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var applied = AutoAssemblerPatcher.TryApply("apply-false", out var patch);

        Assert.False(applied);
        Assert.Null(patch);
        Assert.Equal(0, ReadCounter(scope.State, "auto_assembler_apply_count"));
        Assert.Equal(0, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Apply_when_CE_rejects_the_script_throws_the_stable_expected_failure()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var exception = Assert.Throws<EngineOperationFailedException>(() => AutoAssemblerPatcher.Apply("apply-false"));

        Assert.Equal("AutoAssemblerApply", exception.Operation);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Apply_when_the_protected_CE_call_fails_preserves_the_Lua_failure_and_restores_the_stack()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var exception = Assert.Throws<EngineLuaException>(() => AutoAssemblerPatcher.Apply("apply-raise"));

        Assert.Equal("AutoAssemblerApply", exception.Operation);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Dispose_after_a_successful_disable_is_idempotent_and_never_replays_disable()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("success");

        patch.Dispose();
        patch.Dispose();

        Assert.True(patch.IsDisposed);
        Assert.False(patch.RequiresManualRecovery);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Release_when_CE_returns_false_marks_manual_recovery_releases_the_LuaRef_and_never_retries()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("disable-false");
        var disableInfo = GetDisableInfo(patch);

        var exception = Assert.Throws<EngineOperationFailedException>(patch.Release);

        Assert.Equal("AutoAssemblerDisable", exception.Operation);
        Assert.True(patch.IsDisposed);
        Assert.False(patch.IsEnabled);
        Assert.True(patch.RequiresManualRecovery);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));

        patch.Dispose();

        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Dispose_when_the_disable_call_raises_marks_manual_recovery_releases_the_LuaRef_and_never_retries()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("disable-raise");
        var disableInfo = GetDisableInfo(patch);

        patch.Dispose();
        patch.Dispose();

        Assert.True(patch.IsDisposed);
        Assert.True(patch.RequiresManualRecovery);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Dispose_after_disable_and_reenable_does_not_route_a_stale_disable_info_into_the_new_lifecycle()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        HostScope firstScope = new(state);
        InstallAutoAssembler(firstScope.State);
        var patch = AutoAssemblerPatcher.Apply("success");
        var disableInfo = GetDisableInfo(patch);

        firstScope.Dispose();
        Assert.False(patch.IsEnabled);
        using HostScope secondScope = new(state);

        patch.Dispose();

        Assert.True(patch.IsDisposed);
        Assert.True(patch.RequiresManualRecovery);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(0, ReadCounter(secondScope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, secondScope.State.Top);
    }

    private static LuaRef GetDisableInfo(AutoAssemblerPatch patch)
    {
        var field = typeof(AutoAssemblerPatch).GetField("_disableInfo",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<LuaRef>(field.GetValue(patch));
    }

    private static void InstallAutoAssembler(CheatEngine.SDK.Lua.State.LuaState state)
    {
        EngineTest.Run(state, """
                              auto_assembler_apply_count = 0
                              auto_assembler_disable_count = 0
                              auto_assembler_disable_received_info = false

                              autoAssemble = function(script, disableInfo)
                                if disableInfo == nil then
                                  if script == "apply-false" then return false, nil end
                                  if script == "apply-raise" then error("apply failure") end
                                  auto_assembler_apply_count = auto_assembler_apply_count + 1
                                  return true, { sequence = auto_assembler_apply_count }
                                end

                                auto_assembler_disable_count = auto_assembler_disable_count + 1
                                auto_assembler_disable_received_info = type(disableInfo) == "table"
                                if script == "disable-false" then return false end
                                if script == "disable-raise" then error("disable failure") end
                                return true
                              end
                              """u8);
    }

    private static long ReadCounter(CheatEngine.SDK.Lua.State.LuaState state, string name)
    {
        using var frame = new CheatEngine.SDK.Lua.State.LuaFrame(state);
        Assert.True(state.TryGetGlobal(System.Text.Encoding.UTF8.GetBytes(name)).IsOk);
        return EngineTest.ReadInteger(state, -1);
    }

    private static bool ReadBoolean(CheatEngine.SDK.Lua.State.LuaState state, string name)
    {
        using var frame = new CheatEngine.SDK.Lua.State.LuaFrame(state);
        Assert.True(state.TryGetGlobal(System.Text.Encoding.UTF8.GetBytes(name)).IsOk);
        Assert.Equal(CheatEngine.SDK.Lua.State.LuaType.Boolean, state.TypeOf(-1));
        return state.ToBoolean(-1);
    }
}
