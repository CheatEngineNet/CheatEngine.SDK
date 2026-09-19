namespace CESDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     The <c>global::</c>-qualified names of the <c>CESDK.Lua</c> API that generated code calls, in one place, so that
///     a rename in the runtime is one edit here and the emitters never spell a type name twice.
/// </summary>
/// <remarks>
///     Every name is fully qualified because generated code lives in the consumer's assembly, whose usings, aliases
///     and nested-type names are unknown, and because inside a plugin assembly the simple name <c>CESDK</c> can bind
///     to the host-mandated entry-point type.
/// </remarks>
internal static class LuaApiNames
{
    /// <summary>The state view every operation starts from.</summary>
    public const string LuaState = "global::CESDK.Lua.State.LuaState";

    /// <summary>The protected-call status.</summary>
    public const string LuaStatus = "global::CESDK.Lua.Calls.LuaStatus";

    /// <summary>The cached registry reference a wrapper class holds per bound global.</summary>
    public const string LuaRef = "global::CESDK.Lua.References.LuaRef";

    /// <summary>The exception a throwing wrapper raises.</summary>
    public const string LuaException = "global::CESDK.Lua.Calls.LuaException";

    /// <summary>The address of a managed <c>lua_CFunction</c>.</summary>
    public const string LuaNativeFunction = "global::CESDK.Lua.Callbacks.LuaNativeFunction";

    /// <summary>What a thunk calls to report failures.</summary>
    public const string LuaThunk = "global::CESDK.Lua.Callbacks.LuaThunk";

    /// <summary><c>AcquireState()</c>: one provider call per operation.</summary>
    public const string AcquireState = "global::CESDK.Lua.Runtime.LuaRuntime.AcquireState()";

    /// <summary>Generator-facing push of a cached global function.</summary>
    public const string LuaGlobalFunctions = "global::CESDK.Lua.CompilerServices.LuaGlobalFunctions";

    /// <summary>Generator-facing cold exits of a call body.</summary>
    public const string LuaCallSupport = "global::CESDK.Lua.CompilerServices.LuaCallSupport";

    /// <summary>The attribute every thunk carries, with its <c>cdecl</c> convention.</summary>
    public const string UnmanagedCallersOnlyCdecl =
        "[global::System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]";

    /// <summary>The one exception type a thunk catches.</summary>
    public const string Exception = "global::System.Exception";

    /// <summary>UTF-8 bytes, the primary string type.</summary>
    public const string ReadOnlySpanOfByte = "global::System.ReadOnlySpan<byte>";

    /// <summary>The destination of a copy-out string result.</summary>
    public const string SpanOfByte = "global::System.Span<byte>";

    /// <summary><c>Int32Marshaller</c>.</summary>
    public const string Int32Marshaller = "global::CESDK.Lua.Marshalling.Int32Marshaller";

    /// <summary><c>Int64Marshaller</c>.</summary>
    public const string Int64Marshaller = "global::CESDK.Lua.Marshalling.Int64Marshaller";

    /// <summary><c>SingleMarshaller</c>.</summary>
    public const string SingleMarshaller = "global::CESDK.Lua.Marshalling.SingleMarshaller";

    /// <summary><c>DoubleMarshaller</c>.</summary>
    public const string DoubleMarshaller = "global::CESDK.Lua.Marshalling.DoubleMarshaller";

    /// <summary><c>BooleanMarshaller</c>.</summary>
    public const string BooleanMarshaller = "global::CESDK.Lua.Marshalling.BooleanMarshaller";

    /// <summary><c>AddressMarshaller</c>.</summary>
    public const string AddressMarshaller = "global::CESDK.Lua.Marshalling.AddressMarshaller";

    /// <summary><c>Utf8Marshaller</c>.</summary>
    public const string Utf8Marshaller = "global::CESDK.Lua.Marshalling.Utf8Marshaller";

    /// <summary><c>StringMarshaller</c>.</summary>
    public const string StringMarshaller = "global::CESDK.Lua.Marshalling.StringMarshaller";
}
