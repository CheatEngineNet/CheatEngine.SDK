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
///     Unlike <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c>'s <c>Emit/LuaGlobalFileEmitter.cs</c>, this emitter writes
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

        var hasNamespace = spec.Namespace.Length > 0;
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

        foreach (var global in spec.CachedGlobals)
        {
            writer.WriteLine(GeneratedCodeAttribute);
            writer.Write("private static readonly ");
            writer.Write(LuaApiNames.LuaRef);
            writer.Write(' ');
            writer.Write(LuaGlobalCallModel.CacheFieldFor(global));
            writer.WriteLine(" = new();");
        }

        foreach (var entry in spec.Calls)
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
                writer.WriteLine(GeneratedCodeAttribute);
                LuaGlobalCallEmitter.Emit(writer, entry.Call);
            }
        }

        writer.CloseBlock();
        if (hasNamespace) writer.CloseBlock();

        return writer.ToSourceText();
    }

    // Minimal, sufficient for one line of curator-written prose: XML text content has no other special characters.
    private static string EscapeXmlText(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    // Whether 'entry' needs the two-method Address split: at least one plain (Value-shape) argument of kind
    // Address, and Address nowhere else (no CopyOut result, no Address-kind result or return). The other shapes are
    // not needed by any spec entry and are left to the ordinary path (still nuint) rather than risking an
    // incomplete hand-written parameter list for a combination this project has no test for.
    private static bool NeedsAddressTypedWrapper(LuaGlobalCallModel call)
    {
        var hasAddressArgument = false;
        foreach (var argument in call.Arguments)
            if (argument.Kind == LuaValueKind.Address)
                hasAddressArgument = true;

        if (!hasAddressArgument || call.ReturnKind == LuaValueKind.Address) return false;

        foreach (var result in call.Results)
            if (result.Kind == LuaValueKind.Address || result.Shape == LuaResultShape.CopyOut)
                return false;

        return true;
    }

    // Writes the private nuint-typed core (LuaGlobalCallEmitter's own call shape, byte-for-byte what the ordinary
    // path would emit, just private and under a mangled name) and the public Address-typed wrapper that forwards
    // to it. The core keeps every line of the protected-call body; the only added code is the two-line boundary
    // conversion, exact and allocation-free because CheatEngine.SDK.Engine.Values.Address and nuint carry the same 64-bit pattern
    // on this SDK's only supported architecture (x64).
    private static void EmitAddressTypedWrapper(SourceWriter writer, SpecCallModel entry)
    {
        var call = entry.Call;
        var core = call with { Modifiers = "private static", MethodName = CoreMethodName(call.MethodName) };

        writer.Write("// Raw core of '");
        writer.Write(call.MethodName);
        writer.WriteLine("': nuint address, through AddressMarshaller. Never called except by the wrapper below.");
        writer.WriteLine(GeneratedCodeAttribute);
        LuaGlobalCallEmitter.Emit(writer, core);
        writer.WriteLine();

        writer.Write("/// <summary>");
        writer.Write(EscapeXmlText(entry.Summary));
        writer.WriteLine("</summary>");
        writer.WriteLine(GeneratedCodeAttribute);
        writer.Write("public static ");
        writer.Write(LuaGlobalCallEmitter.ReturnTypeName(call));
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
        return "__" + methodName + "Raw";
    }

    // '(<type> name, ..., out <type> name, ...)': identical to LuaGlobalCallEmitter.WriteParameterList except an
    // Address-kind argument is typed CheatEngine.SDK.Engine.Values.Address instead of nuint (results are never Address here,
    // guaranteed by NeedsAddressTypedWrapper).
    private static void WriteAddressTypedParameterList(SourceWriter writer, LuaGlobalCallModel call)
    {
        writer.Write('(');
        var first = true;
        foreach (var argument in call.Arguments)
        {
            if (argument.IsFixed) continue;

            if (!first) writer.Write(", ");

            first = false;
            writer.Write(argument.Kind == LuaValueKind.Address
                ? EngineAddressTypeName
                : LuaValueKinds.TypeName(argument.Kind, argument.IsNullable));
            writer.Write(' ');
            writer.Write(argument.Name);
        }

        if (call.Form == LuaCallForm.Try)
            foreach (var result in call.Results)
            {
                if (!first) writer.Write(", ");

                first = false;
                writer.Write("out ");
                writer.Write(LuaValueKinds.TypeName(result.Kind, result.IsNullable));
                writer.Write(' ');
                writer.Write(result.Name);
            }

        writer.Write(')');
    }

    // 'return __coreMethod(unchecked((nuint)address.ToUInt64()), value, ..., out value, ...);' (no 'return' for a
    // void throwing wrapper): every argument forwarded by name, an Address-kind one converted at the boundary,
    // every result forwarded as the same 'out' variable the core call already writes through.
    private static void WriteAddressTypedForwardingBody(SourceWriter writer, LuaGlobalCallModel call,
        LuaGlobalCallModel core)
    {
        var isVoid = call.Form == LuaCallForm.Throwing && call.ReturnKind is null;
        if (!isVoid) writer.Write("return ");

        writer.Write(core.MethodName);
        writer.Write('(');
        var first = true;
        foreach (var argument in call.Arguments)
        {
            if (argument.IsFixed) continue;

            if (!first) writer.Write(", ");

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
            foreach (var result in call.Results)
            {
                if (!first) writer.Write(", ");

                first = false;
                writer.Write("out ");
                writer.Write(result.Name);
            }

        writer.WriteLine(");");
    }
}
