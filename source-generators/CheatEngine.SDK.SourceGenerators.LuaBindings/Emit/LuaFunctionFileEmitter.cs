using System;
using System.Collections.Generic;

using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Emit;

/// <summary>
///     Writes the <c>&lt;Type&gt;.LuaFunctions.g.cs</c> file of one containing type: the registration pair, then one
///     thunk per <c>[LuaFunction]</c>, all inside a new partial part of the type.
/// </summary>
/// <remarks>
///     Every member carries <c>[GeneratedCode]</c>; the two registration methods are public and documented (the
///     consumer may build with documentation diagnostics on), the thunks are private. The file needs
///     <c>AllowUnsafeBlocks</c> for the <c>&amp;Thunk</c> expressions of the registration method, which is why the
///     pipeline only reaches this emitter when the compilation allows unsafe code.
/// </remarks>
internal static class LuaFunctionFileEmitter
{
	/// <summary>Suffix of the hint name: <c>Demo.Math.LuaFunctions.g.cs</c>.</summary>
	public const string HintSuffix = LuaFunctionTableModel.HintSuffix;

	// A [LuaFunction] target may be marked [Obsolete] by its own author; calling it from a file that author cannot
	// edit must not warn (and would fail a build that treats warnings as errors). Unconditional, like the generated
	// entry point's construction of an [Obsolete] plugin class (EntryPoint's BootstrapEmitter).
	private const string ObsoleteWarningsOff =
		"#pragma warning disable CS0612, CS0618 // a [LuaFunction] target may be marked [Obsolete]";

	// Computed once: reads the assembly name and version of this generator.
	private static readonly string GeneratedCodeAttribute =
		GeneratedCodeText.CreateGeneratedCodeAttribute(typeof(LuaFunctionFileEmitter));

	private static readonly string[] IdSeparator = [", "];

	/// <summary>
	///     The hint name of the file for <paramref name="table" />: resolved once, across the whole pass, by
	///     <see cref="LuaFunctionTables.Group" /> (<see cref="LuaFunctionTableModel.HintName" />), so that two types whose
	///     names differ only in ASCII case still get distinct files.
	/// </summary>
	public static string HintName(LuaFunctionTableModel table)
	{
		return table.HintName;
	}

	/// <summary>Emits the file for <paramref name="table" />.</summary>
	public static SourceText Emit(LuaFunctionTableModel table)
	{
		SourceWriter writer = new(4096);
		GeneratedCodeText.WriteFileHeader(writer);
		writer.WriteLine(ObsoleteWarningsOff);
		WriteDeclaredDiagnosticIdsPragma(writer, table.Thunks);
		writer.WriteLine();
		TypeScaffoldEmitter.Open(writer, table.ContainingType);

		LuaRegistrationEmitter.Emit(writer, table.Thunks, GeneratedCodeAttribute);
		foreach (LuaThunkModel thunk in table.Thunks)
		{
			writer.WriteLine();
			writer.WriteLine(GeneratedCodeAttribute);
			LuaThunkEmitter.Emit(writer, thunk);
		}

		TypeScaffoldEmitter.Close(writer, table.ContainingType);
		return writer.ToSourceText();
	}

	// Same reason as ObsoleteWarningsOff, for IDs only a target (or a type it is nested in) knows: [Experimental]
	// is an error by default, and a custom [Obsolete(DiagnosticId = ...)] is not covered by CS0612/CS0618.
	private static void WriteDeclaredDiagnosticIdsPragma(SourceWriter writer, EquatableArray<LuaThunkModel> thunks)
	{
		string ids = CollectDeclaredDiagnosticIds(thunks);
		if (ids.Length == 0)
		{
			return;
		}

		writer.Write("#pragma warning disable ");
		writer.Write(ids);
		writer.WriteLine(" // declared by a [LuaFunction] target: [Experimental] or [Obsolete(DiagnosticId = ...)]");
	}

	// The union, without duplicates, of every thunk's DeclaredDiagnosticIds (itself already comma-joined).
	private static string CollectDeclaredDiagnosticIds(EquatableArray<LuaThunkModel> thunks)
	{
		List<string>? ids = null;
		foreach (LuaThunkModel thunk in thunks)
		{
			if (thunk.DeclaredDiagnosticIds.Length == 0)
			{
				continue;
			}

			foreach (string id in thunk.DeclaredDiagnosticIds.Split(IdSeparator, StringSplitOptions.None))
			{
				if (id.Length == 0)
				{
					continue;
				}

				ids ??= [];
				if (!ids.Contains(id))
				{
					ids.Add(id);
				}
			}
		}

		return ids is null ? string.Empty : string.Join(", ", ids);
	}
}
