namespace CheatEngine.SDK.Analyzers.Diagnostics;

/// <summary>
///     The identifiers of every diagnostic this assembly reports. Identifiers are never reused or renumbered: they are
///     what users write in <c>.editorconfig</c>, <c>#pragma warning</c> and <c>NoWarn</c>.
/// </summary>
/// <remarks>
///     Ranges: <c>CESDK0xxx</c> plugin shape and bootstrap, <c>CESDK1xxx</c> runtime-safety usage, <c>CESDK2xxx</c>
///     generator-input errors (reported by the generators' companion rules).
///     Only the identifiers listed here exist.
/// </remarks>
public static class DiagnosticIds
{
	/// <summary>A class marked <c>[CheatEnginePlugin]</c> cannot be constructed by the generated entry point.</summary>
	public const string InvalidPluginClass = "CESDK0001";

	/// <summary>More than one class in the compilation is marked <c>[CheatEnginePlugin]</c>.</summary>
	public const string MultiplePluginClasses = "CESDK0002";

	/// <summary>Generation is disabled but the manual host bootstrap is absent or has the wrong shape.</summary>
	public const string InvalidManualBootstrap = "CESDK0003";

	/// <summary>
	///     A plugin assembly declares a namespace that is <c>CESDK</c> or nested under it: the namespace of the
	///     <c>CESDK.CESDK</c> type that Cheat Engine requires in every plugin assembly.
	/// </summary>
	public const string ReservedNamespace = "CESDK0004";

	/// <summary>A source declaration collides with the <c>CESDK.CESDK</c> type generated for an enabled plugin.</summary>
	public const string GeneratedEntryPointCollision = "CESDK0005";

	/// <summary>A plugin constructor or initializer directly calls an API which requires an enabled plugin.</summary>
	public const string RequiresPluginEnabledTooEarly = "CESDK1001";

	/// <summary>A directly borrowed Cheat Engine value is disposed or asynchronously disposed.</summary>
	public const string DisposeBorrowedValue = "CESDK1003";

	/// <summary>An exception can escape a method marked <c>[UnmanagedCallersOnly]</c>.</summary>
	public const string UnguardedUnmanagedCallersOnly = "CESDK1004";

	/// <summary>An <c>OnEnable</c> or <c>OnDisable</c> implementation is <c>async void</c>.</summary>
	public const string AsyncPluginLifecycle = "CESDK1005";

	/// <summary>A <c>[LuaFunction]</c> or <c>[LuaGlobal]</c> binding exists but the compilation does not allow unsafe code.</summary>
	public const string UnsafeBlocksRequired = "CESDK2001";

	/// <summary>The type that declares a <c>[LuaFunction]</c> or <c>[LuaGlobal]</c> member cannot receive a generated part.</summary>
	public const string InvalidLuaBindingContainingType = "CESDK2002";

	/// <summary>A <c>[LuaFunction]</c> method cannot be exported by a generated thunk.</summary>
	public const string InvalidLuaFunction = "CESDK2003";

	/// <summary>A <c>[LuaGlobal]</c> method cannot receive a generated body.</summary>
	public const string InvalidLuaGlobal = "CESDK2004";

	/// <summary>Two otherwise valid Lua function exports of one type have the same Lua name.</summary>
	public const string DuplicateLuaName = "CESDK2005";

	/// <summary>A Lua annotation is placed on a declaration the active generator cannot implement.</summary>
	public const string InvalidLuaAnnotationTarget = "CESDK2006";

	/// <summary>A user declaration collides with a member that a Lua binding generator must emit.</summary>
	public const string GeneratedLuaIdentityCollision = "CESDK2007";

	/// <summary>A <c>LuaOptional&lt;T&gt;</c> argument of a Lua binding is followed by a required argument.</summary>
	public const string NonTrailingOptionalLuaArgument = "CESDK2010";

	/// <summary>The optional or variadic results of a <c>[LuaGlobal]</c> binding do not have a valid shape.</summary>
	public const string InvalidOptionalOrVariadicLuaResult = "CESDK2011";

	/// <summary>A binding names a type that has the metadata name of an SDK Lua contract type but is not that type.</summary>
	public const string LookAlikeLuaContractType = "CESDK2012";

	/// <summary><c>LuaOptional&lt;T&gt;</c> is used in a position or with a type argument that is not supported.</summary>
	public const string UnsupportedLuaOptionalPosition = "CESDK2013";
}
