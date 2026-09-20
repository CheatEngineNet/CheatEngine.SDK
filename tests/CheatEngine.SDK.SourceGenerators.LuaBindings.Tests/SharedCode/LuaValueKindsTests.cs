using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.SharedCode;

/// <summary>
///     The per-kind facts of <c>CheatEngine.SDK.SourceGenerators.Shared.LuaEmit.LuaValueKinds</c>: each kind maps to one
///     real
///     marshaller of <c>CheatEngine.SDK.Lua</c>.
/// </summary>
public sealed class LuaValueKindsTests
{
    [Theory]
    [InlineData("Int32", "global::CheatEngine.SDK.Lua.Marshalling.Int32Marshaller", "int", "integer", "an integer")]
    [InlineData("Int64", "global::CheatEngine.SDK.Lua.Marshalling.Int64Marshaller", "long", "integer", "an integer")]
    [InlineData("Single", "global::CheatEngine.SDK.Lua.Marshalling.SingleMarshaller", "float", "number", "a number")]
    [InlineData("Double", "global::CheatEngine.SDK.Lua.Marshalling.DoubleMarshaller", "double", "number", "a number")]
    [InlineData("Boolean", "global::CheatEngine.SDK.Lua.Marshalling.BooleanMarshaller", "bool", "boolean", "a boolean")]
    [InlineData("Address", "global::CheatEngine.SDK.Lua.Marshalling.AddressMarshaller", "nuint", "integer",
        "an integer")]
    [InlineData("Utf8", "global::CheatEngine.SDK.Lua.Marshalling.Utf8Marshaller", "global::System.ReadOnlySpan<byte>",
        "string",
        "a string")]
    [InlineData("String", "global::CheatEngine.SDK.Lua.Marshalling.StringMarshaller", "string", "string", "a string")]
    public void Kind_maps_to_marshaller_type_and_words(string kindName, string marshaller, string typeName,
        string expectedArgument, string expectedResult)
    {
        // The enum is internal to the generator: rows name it, the test resolves it.
        var kind = Enum.Parse<LuaValueKind>(kindName);

        Assert.Equal(marshaller, LuaValueKinds.MarshallerTypeName(kind));
        Assert.Equal(typeName, LuaValueKinds.TypeName(kind));
        Assert.Equal(expectedArgument, LuaValueKinds.ExpectedArgument(kind));
        Assert.Equal(expectedResult, LuaValueKinds.ExpectedResult(kind));
    }

    [Fact]
    public void Marshaller_names_denote_real_types_of_the_lua_assembly()
    {
        foreach (var kind in Enum.GetValues<LuaValueKind>())
        {
            var name = LuaValueKinds.MarshallerTypeName(kind)
                .Replace("global::", string.Empty, StringComparison.Ordinal);
            var type = typeof(LuaState).Assembly.GetType(name);
            Assert.NotNull(type);
            Assert.NotNull(type.GetMethod("Push"));
            Assert.NotNull(type.GetMethod("TryRead"));
        }
    }

    [Fact]
    public void String_is_the_only_nullable_and_reference_kind()
    {
        Assert.Equal("string?", LuaValueKinds.TypeName(LuaValueKind.String, true));
        Assert.Equal("int", LuaValueKinds.TypeName(LuaValueKind.Int32, true));
        Assert.True(LuaValueKinds.IsReferenceType(LuaValueKind.String));
        Assert.False(LuaValueKinds.IsReferenceType(LuaValueKind.Utf8));
    }

    [Fact]
    public void Utf8_is_the_only_kind_that_cannot_be_a_result()
    {
        foreach (var kind in Enum.GetValues<LuaValueKind>())
            Assert.Equal(kind != LuaValueKind.Utf8, LuaValueKinds.CanBeResult(kind));
    }
}
