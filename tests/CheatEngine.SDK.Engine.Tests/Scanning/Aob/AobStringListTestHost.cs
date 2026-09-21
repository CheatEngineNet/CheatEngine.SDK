using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Tests.Scanning.Aob;

/// <summary>CE 7.7-shaped Lua stand-ins for the AOB/StringList vertical slice, over the normal fake host userdata.</summary>
internal static class AobStringListTestHost
{
    /// <summary>Creates a fresh host object with the CE StringList members this slice invokes.</summary>
    public static CEObject CreateList(LuaState state)
    {
        return FakeHost.CreateObject(state, "Probe", """
                                                     o.props.Count = 2
                                                     o.props.Sorted = false
                                                     o.props.Duplicates = 1
                                                     o.setters.Duplicates = function(o, value)
                                                       o.props.Duplicates = math.tointeger(value) or 0
                                                     end
                                                     o.props.CaseSensitive = true
                                                     o.items = { '00401000', '7FF6A1B2C3D4' }
                                                     o.getters.add = function(o)
                                                       return function(value)
                                                         o.items[#o.items + 1] = value
                                                         o.props.Count = #o.items
                                                         return #o.items - 1
                                                       end
                                                     end
                                                     o.getters.clear = function(o)
                                                       return function()
                                                         o.items = {}
                                                         o.props.Count = 0
                                                       end
                                                     end
                                                     o.getters.delete = function(o)
                                                       return function(index)
                                                         table.remove(o.items, index + 1)
                                                         o.props.Count = #o.items
                                                       end
                                                     end
                                                     o.getters.getText = function(o)
                                                       return function() return table.concat(o.items, '\n') end
                                                     end
                                                     o.getters.setText = function(o)
                                                       return function(value)
                                                         o.items = { value }
                                                         o.props.Count = 1
                                                       end
                                                     end
                                                     o.getters.indexOf = function(o)
                                                       return function(value)
                                                         for i, candidate in ipairs(o.items) do
                                                           if candidate == value then return i - 1 end
                                                         end
                                                         return -1
                                                       end
                                                     end
                                                     """);
    }

    /// <summary>Creates the CE-shaped empty StringList result used to prove that an empty scan remains successful.</summary>
    public static CEObject CreateEmptyList(LuaState state)
    {
        return FakeHost.CreateObject(state, "Probe", "o.props.Count = 0");
    }

    /// <summary>Creates a CE-shaped object whose Count is deliberately invalid for result-shape failure coverage.</summary>
    public static CEObject CreateInvalidCountList(LuaState state)
    {
        return FakeHost.CreateObject(state, "Probe", "o.props.Count = -1");
    }

    /// <summary>Publishes a fake host object as a Lua global through the protected setter.</summary>
    public static void SetGlobalObject(LuaState state, ReadOnlySpan<byte> name, CEObject value)
    {
        using LuaFrame frame = new(state);
        value.Push(state);
        Assert.True(state.TrySetGlobal(name).IsOk);
    }

    /// <summary>Installs a string-form AOBScan stand-in that records every optional argument and returns the given list.</summary>
    public static void InstallAobScan(LuaState state, CEObject results)
    {
        SetGlobalObject(state, "aob_results"u8, results);
        EngineTest.Run(state, """
                              function AOBScan(...)
                                aob_argument_count = select('#', ...)
                                aob_pattern = select(1, ...)
                                aob_protection = select(2, ...)
                                aob_alignment = select(3, ...)
                                aob_alignment_parameter = select(4, ...)
                                if aob_pattern == 'nil-result' then return nil end
                                if aob_pattern == 'raise' then error('AOBScan stand-in raised') end
                                if aob_pattern == 'invalid-result' then return 42 end
                                if aob_pattern == 'malformed-result' then return aob_malformed end
                                return aob_results
                              end
                              """u8);
    }

    /// <summary>Installs the exact CE factory spelling and makes it return the supplied fresh fake list.</summary>
    public static void InstallStringListFactory(LuaState state, CEObject created)
    {
        SetGlobalObject(state, "created_string_list"u8, created);
        EngineTest.Run(state, "function createStringlist() return created_string_list end"u8);
    }
}
