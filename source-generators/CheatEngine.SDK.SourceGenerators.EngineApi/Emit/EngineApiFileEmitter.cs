using System.Globalization;

using CheatEngine.SDK.SourceGenerators.EngineApi.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Emit;

/// <summary>
///     Writes the generated file of one spec file: one <c>LuaRef</c> cache field per distinct global, then one complete
///     wrapper declaration (XML summary, <c>[GeneratedCode]</c>, signature and body) per entry, inside the declared
///     namespace and a single <c>public static partial class</c>.
/// </summary>
/// <remarks>
///     Unlike <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c>'s <c>Emit/LuaGlobalFileEmitter.cs</c>, this emitter
///     writes
///     <b>complete</b> declarations, never the implementing half of a partial method someone else declares: source
///     generators cannot see each other's output, so there is no author-written defining declaration to repeat
///     modifiers or parameter names from. It also opens exactly one <c>partial class</c> (no nested-type chain): the
///     spec grammar names one type per file.
/// </remarks>
internal static class EngineApiFileEmitter
{
	// The public type of a CE-side (target-process) address: the unsigned 64-bit Address wrapper, not a pointer-sized
	// nuint. CheatEngine.SDK.Lua's pointer-typed marshallers are not reused for it on the public surface. Every entry that
	// pushes one is split into a private nuint-typed core (LuaGlobalCallEmitter's unmodified call shape, through
	// AddressMarshaller) and a public forwarding wrapper typed in this.
	// See EmitAddressTypedWrapper.
	private const string EngineAddressTypeName = "global::CheatEngine.SDK.Engine.Values.Address";

	// Computed once: reads this generator assembly's name and version.
	private static readonly string GeneratedCodeAttribute =
		GeneratedCodeText.CreateGeneratedCodeAttribute(typeof(EngineApiFileEmitter));

	/// <summary>The hint name assigned to <paramref name="spec" /> by <c>Model/SpecFiles.AssignHintNames</c>.</summary>
	public static string HintName(SpecFileModel spec)
	{
		return spec.HintName;
	}

	/// <summary>
	///     Emits the file for <paramref name="spec" />. The caller must not call this for a file with zero
	///     <see cref="SpecFileModel.Calls" />.
	/// </summary>
	public static SourceText Emit(SpecFileModel spec)
	{
		SourceWriter writer = new(4096);
		GeneratedCodeText.WriteFileHeader(writer);

		bool hasNamespace = spec.Namespace.Length > 0;
		if (hasNamespace)
		{
			writer.Write("namespace ");
			writer.WriteLine(spec.Namespace);
			writer.OpenBlock();
		}

		writer.WriteLine(
			"/// <summary>Wrapper members generated from the Cheat Engine API spec file curated for this type.</summary>");
		writer.Write("public static partial class ");
		writer.WriteLine(spec.TypeName);
		writer.OpenBlock();

		foreach (string global in spec.CachedGlobals)
		{
			writer.WriteLine(GeneratedCodeAttribute);
			writer.Write("private static readonly ");
			writer.Write(LuaApiNames.LuaRef);
			writer.Write(' ');
			writer.Write(LuaGlobalCallModel.CacheFieldFor(global));
			writer.WriteLine(" = new();");
		}

		foreach (SpecCallModel entry in spec.Calls)
		{
			writer.WriteLine();
			if (NeedsAddressTypedWrapper(entry.Call))
			{
				EmitAddressTypedWrapper(writer, entry);
			}
			else
			{
				writer.Write("/// <summary>");
				writer.Write(EscapeXmlText(entry.Summary));
				writer.WriteLine("</summary>");
				WriteContractRemarks(writer, entry.Contract, entry.Call);
				writer.WriteLine(GeneratedCodeAttribute);
				LuaGlobalCallEmitter.Emit(writer, entry.Call);
			}
		}

		writer.CloseBlock();
		if (hasNamespace)
		{
			writer.CloseBlock();
		}

		return writer.ToSourceText();
	}

	// Minimal, sufficient for one line of curator-written prose: XML text content has no other special characters.
	private static string EscapeXmlText(string value)
	{
		return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
	}

	private static void WriteContractRemarks(SourceWriter writer, SpecContract? contract, LuaGlobalCallModel call)
	{
		if (contract is null)
		{
			return;
		}

		writer.Write("/// <remarks>CE &gt;= ");
		writer.Write(EscapeXmlText(contract.MinimumCheatEngineVersion));
		writer.Write("; architecture: ");
		writer.Write(EscapeXmlText(contract.Architecture));
		writer.Write("; thread: ");
		writer.Write(EscapeXmlText(contract.ThreadAffinity));
		writer.Write("; ownership: ");
		writer.Write(EscapeXmlText(contract.Ownership));
		writer.Write("; return: ");
		writer.Write(EscapeXmlText(ReturnSemantics(call)));
		writer.Write("; nil: ");
		writer.Write(EscapeXmlText(contract.NilSemantics));
		writer.Write("; provenance: ");
		writer.Write(EscapeXmlText(contract.Provenance));
		writer.WriteLine(".</remarks>");
	}

	private static string ReturnSemantics(LuaGlobalCallModel call)
	{
		if (call.Form == LuaCallForm.Try)
		{
			return "bool with out results";
		}

		if (call.ReturnKind is null)
		{
			return "void or Lua exception";
		}

		return "throwing " + LuaValueKinds.TypeName(call.ReturnKind.Value, call.ReturnIsNullable);
	}

	// Every target-process Address crosses this public boundary as Engine.Values.Address. The shared call emitter keeps
	// its proven host-width nuint core, while this facade converts every argument, Try out value and throwing return;
	// a new EngineApi spec can therefore never silently expose an address as an ambiguous nuint.
	private static bool NeedsAddressTypedWrapper(LuaGlobalCallModel call)
	{
		foreach (LuaArgumentModel argument in call.Arguments)
		{
			if (argument.Kind == LuaValueKind.Address)
			{
				return true;
			}
		}

		foreach (LuaResultModel result in call.Results)
		{
			if (result.Kind == LuaValueKind.Address)
			{
				return true;
			}
		}

		return call.ReturnKind == LuaValueKind.Address;
	}

	// Writes the private nuint-typed core (LuaGlobalCallEmitter's own call shape, byte-for-byte what the ordinary
	// path would emit, just private and under a mangled name) and the public Address-typed wrapper that forwards
	// to it. The core keeps every line of the protected-call body; the only added code is the two-line boundary
	// conversion, exact and allocation-free because CheatEngine.SDK.Engine.Values.Address and nuint carry the same 64-bit pattern
	// on this SDK's only supported architecture (x64).
	private static void EmitAddressTypedWrapper(SourceWriter writer, SpecCallModel entry)
	{
		LuaGlobalCallModel call = entry.Call;
		LuaGlobalCallModel core = call with
		{
			Modifiers = "private static", MethodName = CoreMethodName(call.MethodName)
		};

		writer.Write("// Raw core of '");
		writer.Write(call.MethodName);
		writer.WriteLine("': nuint address, through AddressMarshaller. Never called except by the wrapper below.");
		writer.WriteLine(GeneratedCodeAttribute);
		LuaGlobalCallEmitter.Emit(writer, core);
		writer.WriteLine();

		writer.Write("/// <summary>");
		writer.Write(EscapeXmlText(entry.Summary));
		writer.WriteLine("</summary>");
		WriteContractRemarks(writer, entry.Contract, call);
		writer.WriteLine(GeneratedCodeAttribute);
		writer.Write("public static ");
		writer.Write(PublicReturnTypeName(call));
		writer.Write(' ');
		writer.Write(call.MethodName);
		WriteAddressTypedParameterList(writer, call);
		writer.WriteLine();
		writer.OpenBlock();
		WriteAddressTypedForwardingBody(writer, call, core);
		writer.CloseBlock();
	}

	// '__<method>Raw': a leading double underscore is not a reserved C# identifier form, the same convention
	// LuaGlobalCallEmitter's own body locals use for the same reason: a collision is extremely unlikely, not
	// impossible, and fails loudly (a collision with a curated 'method:' name is CS0111 in the generated file, not
	// a silent miscompile).
	private static string CoreMethodName(string methodName)
	{
		return "__" + (methodName[0] == '@' ? methodName[1..] : methodName) + "Raw";
	}

	// '(<type> name, ..., out <type> name, ...)': identical to LuaGlobalCallEmitter.WriteParameterList except every
	// target Address is typed CheatEngine.SDK.Engine.Values.Address instead of the raw nuint used in the private core.
	private static void WriteAddressTypedParameterList(SourceWriter writer, LuaGlobalCallModel call)
	{
		writer.Write('(');
		bool first = true;
		foreach (LuaArgumentModel argument in call.Arguments)
		{
			if (argument.IsFixed)
			{
				continue;
			}

			if (!first)
			{
				writer.Write(", ");
			}

			first = false;
			writer.Write(argument.Kind == LuaValueKind.Address
				? EngineAddressTypeName
				: LuaValueKinds.TypeName(argument.Kind, argument.IsNullable));
			writer.Write(' ');
			writer.Write(argument.Name);
		}

		if (call.Form == LuaCallForm.Try)
		{
			for (int i = 0; i < call.Results.Length; i++)
			{
				LuaResultModel result = call.Results[i];
				if (!first)
				{
					writer.Write(", ");
				}

				first = false;
				writer.Write("out ");
				writer.Write(result.Kind == LuaValueKind.Address
					? EngineAddressTypeName
					: LuaValueKinds.TypeName(result.Kind, result.IsNullable));
				writer.Write(' ');
				writer.Write(result.Name);
			}
		}

		writer.Write(')');
	}

	// The raw core can write nuint only. Try wrappers with Address out values receive raw locals then convert every
	// result (also on false, where the raw core deterministically assigned zero); throwing returns convert the raw
	// value after the protected call succeeded. The simple argument-only forms retain their allocation-free direct
	// forwarding body.
	private static void WriteAddressTypedForwardingBody(SourceWriter writer, LuaGlobalCallModel call,
		LuaGlobalCallModel core)
	{
		if (call.Form == LuaCallForm.Try && HasAddressResult(call))
		{
			for (int i = 0; i < call.Results.Length; i++)
			{
				if (call.Results[i].Kind == LuaValueKind.Address)
				{
					writer.Write("nuint ");
					writer.Write(RawResultName(i));
					writer.WriteLine(";");
				}
			}

			writer.Write("bool __engineApiSucceeded = ");
			WriteAddressTypedInvocation(writer, call, core, true);
			writer.WriteLine(";");

			for (int i = 0; i < call.Results.Length; i++)
			{
				if (call.Results[i].Kind == LuaValueKind.Address)
				{
					writer.Write(call.Results[i].Name);
					writer.Write(" = new ");
					writer.Write(EngineAddressTypeName);
					writer.Write("(unchecked((ulong)");
					writer.Write(RawResultName(i));
					writer.WriteLine("));");
				}
			}

			writer.WriteLine("return __engineApiSucceeded;");
			return;
		}

		if (call.Form == LuaCallForm.Throwing && call.ReturnKind == LuaValueKind.Address)
		{
			writer.Write("nuint __engineApiRawResult = ");
			WriteAddressTypedInvocation(writer, call, core, false);
			writer.WriteLine(";");
			writer.Write("return new ");
			writer.Write(EngineAddressTypeName);
			writer.WriteLine("(unchecked((ulong)__engineApiRawResult));");
			return;
		}

		bool isVoid = call.Form == LuaCallForm.Throwing && call.ReturnKind is null;
		if (!isVoid)
		{
			writer.Write("return ");
		}

		WriteAddressTypedInvocation(writer, call, core, false);
		writer.WriteLine(";");
	}

	private static void WriteAddressTypedInvocation(SourceWriter writer, LuaGlobalCallModel call,
		LuaGlobalCallModel core, bool useRawAddressResults)
	{
		writer.Write(core.MethodName);
		writer.Write('(');
		bool first = true;
		foreach (LuaArgumentModel argument in call.Arguments)
		{
			if (argument.IsFixed)
			{
				continue;
			}

			if (!first)
			{
				writer.Write(", ");
			}

			first = false;
			if (argument.Kind == LuaValueKind.Address)
			{
				writer.Write("unchecked((nuint)");
				writer.Write(argument.Name);
				writer.Write(".ToUInt64())");
			}
			else
			{
				writer.Write(argument.Name);
			}
		}

		if (call.Form == LuaCallForm.Try)
		{
			for (int i = 0; i < call.Results.Length; i++)
			{
				LuaResultModel result = call.Results[i];
				if (!first)
				{
					writer.Write(", ");
				}

				first = false;
				writer.Write("out ");
				writer.Write(useRawAddressResults && result.Kind == LuaValueKind.Address
					? RawResultName(i)
					: result.Name);
			}
		}

		writer.Write(')');
	}

	private static bool HasAddressResult(LuaGlobalCallModel call)
	{
		foreach (LuaResultModel result in call.Results)
		{
			if (result.Kind == LuaValueKind.Address)
			{
				return true;
			}
		}

		return false;
	}

	private static string PublicReturnTypeName(LuaGlobalCallModel call)
	{
		return call.Form == LuaCallForm.Throwing && call.ReturnKind == LuaValueKind.Address
			? EngineAddressTypeName
			: LuaGlobalCallEmitter.ReturnTypeName(call);
	}

	private static string RawResultName(int index)
	{
		return "__engineApiRawResult" + index.ToString(CultureInfo.InvariantCulture);
	}
}
