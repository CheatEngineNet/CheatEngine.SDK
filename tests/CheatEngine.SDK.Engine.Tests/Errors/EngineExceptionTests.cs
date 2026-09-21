using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Tests.Errors;

/// <summary>Contract tests for the stable Engine error taxonomy.</summary>
public sealed class EngineExceptionTests
{
    [Fact]
    public void Operation_failure_exposes_its_category_operation_and_inner_cause()
    {
        var cause = new InvalidOperationException("The target address was unreadable.");
        var exception =
            new EngineOperationFailedException("TargetMemory.ReadInt32", "The read could not complete.", cause);

        Assert.Equal(EngineFailureKind.ExpectedOperationFailure, exception.Kind);
        Assert.Equal("TargetMemory.ReadInt32", exception.Operation);
        Assert.Equal("The read could not complete.", exception.Message);
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public void Capability_failure_keeps_the_public_identifier_out_of_its_binding_details()
    {
        var exception = new EngineCapabilityUnavailableException("MemoryProtection.Query");

        Assert.Equal(EngineFailureKind.CapabilityUnavailable, exception.Kind);
        Assert.Equal("MemoryProtection.Query", exception.Capability);
        Assert.Equal("The Cheat Engine capability 'MemoryProtection.Query' is unavailable.", exception.Message);
        Assert.DoesNotContain("VirtualQueryEx", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Global_failure_is_distinct_from_a_capability_that_is_unavailable_before_binding()
    {
        var exception = new EngineGlobalUnavailableException("TargetMemory.ReadInt32");

        Assert.Equal(EngineFailureKind.GlobalUnavailable, exception.Kind);
        Assert.Equal("TargetMemory.ReadInt32", exception.Operation);
        Assert.Equal("The required binding global for Cheat Engine operation 'TargetMemory.ReadInt32' is unavailable.",
            exception.Message);
        Assert.DoesNotContain("readInteger", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Protected_lua_failure_exposes_status_but_not_the_inner_lua_text()
    {
        var cause = new LuaException("internal Lua stack: target process detached");
        var exception = new EngineLuaException("TargetMemory.ReadInt32", LuaStatus.RuntimeError);

        var withCause = new EngineLuaException("TargetMemory.ReadInt32", LuaStatus.RuntimeError,
            "The protected call failed.", cause);

        Assert.Equal(EngineFailureKind.ProtectedLuaFailure, exception.Kind);
        Assert.Equal("TargetMemory.ReadInt32", exception.Operation);
        Assert.Equal(LuaStatus.RuntimeError, exception.Status);
        Assert.Equal("The protected Lua operation 'TargetMemory.ReadInt32' failed with status LUA_ERRRUN.",
            exception.Message);
        Assert.DoesNotContain("internal Lua stack", exception.Message, StringComparison.Ordinal);
        Assert.Same(cause, withCause.InnerException);
        Assert.DoesNotContain("internal Lua stack", withCause.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Protected_lua_failure_rejects_a_success_status()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new EngineLuaException("TargetMemory.ReadInt32", LuaStatus.Ok));

        Assert.Equal("status", exception.ParamName);
    }

    [Fact]
    public void Binding_failure_distinguishes_a_contract_problem_from_a_missing_capability()
    {
        var exception = new EngineBindingException("MemoryScalars.ReadInt32");

        Assert.Equal(EngineFailureKind.BindingFailure, exception.Kind);
        Assert.Equal("MemoryScalars.ReadInt32", exception.Binding);
        Assert.Equal("The Cheat Engine binding 'MemoryScalars.ReadInt32' cannot uphold its contract.",
            exception.Message);
    }

    [Fact]
    public void Marshalling_failure_distinguishes_an_invalid_result_and_preserves_its_cause()
    {
        var cause = new InvalidCastException("A Lua table cannot be read as an integer.");
        var exception = new EngineMarshallingException("TargetMemory.ReadInt32", EngineMarshallingDirection.Result,
            "a 32-bit signed integer", "a table", "The target returned an invalid value.", cause);

        Assert.Equal(EngineFailureKind.MarshallingFailure, exception.Kind);
        Assert.Equal("TargetMemory.ReadInt32", exception.Operation);
        Assert.Equal(EngineMarshallingDirection.Result, exception.Direction);
        Assert.Equal("a 32-bit signed integer", exception.Expected);
        Assert.Equal("a table", exception.Actual);
        Assert.Equal("The target returned an invalid value.", exception.Message);
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public void Marshalling_failure_rejects_an_undefined_direction()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EngineMarshallingException("TargetMemory.ReadInt32", (EngineMarshallingDirection)42,
                "a 32-bit signed integer", "a table"));

        Assert.Equal("direction", exception.ParamName);
    }

    [Fact]
    public void Target_identity_failure_preserves_the_observed_mismatch_category()
    {
        TargetProcessIncarnation observedIncarnation = new(43, 2);
        var observed = TargetSelectionObservation.Qualified(observedIncarnation);
        var check = new TargetIdentityCheck(TargetIdentityCheckKind.TargetChanged, observed);
        var exception = new EngineTargetIdentityException("TargetMemoryDeallocate", check);

        Assert.Equal(EngineFailureKind.TargetIdentityMismatch, exception.Kind);
        Assert.Equal(TargetIdentityCheckKind.TargetChanged, exception.Check.Kind);
        Assert.True(exception.Check.Observed.Incarnation.HasValue);
        Assert.Equal(observedIncarnation, exception.Check.Observed.Incarnation.GetValueOrDefault());
    }

    [Fact]
    public void Target_identity_failure_kinds_append_without_reassigning_the_existing_failure_values()
    {
        Assert.Equal(6, (int)EngineFailureKind.TargetIdentityUnavailable);
        Assert.Equal(7, (int)EngineFailureKind.TargetIdentityMismatch);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Public_identifiers_must_not_be_null_or_empty(string? value)
    {
        Assert.Throws<ArgumentException>(() => new EngineOperationFailedException(value!));
        Assert.Throws<ArgumentException>(() => new EngineCapabilityUnavailableException(value!));
        Assert.Throws<ArgumentException>(() => new EngineGlobalUnavailableException(value!));
        Assert.Throws<ArgumentException>(() => new EngineBindingException(value!));
        Assert.Throws<ArgumentException>(() => new EngineMarshallingException("TargetMemory.ReadInt32",
            EngineMarshallingDirection.Result, value!, "a nil value"));
    }
}
