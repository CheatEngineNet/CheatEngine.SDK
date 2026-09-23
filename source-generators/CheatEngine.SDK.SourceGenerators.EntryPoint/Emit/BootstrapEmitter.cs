using System;

using CheatEngine.SDK.SourceGenerators.EntryPoint.Model;
using CheatEngine.SDK.SourceGenerators.Shared;

using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Emit;

/// <summary>
///     Writes the one file this generator produces: the host-mandated <c>CESDK.CESDK</c> entry point and the
///     file-local plugin factory.
/// </summary>
/// <remarks>
///     <para>
///         Rules of the emitted text: every type name is <c>global::</c>-qualified (the consumer's own types, usings and
///         aliases are unknown, and inside <c>namespace CESDK</c> the simple name <c>CESDK</c> binds to the entry-point
///         class, not to the namespace); no <see langword="unsafe" /> code, so the file compiles in a project that
///         forbids it; no dependency on the consumer's usings, nullable context or implicit usings; nothing newer than
///         C# 11 (<c>file</c> types, <c>u8</c> literals, static abstract interface members).
///     </para>
///     <para>
///         Characters: the fixed text and the display name (an escaped <c>u8</c> literal) are printable ASCII. The plugin
///         type name and the diagnostic IDs the plugin class declares are identifiers of the author's code and are written
///         as declared, so the file is ASCII-only exactly when those identifiers are; it is UTF-8 either way.
///     </para>
///     <para>
///         The bootstrap's own namespace, type name and method name are not re-spelled here as literals: they come from
///         <see cref="ManagedEntryPointNames" />, which restates the host-mandated identity
///         <c>CheatEngine.SDK.Abi.Managed.ManagedEntryPoint</c>
///         declares (that assembly is <c>net10.0</c>; this one is <c>netstandard2.0</c> and cannot reference it).
///     </para>
/// </remarks>
internal static class BootstrapEmitter
{
	/// <summary>Hint name of the emitted file: constant, because at most one file is ever produced.</summary>
	public const string HintName = "CheatEngine.SDK.EntryPoint.g.cs";

	/// <summary>Name of the file-local factory.</summary>
	internal const string FactoryName = "PluginFactory";

	/// <summary>Name of the factory when the plugin class itself occupies <c>CESDK.PluginFactory</c>.</summary>
	internal const string AlternateFactoryName = "GeneratedPluginFactory";

	private const string QualifiedFactoryName = "global::CESDK." + FactoryName;

	private const string EditorBrowsableNever =
		"[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]";

	// Constructing a plugin class that its author marked [Obsolete] must not warn in a file the author cannot edit
	// (and would fail a build that treats warnings as errors).
	private const string ObsoleteWarningsOff =
		"#pragma warning disable CS0612, CS0618 // the plugin class may be marked [Obsolete]";

	// Computed once: reads the assembly name and version of this generator.
	private static readonly string GeneratedCodeAttribute =
		GeneratedCodeText.CreateGeneratedCodeAttribute(typeof(BootstrapEmitter));

	/// <summary>Emits the bootstrap for <paramref name="model" />.</summary>
	public static SourceText Emit(BootstrapModel model)
	{
		SourceWriter writer = new(2048);
		string factoryName = ChooseFactoryName(model.FullyQualifiedTypeName);

		GeneratedCodeText.WriteFileHeader(writer);
		writer.WriteLine(ObsoleteWarningsOff);
		if (model.DeclaredDiagnosticIds.Length > 0)
		{
			// Same reason, for IDs only the plugin class knows: [Experimental("ID")] is an error by default.
			writer.Write("#pragma warning disable ");
			writer.Write(model.DeclaredDiagnosticIds);
			writer.WriteLine(" // declared by the plugin class: [Experimental] or [Obsolete(DiagnosticId = ...)]");
		}

		writer.WriteLine();
		writer.WriteLine($"namespace {ManagedEntryPointNames.Namespace}");
		writer.OpenBlock();
		WriteEntryPoint(writer, factoryName);
		writer.WriteLine();
		WriteFactory(writer, model, factoryName);
		writer.CloseBlock();

		return writer.ToSourceText();
	}

	/// <summary>
	///     <see cref="FactoryName" />, unless the plugin class is, or is nested in, a type <c>CESDK.PluginFactory</c>.
	/// </summary>
	/// <remarks>
	///     A file-local type wins name lookup in its own file, even through a <c>global::</c>-qualified name. That is
	///     what keeps an unrelated user type <c>CESDK.PluginFactory</c> harmless, and what would make
	///     <c>new global::CESDK.PluginFactory()</c> construct the factory instead of the plugin. The plugin type is the
	///     only user-chosen name in the file, so stepping aside for it is enough.
	/// </remarks>
	internal static string ChooseFactoryName(string fullyQualifiedPluginTypeName)
	{
		bool collides = fullyQualifiedPluginTypeName.StartsWith(QualifiedFactoryName, StringComparison.Ordinal)
		                && (fullyQualifiedPluginTypeName.Length == QualifiedFactoryName.Length
		                    || fullyQualifiedPluginTypeName[QualifiedFactoryName.Length] == '.');

		return collides ? AlternateFactoryName : FactoryName;
	}

	private static void WriteEntryPoint(SourceWriter writer, string factoryName)
	{
		writer.WriteLine("/// <summary>");
		writer.WriteLine(
			"/// Managed entry point of this plugin assembly. Cheat Engine looks up the type <c>CESDK.CESDK</c> and its");
		writer.WriteLine(
			"/// method <c>CEPluginInitialize</c> by name, in the plugin assembly itself: both names are imposed by the host.");
		writer.WriteLine("/// </summary>");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.WriteLine(EditorBrowsableNever);
		writer.WriteLine($"internal static class {ManagedEntryPointNames.TypeName}");
		writer.OpenBlock();
		writer.WriteLine("/// <summary>");
		writer.WriteLine(
			"/// Called by the host, more than once per load. Forwards to the hosting runtime, which is idempotent, and never");
		writer.WriteLine("/// lets an exception reach native code.");
		writer.WriteLine("/// </summary>");
		writer.WriteLine("/// <param name=\"args\">The first opaque value supplied by the host.</param>");
		writer.WriteLine(
			"/// <param name=\"opaqueArgument\">The second opaque value supplied by the host, forwarded without interpretation.</param>");
		writer.WriteLine("/// <returns>1 on success, 0 on failure.</returns>");
		writer.WriteLine(
			$"public static int {ManagedEntryPointNames.MethodName}(global::System.IntPtr args, int opaqueArgument)");
		writer.OpenBlock();
		writer.WriteLine("try");
		writer.OpenBlock();
		writer.Write("return global::CheatEngine.SDK.Hosting.Bootstrap.PluginHost.InitializeManaged<");
		writer.Write(factoryName);
		writer.WriteLine(">(args, opaqueArgument);");
		writer.CloseBlock();
		writer.WriteLine("catch (global::System.Exception)");
		writer.OpenBlock();
		writer.WriteLine("return 0;");
		writer.CloseBlock();
		writer.CloseBlock();
		writer.CloseBlock();
	}

	private static void WriteFactory(SourceWriter writer, BootstrapModel model, string factoryName)
	{
		writer.WriteLine(
			"/// <summary>Constructs the plugin class without reflection and carries its display name as UTF-8.</summary>");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.WriteLine(EditorBrowsableNever);
		writer.Write("file sealed class ");
		writer.Write(factoryName);
		writer.WriteLine(" : global::CheatEngine.SDK.Hosting.Plugin.IPluginFactory");
		writer.OpenBlock();

		writer.Write("public static global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin Create() => new ");
		writer.Write(model.FullyQualifiedTypeName);
		writer.WriteLine("();");
		writer.WriteLine();

		writer.Write("public static global::System.ReadOnlySpan<byte> Utf8Name => ");
		writer.Write(CSharpLiteral.ToUtf8Literal(model.DisplayName));
		writer.WriteLine(";");

		writer.CloseBlock();
	}
}
