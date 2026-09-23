namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     The <c>global::</c>-qualified names of the <c>CheatEngine.SDK.Lua</c> API that generated code calls, in one place,
///     so that
///     a rename in the runtime is one edit here and the emitters never spell a type name twice.
/// </summary>
/// <remarks>
///     Every name is fully qualified because generated code lives in the consumer's assembly, whose usings, aliases,
///     namespaces and nested-type names are unknown: a consumer type or namespace named <c>CheatEngine</c> would
///     capture the first segment of a plain <c>CheatEngine.SDK.Lua...</c> name.
/// </remarks>
internal static class LuaApiNames
{
	/// <summary>The state view every operation starts from.</summary>
	public const string LuaState = "global::CheatEngine.SDK.Lua.State.LuaState";

	/// <summary>The protected-call status.</summary>
	public const string LuaStatus = "global::CheatEngine.SDK.Lua.Calls.LuaStatus";

	/// <summary>The compact outcome returned by an opt-in detailed generated global binding.</summary>
	public const string LuaOperationStatus = "global::CheatEngine.SDK.Lua.Calls.LuaOperationStatus";

	/// <summary>The cached registry reference a wrapper class holds per bound global.</summary>
	public const string LuaRef = "global::CheatEngine.SDK.Lua.References.LuaRef";

	/// <summary>The exception a throwing wrapper raises.</summary>
	public const string LuaException = "global::CheatEngine.SDK.Lua.Calls.LuaException";

	/// <summary>The address of a managed <c>lua_CFunction</c>.</summary>
	public const string LuaNativeFunction = "global::CheatEngine.SDK.Lua.Callbacks.LuaNativeFunction";

	/// <summary>The SDK-owned result of a lease-producing Lua registration transaction.</summary>
	public const string LuaRegistrationResult = "global::CheatEngine.SDK.Lua.Registration.LuaRegistrationResult";

	/// <summary>The collision policy passed explicitly to the SDK registration transaction.</summary>
	public const string LuaRegistrationCollisionPolicy =
		"global::CheatEngine.SDK.Lua.Registration.LuaRegistrationCollisionPolicy";

	/// <summary>One generated global-name/native-thunk registration descriptor.</summary>
	public const string LuaRegistrationEntry = "global::CheatEngine.SDK.Lua.Registration.LuaRegistrationEntry";

	/// <summary>The SDK-owned registration-set transaction.</summary>
	public const string LuaRegistrationSet = "global::CheatEngine.SDK.Lua.Registration.LuaRegistrationSet";

	/// <summary>What a thunk calls to report failures.</summary>
	public const string LuaThunk = "global::CheatEngine.SDK.Lua.Callbacks.LuaThunk";

	/// <summary>The stack-bound lifecycle lease generated calls hold through their final stack restoration.</summary>
	public const string LuaRuntimeOperation = "global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation";

	/// <summary><c>AcquireOperation()</c>: one provider call per generated call.</summary>
	public const string AcquireOperation = "global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation()";

	/// <summary>The runtime owner of a state-supplied generated call.</summary>
	public const string LuaRuntime = "global::CheatEngine.SDK.Lua.Runtime.LuaRuntime";

	/// <summary>Generator-facing push of a cached global function.</summary>
	public const string LuaGlobalFunctions = "global::CheatEngine.SDK.Lua.CompilerServices.LuaGlobalFunctions";

	/// <summary>Generator-facing cold exits of a call body.</summary>
	public const string LuaCallSupport = "global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport";

	/// <summary>The untyped, borrowed Cheat Engine object handle used by generated class wrappers.</summary>
	public const string CEObject = "global::CheatEngine.SDK.Engine.Objects.CEObject";

	/// <summary>The static-abstract borrowed-handle contract implemented by generated class wrappers.</summary>
	public const string ICEObject = "global::CheatEngine.SDK.Engine.Objects.ICEObject";

	/// <summary>The static Lua marshaller contract implemented by generated class wrappers.</summary>
	public const string ILuaMarshaller = "global::CheatEngine.SDK.Lua.Marshalling.ILuaMarshaller";

	/// <summary>
	///     The explicit optional value (omitted, <c>nil</c> or a value) and, without type arguments, its non-generic
	///     factory class. Generated code writes <c>LuaOptional&lt;T&gt;</c> for an optional argument or result.
	/// </summary>
	public const string LuaOptional = "global::CheatEngine.SDK.Lua.Marshalling.LuaOptional";

	/// <summary>The exception a wrapper throws before touching Lua when optional arguments leave a gap.</summary>
	public const string ArgumentException = "global::System.ArgumentException";

	/// <summary>The attribute every thunk carries, with its <c>cdecl</c> convention.</summary>
	public const string UnmanagedCallersOnlyCdecl =
		"[global::System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]";

	/// <summary>The one exception type a thunk catches.</summary>
	public const string Exception = "global::System.Exception";

	/// <summary>UTF-8 bytes, the primary string type.</summary>
	public const string ReadOnlySpanOfByte = "global::System.ReadOnlySpan<byte>";

	/// <summary>The destination of a copy-out string result.</summary>
	public const string SpanOfByte = "global::System.Span<byte>";

	/// <summary>The generic span a variadic result pair copies into, without its type argument.</summary>
	public const string Span = "global::System.Span";

	/// <summary><c>Int32Marshaller</c>.</summary>
	public const string Int32Marshaller = "global::CheatEngine.SDK.Lua.Marshalling.Int32Marshaller";

	/// <summary><c>Int64Marshaller</c>.</summary>
	public const string Int64Marshaller = "global::CheatEngine.SDK.Lua.Marshalling.Int64Marshaller";

	/// <summary><c>SingleMarshaller</c>.</summary>
	public const string SingleMarshaller = "global::CheatEngine.SDK.Lua.Marshalling.SingleMarshaller";

	/// <summary><c>DoubleMarshaller</c>.</summary>
	public const string DoubleMarshaller = "global::CheatEngine.SDK.Lua.Marshalling.DoubleMarshaller";

	/// <summary><c>BooleanMarshaller</c>.</summary>
	public const string BooleanMarshaller = "global::CheatEngine.SDK.Lua.Marshalling.BooleanMarshaller";

	/// <summary><c>AddressMarshaller</c>.</summary>
	public const string AddressMarshaller = "global::CheatEngine.SDK.Lua.Marshalling.AddressMarshaller";

	/// <summary><c>Utf8Marshaller</c>.</summary>
	public const string Utf8Marshaller = "global::CheatEngine.SDK.Lua.Marshalling.Utf8Marshaller";

	/// <summary><c>StringMarshaller</c>.</summary>
	public const string StringMarshaller = "global::CheatEngine.SDK.Lua.Marshalling.StringMarshaller";
}
