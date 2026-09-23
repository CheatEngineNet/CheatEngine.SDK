using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.Analyzers.Diagnostics;

/// <summary>
///     Every <see cref="DiagnosticDescriptor" /> of the assembly, in one place: identifier, category, severity, texts and
///     help link are reviewed together and stay in step with <c>AnalyzerReleases.Unshipped.md</c> and
///     <c>analyzers/docs/</c>.
/// </summary>
internal static class DiagnosticDescriptors
{
	// One markdown page per rule, named after the identifier.
	private const string HelpLinkBase = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/";

	/// <summary>CESDK0001. Message arguments: the class name, then the sentence fragment describing the problem.</summary>
	public static readonly DiagnosticDescriptor InvalidPluginClass = new(
		DiagnosticIds.InvalidPluginClass,
		"Plugin class cannot be constructed by the generated entry point",
		"Plugin class '{0}' {1}",
		DiagnosticCategories.Plugin,
		DiagnosticSeverity.Error,
		true,
		"The entry point generated into a plugin assembly creates the plugin with 'new' from a top-level type of the same assembly. "
		+ "The class marked [CheatEnginePlugin] must therefore be a non-static, non-abstract, non-generic class that is not nested in a generic type, "
		+ "derives from CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin, is at least internal at every nesting level, is not file-local, does not take the reserved type name 'CESDK.CESDK', "
		+ "has a public or internal parameterless constructor that 'new' can call without an object initializer (no required members left unset, no [Obsolete] error) "
		+ "and carries a non-empty display name. When any of this is violated the generator emits nothing, or emits code that does not compile, and Cheat Engine cannot load the plugin.",
		HelpLinkBase + DiagnosticIds.InvalidPluginClass + ".md");

	/// <summary>CESDK0002 (compilation end). Message arguments: the class name, then the number of plugin classes found.</summary>
	public static readonly DiagnosticDescriptor MultiplePluginClasses = new(
		DiagnosticIds.MultiplePluginClasses,
		"More than one plugin class in the assembly",
		"'{0}' is one of {1} classes marked [CheatEnginePlugin]; a plugin assembly must contain exactly one, because Cheat Engine calls a single entry point per assembly",
		DiagnosticCategories.Plugin,
		DiagnosticSeverity.Error,
		true,
		"Cheat Engine loads one plugin per assembly through one fixed entry point. With several classes marked [CheatEnginePlugin] the generator cannot choose, "
		+ "emits nothing, and Cheat Engine cannot load the plugin. Keep the attribute on exactly one class, or move the other plugins to their own assemblies.",
		HelpLinkBase + DiagnosticIds.MultiplePluginClasses + ".md",
		WellKnownDiagnosticTags.CompilationEnd);

	/// <summary>CESDK0003 (compilation end). Message argument: the missing manual-bootstrap requirement.</summary>
	public static readonly DiagnosticDescriptor InvalidManualBootstrap = new(
		DiagnosticIds.InvalidManualBootstrap,
		"Manual Cheat Engine bootstrap is missing or malformed",
		"Entry-point generation is disabled, but {0}",
		DiagnosticCategories.Plugin,
		DiagnosticSeverity.Error,
		true,
		"When CheatEngineSdkGenerateEntryPoint is false, the assembly itself must provide the exact host entry point: "
		+ "a static CESDK.CESDK type whose public static CEPluginInitialize(System.IntPtr, int) method returns int. "
		+ "Cheat Engine looks up that identity by name and does not discover alternatives.",
		HelpLinkBase + DiagnosticIds.InvalidManualBootstrap + ".md",
		WellKnownDiagnosticTags.CompilationEnd);

	/// <summary>CESDK0004 (compilation end). Message argument: the declared namespace.</summary>
	public static readonly DiagnosticDescriptor ReservedNamespace = new(
		DiagnosticIds.ReservedNamespace,
		"Plugin assembly declares a namespace under 'CESDK'",
		"Namespace '{0}' is 'CESDK' or nested under it in a plugin assembly. Cheat Engine requires the type 'CESDK.CESDK' in every plugin assembly, "
		+ "so inside that namespace the simple name 'CESDK' binds to that type and a name that starts with 'CESDK.' no longer reaches a namespace you declared under 'CESDK' (CS0426). Use a different root namespace.",
		DiagnosticCategories.Plugin,
		DiagnosticSeverity.Warning,
		true,
		"Cheat Engine requires the plugin assembly to contain the type 'CESDK.CESDK', which the entry point generator emits, so the assembly always holds a class named 'CESDK' in the namespace 'CESDK'. "
		+ "Name lookup walks the enclosing namespaces before the global namespace: in code placed in 'CESDK' or 'CESDK.Something', the simple name 'CESDK' finds that class first, "
		+ "so a qualified name that starts with 'CESDK.' no longer resolves to a namespace you declared under 'CESDK' (CS0426). Keep plugin code in a namespace that does not start with 'CESDK'. "
		+ "The SDK itself lives under 'CheatEngine.SDK' and is not affected.",
		HelpLinkBase + DiagnosticIds.ReservedNamespace + ".md",
		WellKnownDiagnosticTags.CompilationEnd);

	/// <summary>CESDK0005 (compilation end). Message argument: the colliding source type.</summary>
	public static readonly DiagnosticDescriptor GeneratedEntryPointCollision = new(
		DiagnosticIds.GeneratedEntryPointCollision,
		"Source type collides with the generated Cheat Engine entry point",
		"Type '{0}' is declared by user code, but entry-point generation also emits CESDK.CESDK. Remove the type or disable generation and provide the complete manual bootstrap.",
		DiagnosticCategories.Plugin,
		DiagnosticSeverity.Error,
		true,
		"A generated plugin entry point always owns the type CESDK.CESDK. A user-authored type with that exact metadata "
		+ "identity makes the compilation ambiguous or duplicate the host entry point. Either let the generator own it, "
		+ "or disable generation and implement the complete manual bootstrap contract.",
		HelpLinkBase + DiagnosticIds.GeneratedEntryPointCollision + ".md",
		WellKnownDiagnosticTags.CompilationEnd);

	/// <summary>CESDK1001. Message argument: the enabled-only member called too early.</summary>
	public static readonly DiagnosticDescriptor RequiresPluginEnabledTooEarly = new(
		DiagnosticIds.RequiresPluginEnabledTooEarly,
		"Plugin startup code calls an enabled-only API",
		"'{0}' requires an enabled plugin and cannot be called from a plugin constructor, field initializer or property initializer",
		DiagnosticCategories.Usage,
		DiagnosticSeverity.Error,
		true,
		"Cheat Engine attaches the SDK runtime only after constructing the plugin. An API marked RequiresPluginEnabled "
		+ "therefore fails before OnEnable, including from instance construction and field or property initializers. Move the "
		+ "operation into OnEnable or a method OnEnable calls.",
		HelpLinkBase + DiagnosticIds.RequiresPluginEnabledTooEarly + ".md");

	/// <summary>CESDK1003. Message argument: the directly borrowed expression.</summary>
	public static readonly DiagnosticDescriptor DisposeBorrowedValue = new(
		DiagnosticIds.DisposeBorrowedValue,
		"A Cheat Engine-owned value is being destroyed",
		"'{0}' is explicitly marked CEOwned (borrowed) and must not be disposed; only dispose an Owned&lt;T&gt; value you own",
		DiagnosticCategories.Usage,
		DiagnosticSeverity.Error,
		true,
		"CEOwned marks a return value, property or parameter as a borrowed view of an object Cheat Engine owns. Calling "
		+ "Dispose or DisposeAsync directly on that value can leave Cheat Engine with a dangling object. Keep it borrowed, "
		+ "or obtain an explicit ownership-transfer contract before disposing it.",
		HelpLinkBase + DiagnosticIds.DisposeBorrowedValue + ".md");

	/// <summary>CESDK1004. Message argument: the method name.</summary>
	public static readonly DiagnosticDescriptor UnguardedUnmanagedCallersOnly = new(
		DiagnosticIds.UnguardedUnmanagedCallersOnly,
		"Exception can escape an [UnmanagedCallersOnly] method",
		"An exception can escape '{0}', which native code calls directly: the whole body must be one try statement whose catch-all clause returns a failure value",
		DiagnosticCategories.Usage,
		DiagnosticSeverity.Warning,
		true,
		"A managed exception that unwinds out of an [UnmanagedCallersOnly] method terminates the host process (Cheat Engine). "
		+ "The rule is structural: every top-level statement of the body must be a try statement with a 'catch' or 'catch (System.Exception)' clause without a filter, "
		+ "a local declaration or return whose value cannot throw, a local function declaration, an empty statement, or a nested block of such statements. "
		+ "No catch or finally block of such a try statement may contain a throw or a call of a [DoesNotReturn] method other than Environment.FailFast and Environment.Exit.",
		HelpLinkBase + DiagnosticIds.UnguardedUnmanagedCallersOnly + ".md");

	/// <summary>CESDK1005. Message argument: the lifecycle method name.</summary>
	public static readonly DiagnosticDescriptor AsyncPluginLifecycle = new(
		DiagnosticIds.AsyncPluginLifecycle,
		"Plugin lifecycle callback must not be async void",
		"'{0}' is an async void lifecycle callback: its continuation can outlive the plugin enable or disable transition",
		DiagnosticCategories.Usage,
		DiagnosticSeverity.Error,
		true,
		"Cheat Engine's enable and disable callbacks are synchronous and the host cannot await async void. A continuation may "
		+ "run after teardown, lose exceptions, or touch an invalid Lua state. Keep OnEnable and OnDisable synchronous; use a "
		+ "host-owned, explicitly tracked operation only when the API actually supports asynchronous waiting.",
		HelpLinkBase + DiagnosticIds.AsyncPluginLifecycle + ".md");

	/// <summary>CESDK1020. Message argument: the host-width expression.</summary>
	public static readonly DiagnosticDescriptor HostWidthPointerSize = new(
		DiagnosticIds.HostWidthPointerSize,
		"PointerSize built from the plugin process width",
		"This PointerSize comes from '{0}', the width of the plugin process, not of the Cheat Engine target",
		DiagnosticCategories.Usage,
		DiagnosticSeverity.Warning,
		true,
		"A plugin always runs inside the 64-bit Cheat Engine process, so IntPtr.Size, nint.Size, sizeof(nint), Unsafe.SizeOf<nint>(), "
		+ "Marshal.SizeOf<IntPtr>() and Environment.Is64BitProcess describe the plugin, never the target: an x86 target has 4-byte pointers, "
		+ "and Cheat Engine's configured pointer size is a separate setting. Read the target bitness or the configured pointer size from "
		+ "Cheat Engine instead (TargetArchitectureObservation.Bitness or ConfiguredPointerSize).",
		HelpLinkBase + DiagnosticIds.HostWidthPointerSize + ".md");

	/// <summary>CESDK2001. Message argument: the member name.</summary>
	public static readonly DiagnosticDescriptor UnsafeBlocksRequired = new(
		DiagnosticIds.UnsafeBlocksRequired,
		"Lua binding needs AllowUnsafeBlocks",
		"'{0}' is a Lua function export, but this compilation does not allow unsafe code (AllowUnsafeBlocks); registration thunks need it",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"The generated registration table takes the address of the UnmanagedCallersOnly thunks emitted for LuaFunction, which needs unsafe code. "
		+ "LuaGlobal bodies do not take function addresses and therefore remain available without AllowUnsafeBlocks. A project that declares a LuaFunction must explicitly set <AllowUnsafeBlocks>true</AllowUnsafeBlocks>.",
		HelpLinkBase + DiagnosticIds.UnsafeBlocksRequired + ".md");

	/// <summary>CESDK2002. Message arguments: the member name, then the sentence fragment describing the problem.</summary>
	public static readonly DiagnosticDescriptor InvalidLuaBindingContainingType = new(
		DiagnosticIds.InvalidLuaBindingContainingType,
		"Type cannot receive a generated Lua binding part",
		"'{0}' is a Lua binding, but its containing type {1}",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"The LuaBindings generator adds a generated part to the type that declares a [LuaFunction] or [LuaGlobal] member. "
		+ "That type, and every type it is nested in, must be a partial, non-generic class or struct that is not a file-local type. "
		+ "When any of this is violated the generator emits nothing for the member, and this rule names the cause.",
		HelpLinkBase + DiagnosticIds.InvalidLuaBindingContainingType + ".md");

	/// <summary>
	///     CESDK2003. Message arguments: the method name, then the sentence fragment describing the problem. All reports
	///     are local symbol diagnostics. CESDK2005 owns the one compilation-end condition, duplicate Lua names.
	/// </summary>
	public static readonly DiagnosticDescriptor InvalidLuaFunction = new(
		DiagnosticIds.InvalidLuaFunction,
		"[LuaFunction] method cannot be exported by a generated thunk",
		"Lua function '{0}' {1}",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"A method marked [LuaFunction] must be an ordinary, static, non-generic, non-async method with a valid Lua name; parameters are passed by value, none optional or params, "
		+ "each of a marshalled kind (int, long, float, double, bool, nuint, ReadOnlySpan<byte>, string) except an optional leading LuaState; the return type is void or one of the same marshalled kinds. "
		+ "When any of this is violated the generator emits no thunk for the method, and this rule names the cause.",
		HelpLinkBase + DiagnosticIds.InvalidLuaFunction + ".md");

	/// <summary>CESDK2004. Message arguments: the method name, then the sentence fragment describing the problem.</summary>
	public static readonly DiagnosticDescriptor InvalidLuaGlobal = new(
		DiagnosticIds.InvalidLuaGlobal,
		"[LuaGlobal] method cannot receive a generated body",
		"Lua global binding '{0}' {1}",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"A method marked [LuaGlobal] must be the defining declaration of an ordinary, static, non-generic, non-async partial method with a valid Lua name and no implementing part yet. "
		+ "Parameters, in order: an optional leading LuaState, then by-value arguments of a marshalled kind, then the results (out parameters of a marshalled kind other than ReadOnlySpan<byte>, "
		+ "or a copy-out pair Span<byte> destination, out int written). Any out result makes it the Try form, which must return bool; no result makes it the throwing form, "
		+ "whose return type is void or a marshalled kind other than ReadOnlySpan<byte>. When any of this is violated the generator emits no body for the method, and this rule names the cause.",
		HelpLinkBase + DiagnosticIds.InvalidLuaGlobal + ".md");

	/// <summary>CESDK2005 (compilation end). Message arguments: the method and duplicated Lua name.</summary>
	public static readonly DiagnosticDescriptor DuplicateLuaName = new(
		DiagnosticIds.DuplicateLuaName,
		"Lua function name is duplicated",
		"Lua function '{0}' duplicates the Lua name '{1}' in the same containing type; one registration table cannot bind that name twice",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"Two valid LuaFunction methods of one type cannot export the same Lua global name. The generator deliberately emits "
		+ "neither thunk so registration order cannot silently choose one. Give one method a distinct Lua name or move it to "
		+ "another binding type.",
		HelpLinkBase + DiagnosticIds.DuplicateLuaName + ".md",
		WellKnownDiagnosticTags.CompilationEnd);

	/// <summary>CESDK2006. Message arguments: the annotated member and its unsupported shape.</summary>
	public static readonly DiagnosticDescriptor InvalidLuaAnnotationTarget = new(
		DiagnosticIds.InvalidLuaAnnotationTarget,
		"Lua annotation target cannot receive generated code",
		"Lua annotation on '{0}' cannot be generated: {1}",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"LuaClass, LuaMethod and LuaProperty are declarative generator inputs. Their target must have the exact partial "
		+ "borrowed-handle or member shape that the generator can implement. The analyzer reports the invalid declaration at "
		+ "its source location so an unsupported target never silently loses generated code.",
		HelpLinkBase + DiagnosticIds.InvalidLuaAnnotationTarget + ".md");

	/// <summary>CESDK2007. Message arguments: the member and the generated identity it collides with.</summary>
	public static readonly DiagnosticDescriptor GeneratedLuaIdentityCollision = new(
		DiagnosticIds.GeneratedLuaIdentityCollision,
		"User member collides with a generated Lua binding identity",
		"Member '{0}' collides with generated identity '{1}'; rename the user member or change the binding declaration",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"Generated Lua bindings add required members such as registration methods, thunk methods, cached globals and LuaClass "
		+ "handle members. A source declaration with the same identity prevents compilation. This rule identifies the user "
		+ "declaration before generated code is emitted.",
		HelpLinkBase + DiagnosticIds.GeneratedLuaIdentityCollision + ".md");

	/// <summary>CESDK2010. Message arguments: the method name, then the sentence fragment describing the problem.</summary>
	public static readonly DiagnosticDescriptor NonTrailingOptionalLuaArgument = new(
		DiagnosticIds.NonTrailingOptionalLuaArgument,
		"Optional Lua argument is not in a trailing run",
		"Lua binding '{0}' {1}",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"A LuaOptional<T> argument of a [LuaGlobal] or [LuaFunction] binding can be omitted, and Lua cannot receive an argument "
		+ "after an absent one. Every required argument therefore comes before the first optional one. The generator emits "
		+ "nothing for a declaration that breaks this rule.",
		HelpLinkBase + DiagnosticIds.NonTrailingOptionalLuaArgument + ".md");

	/// <summary>CESDK2011. Message arguments: the method name, then the sentence fragment describing the problem.</summary>
	public static readonly DiagnosticDescriptor InvalidOptionalOrVariadicLuaResult = new(
		DiagnosticIds.InvalidOptionalOrVariadicLuaResult,
		"Optional or variadic Lua result shape is invalid",
		"Lua global binding '{0}' {1}",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"The results of a [LuaGlobal] binding are read in order: required results, then 'out LuaOptional<T>' results, then at "
		+ "most one variadic 'Span<T> values, out int count' pair of int, long, float, double, bool or nuint, declared last "
		+ "and only on the form that returns LuaOperationStatus. The generator emits nothing for another result order or shape.",
		HelpLinkBase + DiagnosticIds.InvalidOptionalOrVariadicLuaResult + ".md");

	/// <summary>CESDK2012. Message arguments: the member name, then the sentence fragment describing the problem.</summary>
	public static readonly DiagnosticDescriptor LookAlikeLuaContractType = new(
		DiagnosticIds.LookAlikeLuaContractType,
		"Type impersonates an SDK Lua contract type",
		"Lua binding '{0}' {1}",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"LuaOptional<T> and LuaOperationStatus are contracts of the CheatEngine.SDK.Lua assembly. A type with the same namespace, "
		+ "name and arity declared in source or in another assembly is not that contract, so the generator never selects the "
		+ "optional or outcome shape for it and emits nothing for the declaration.",
		HelpLinkBase + DiagnosticIds.LookAlikeLuaContractType + ".md");

	/// <summary>CESDK2013. Message arguments: the member name, then the sentence fragment describing the problem.</summary>
	public static readonly DiagnosticDescriptor UnsupportedLuaOptionalPosition = new(
		DiagnosticIds.UnsupportedLuaOptionalPosition,
		"LuaOptional is not supported in this position",
		"Lua binding '{0}' {1}",
		DiagnosticCategories.Generation,
		DiagnosticSeverity.Error,
		true,
		"LuaOptional<T> is supported for a [LuaGlobal] argument or 'out' result and for a [LuaFunction] parameter, with T one of "
		+ "int, long, float, double, bool, nuint or string. It is not supported as a return value, on [LuaMethod] or "
		+ "[LuaProperty] members, with string?, a custom-marshalled or nested type argument, or together with [LuaMarshaller]. "
		+ "The generator emits nothing for such a member.",
		HelpLinkBase + DiagnosticIds.UnsupportedLuaOptionalPosition + ".md");
}
