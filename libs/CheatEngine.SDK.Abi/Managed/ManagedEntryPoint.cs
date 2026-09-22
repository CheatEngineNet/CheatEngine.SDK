namespace CheatEngine.SDK.Abi.Managed;

/// <summary>
///     The names Cheat Engine looks up inside a managed plugin assembly, and the result codes of the bootstrap method.
/// </summary>
/// <remarks>
///     <para>
///         The host resolves a public static method with the default hostfxr component entry-point shape,
///         <c>int CEPluginInitialize(IntPtr args, int size)</c>, on a type whose full name is fixed. The type has to exist
///         in the plugin assembly itself; a referenced library cannot supply it. <c>args</c> is the address of a
///         <see cref="PluginInitRecord" />; the value of <c>size</c> is unverified and must not be relied on.
///     </para>
///     <para>
///         The bootstrap type is named <c>CESDK.CESDK</c> because Cheat Engine demands that name. It is unrelated to the
///         <c>CheatEngine.SDK</c> namespaces of this SDK and does not follow their naming.
///     </para>
///     <para>
///         <b>Evidence.</b> Namespace, type name, method name, signature and result codes: the official managed bootstrap
///         (<c>c# template/SDK/CESDK.cs</c>, CE 7.7.0.10621) - <i>verified</i>. That the host looks the names up verbatim:
///         7.5 host source plus string constants of the 7.7 executable - <i>inferred</i> for 7.7.
///     </para>
///     <para>Compile-time constants: usable anywhere, including attribute arguments and generated code.</para>
/// </remarks>
public static class ManagedEntryPoint
{
	/// <summary>The namespace of the bootstrap type.</summary>
	public const string Namespace = "CESDK";

	/// <summary>The simple name of the bootstrap type.</summary>
	public const string TypeName = "CESDK";

	/// <summary>The full name of the bootstrap type, as the host requests it.</summary>
	public const string FullTypeName = Namespace + "." + TypeName;

	/// <summary>The name of the public static bootstrap method.</summary>
	public const string MethodName = "CEPluginInitialize";

	/// <summary>Bootstrap result: the init record was filled in.</summary>
	public const int Success = 1;

	/// <summary>Bootstrap result: the plugin could not initialise; the init record content is unspecified.</summary>
	public const int Failure = 0;
}
