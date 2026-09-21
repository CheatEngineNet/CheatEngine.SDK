using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Inspection;

/// <summary>Fixture-backed contracts for the user-symbol registry and address-name Lua globals.</summary>
[Trait("Category", "NativeLua")]
public sealed class SymbolRegistryTests
{
    [Fact]
    public void TryGetName_forwards_only_the_source_mapped_address()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          getNameFromAddress = function(address, second)
                            if address == 0x140001000 and second == nil then
                              return 'one-argument-default'
                            end
                            return 42
                          end
                          """u8);

        var top = L.Top;
        var status = SymbolRegistry.TryGetName(0x140001000UL, out var defaultName);

        Assert.Equal(LuaOperationStatusKind.Success, status.Kind);
        Assert.Equal("one-argument-default", defaultName);
        Assert.Equal(top, L.Top);

    }

    [Fact]
    public void Register_and_unregister_forward_typed_values_and_preserve_the_stack()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          registered = nil
                          removed = nil
                          registerSymbol = function(name, address, doNotSave)
                            registered = { name = name, address = address, doNotSave = doNotSave }
                          end
                          unregisterSymbol = function(name) removed = name end
                          """u8);

        var top = L.Top;
        SymbolName name = new("Player.Health");
        var registration = SymbolRegistry.Register(name, 0x140001234UL,
            new SymbolRegistrationOptions(DoNotSave: true));

        Assert.Equal(LuaOperationStatusKind.Success, registration.Kind);
        Assert.Equal(top, L.Top);
        EngineTest.Run(L,
            "assert(registered.name == 'Player.Health' and registered.address == 0x140001234 and registered.doNotSave)"u8);
        Assert.Equal(top, L.Top);

        var release = SymbolRegistry.Unregister(name);

        Assert.Equal(LuaOperationStatusKind.Success, release.Kind);
        Assert.Equal(top, L.Top);
        EngineTest.Run(L, "assert(removed == 'Player.Health')"u8);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void Registry_distinguishes_missing_globals_lua_failures_and_invalid_name_results()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var top = L.Top;

        var status = SymbolRegistry.TryGetName(0x140001000UL, out var unavailableName);

        Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, status.Kind);
        Assert.Null(unavailableName);
        Assert.Equal(top, L.Top);

        EngineTest.Run(L, """
                          getNameFromAddress = function() return nil end
                          registerSymbol = function() error('registration rejected') end
                          unregisterSymbol = function() error('removal rejected') end
                          """u8);

        status = SymbolRegistry.TryGetName(0x140001000UL, out var malformedName);

        Assert.Equal(LuaOperationStatusKind.NilResult, status.Kind);
        Assert.Null(malformedName);
        Assert.Equal(top, L.Top);

        SymbolName name = new("Player.Health");
        Assert.Equal(LuaOperationStatusKind.LuaFailure, SymbolRegistry.Register(name, 0x140001000UL).Kind);
        Assert.Equal(top, L.Top);
        Assert.Equal(LuaOperationStatusKind.LuaFailure, SymbolRegistry.Unregister(name).Kind);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void Symbol_name_rejects_missing_text_and_keeps_ordinal_identity()
    {
        Assert.Throws<ArgumentException>(() => new SymbolName(""));
        Assert.Throws<ArgumentException>(() => new SymbolName(" \t"));

        SymbolName upper = new("Player.Health");
        SymbolName same = new("Player.Health");
        SymbolName differentCase = new("player.health");

        Assert.Equal(upper, same);
        Assert.NotEqual(upper, differentCase);
        Assert.Equal("Player.Health", upper.ToString());
    }
}
