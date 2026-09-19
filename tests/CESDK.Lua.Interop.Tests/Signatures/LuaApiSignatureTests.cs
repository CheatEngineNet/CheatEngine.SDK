using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using CESDK.Lua.Interop.Api;
using CESDK.Lua.Interop.Tests.Support;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Tests.Signatures;

/// <summary>
///     DLL-free. A wrong native signature is memory-unsafe rather than an exception, so the shape of the table is pinned
///     by reflection: every slot is an unmanaged cdecl function pointer, and its public forwarder repeats it exactly
///     (C# would happily widen an <c>int</c> argument into a <c>long</c> slot).
/// </summary>
public sealed class LuaApiSignatureTests
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags PublicStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;

    private static FieldInfo[] TableFields => typeof(LuaApi.Table).GetFields(AnyInstance);

    [Fact]
    public void Table_has_a_slot_for_every_required_function()
    {
        string[] required =
        [
            "luaL_newstate", "lua_close", "lua_newthread",
            "lua_gettop", "lua_settop", "lua_pushvalue", "lua_rotate", "lua_copy", "lua_checkstack", "lua_absindex",
            "lua_type", "lua_isnumber", "lua_isstring", "lua_iscfunction", "lua_isinteger", "lua_isuserdata",
            "lua_tonumberx", "lua_tointegerx", "lua_toboolean", "lua_tolstring", "lua_rawlen", "lua_touserdata",
            "lua_topointer",
            "lua_pushnil", "lua_pushnumber", "lua_pushinteger", "lua_pushlstring", "lua_pushstring", "lua_pushcclosure",
            "lua_pushboolean", "lua_pushlightuserdata",
            "lua_getglobal", "lua_setglobal", "lua_gettable", "lua_settable", "lua_getfield", "lua_setfield",
            "lua_geti",
            "lua_seti", "lua_rawget", "lua_rawset", "lua_rawgeti", "lua_rawseti", "lua_createtable", "lua_newuserdata",
            "lua_getmetatable", "lua_setmetatable", "lua_next",
            "lua_callk", "lua_pcallk", "luaL_loadbufferx", "luaL_loadstring", "luaL_ref", "luaL_unref", "lua_error"
        ];

        HashSet<string> slots = new(TableFields.Select(static field => field.Name), StringComparer.Ordinal);

        Assert.DoesNotContain(required, name => !slots.Contains(name));
    }

    [Fact]
    public void Table_slots_are_unmanaged_cdecl_function_pointers()
    {
        Assert.NotEmpty(TableFields);
        foreach (var field in TableFields)
        {
            Assert.True(field.FieldType.IsUnmanagedFunctionPointer, field.Name);
            var conventions = field.GetModifiedFieldType().GetFunctionPointerCallingConventions();
            Assert.True(conventions is [var convention] && convention == typeof(CallConvCdecl), field.Name);
        }
    }

    [Fact]
    public void Public_forwarders_repeat_the_slot_signature_exactly()
    {
        foreach (var field in TableFields)
        {
            var members = typeof(LuaApi).GetMember(field.Name, PublicStatic);
            var forwarder = Assert.Single(members);

            if (forwarder is PropertyInfo property)
            {
                Assert.True(property.PropertyType == field.FieldType, field.Name);
                continue;
            }

            var method = Assert.IsType<MethodInfo>(forwarder, false);
            Type[] parameters = [.. method.GetParameters().Select(static parameter => parameter.ParameterType)];
            Assert.True(method.ReturnType == field.FieldType.GetFunctionPointerReturnType(), field.Name);
            Assert.True(parameters.SequenceEqual(field.FieldType.GetFunctionPointerParameterTypes()), field.Name);
        }
    }

    [Fact]
    public void Public_forwarders_load_their_own_slot_and_pass_the_parameters_in_declared_order()
    {
        // The type comparison above cannot see a forwarder wired to a sibling slot of the same type (lua_getlocal and
        // lua_setlocal, lua_rawget and lua_rawset, ...) or two same-typed arguments in the wrong order. The IL can:
        // a forwarder loads exactly one slot, its own, and its arguments are loaded as 0, 1, 2, ...
        foreach (var field in TableFields)
        {
            var forwarder = Assert.Single(typeof(LuaApi).GetMember(field.Name, PublicStatic));
            var method = forwarder is PropertyInfo property ? property.GetMethod : forwarder as MethodInfo;
            Assert.NotNull(method);

            List<string> slots = [];
            List<int> arguments = [];
            var indirectCalls = 0;
            var otherCalls = 0;
            foreach (var (code, operand) in IlReader.Read(method))
                if (code.OperandType == OperandType.InlineField)
                {
                    var loaded = method.Module.ResolveField(operand);
                    if (loaded?.DeclaringType == typeof(LuaApi.Table)) slots.Add(loaded.Name);
                }
                else if (ArgumentIndex(code, operand) is int index)
                {
                    arguments.Add(index);
                }
                else if (code == OpCodes.Calli)
                {
                    indirectCalls++;
                }
                else if (code.FlowControl == FlowControl.Call)
                {
                    otherCalls++;
                }

            Assert.True(slots is [var slot] && string.Equals(slot, field.Name, StringComparison.Ordinal), field.Name);
            Assert.True(arguments.SequenceEqual(Enumerable.Range(0, method.GetParameters().Length)), field.Name);
            Assert.True(indirectCalls == (forwarder is PropertyInfo ? 0 : 1), field.Name);
            Assert.True(otherCalls == 0, field.Name);
        }
    }

    [Fact]
    public void Forwarders_and_macros_are_aggressively_inlined_except_the_two_cold_macros()
    {
        // "A forwarder costs nothing over a raw calli" holds only while the JIT inlines it. Without the attribute that
        // is a per-call-site profitability guess (lua_pushliteral, the macro of the "name"u8 hot path, once lacked it).
        string[] binding = [nameof(LuaApi.Initialize), nameof(LuaApi.TryInitialize), nameof(LuaApi.GetMissingExports)];
        string[] cold = [nameof(LuaApi.luaL_dofile), nameof(LuaApi.luaL_dostring)];

        MethodInfo[] methods =
        [
            .. typeof(LuaApi).GetMethods(PublicStatic)
                .Where(method => !method.IsSpecialName && !binding.Contains(method.Name, StringComparer.Ordinal))
        ];
        string[] notInlined =
        [
            .. methods
                .Where(static method =>
                    !method.MethodImplementationFlags.HasFlag(MethodImplAttributes.AggressiveInlining))
                .Select(static method => method.Name)
                .Order(StringComparer.Ordinal)
        ];

        Assert.True(methods.Length > TableFields.Length, "The macros are missing from the reflected method set.");
        Assert.Equal(cold, notInlined);
    }

    [Fact]
    public void Pcallk_takes_six_parameters_with_a_pointer_sized_context()
    {
        var parameters = SlotParameters("lua_pcallk");

        Assert.Equal(6, parameters.Length);
        Assert.Equal(typeof(lua_State*), parameters[0]);
        Assert.Equal([typeof(int), typeof(int), typeof(int), typeof(nint)], parameters[1..5]);
        Assert.True(parameters[5].IsUnmanagedFunctionPointer);
        Assert.Equal(typeof(int), SlotReturn("lua_pcallk"));
    }

    [Fact]
    public void Callk_takes_five_parameters_with_a_pointer_sized_context()
    {
        var parameters = SlotParameters("lua_callk");

        Assert.Equal(5, parameters.Length);
        Assert.Equal(typeof(nint), parameters[3]);
        Assert.True(parameters[4].IsUnmanagedFunctionPointer);
        Assert.Equal(typeof(void), SlotReturn("lua_callk"));
    }

    [Fact]
    public void Size_t_is_pointer_sized_unsigned()
    {
        Assert.Equal(typeof(nuint), SlotReturn("lua_rawlen"));
        Assert.Equal(typeof(nuint), SlotReturn("lua_stringtonumber"));
        Assert.Equal(typeof(nuint*), SlotParameters("lua_tolstring")[2]);
        Assert.Equal(typeof(nuint*), SlotParameters("luaL_tolstring")[2]);
        Assert.Equal(typeof(nuint), SlotParameters("lua_pushlstring")[2]);
        Assert.Equal(typeof(nuint), SlotParameters("lua_newuserdata")[1]);
        Assert.Equal(typeof(nuint), SlotParameters("luaL_loadbufferx")[2]);
    }

    [Fact]
    public void Lua_integer_and_number_are_64_bit()
    {
        Assert.Equal(typeof(long), SlotParameters("lua_pushinteger")[1]);
        Assert.Equal(typeof(long), SlotReturn("lua_tointegerx"));
        Assert.Equal(typeof(long), SlotParameters("lua_rawgeti")[2]);
        Assert.Equal(typeof(long), SlotParameters("lua_rawseti")[2]);
        Assert.Equal(typeof(long), SlotParameters("lua_geti")[2]);
        Assert.Equal(typeof(long), SlotParameters("lua_seti")[2]);
        Assert.Equal(typeof(long), SlotReturn("luaL_len"));
        Assert.Equal(typeof(double), SlotParameters("lua_pushnumber")[1]);
        Assert.Equal(typeof(double), SlotReturn("lua_tonumberx"));
    }

    [Theory]
    [InlineData("lua_isnumber")]
    [InlineData("lua_isstring")]
    [InlineData("lua_iscfunction")]
    [InlineData("lua_isinteger")]
    [InlineData("lua_isuserdata")]
    [InlineData("lua_toboolean")]
    [InlineData("lua_checkstack")]
    [InlineData("lua_rawequal")]
    [InlineData("lua_compare")]
    [InlineData("lua_next")]
    [InlineData("lua_getmetatable")]
    [InlineData("lua_isyieldable")]
    public void Native_predicates_return_c_int_not_bool(string name)
    {
        Assert.Equal(typeof(int), SlotReturn(name));
    }

    [Fact]
    public void No_slot_uses_a_non_blittable_type()
    {
        foreach (var field in TableFields)
        {
            var signature = field.FieldType;
            Type[] all = [signature.GetFunctionPointerReturnType(), .. signature.GetFunctionPointerParameterTypes()];
            Assert.DoesNotContain(all,
                static type => type == typeof(bool) || type == typeof(char) || type == typeof(string) || type.IsByRef);
        }
    }

    private static int? ArgumentIndex(OpCode code, int operand)
    {
        if (code == OpCodes.Ldarg_0) return 0;

        if (code == OpCodes.Ldarg_1) return 1;

        if (code == OpCodes.Ldarg_2) return 2;

        if (code == OpCodes.Ldarg_3) return 3;

        return code == OpCodes.Ldarg_S || code == OpCodes.Ldarg ? operand : null;
    }

    private static Type[] SlotParameters(string name)
    {
        return Slot(name).FieldType.GetFunctionPointerParameterTypes();
    }

    private static Type SlotReturn(string name)
    {
        return Slot(name).FieldType.GetFunctionPointerReturnType();
    }

    private static FieldInfo Slot(string name)
    {
        var field = typeof(LuaApi.Table).GetField(name, AnyInstance);
        Assert.NotNull(field);
        return field;
    }
}
