using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Emit;

/// <summary>
///     Writes the <c>&lt;Type&gt;.LuaGlobals.g.cs</c> file of one containing type: one <c>LuaRef</c> cache field per
///     distinct global, then the implementing declaration of every <c>[LuaGlobal]</c> partial method, all inside a
///     new partial part of the type.
/// </summary>
/// <remarks>
///     The cache fields are <c>private static readonly</c> and created with <c>new LuaRef()</c>, which runs no Lua
///     code, so the type's static initialisation never touches the runtime (no SDK API is usable before enable). The
///     implementing declarations repeat the defining declarations' modifiers, parameter names and types exactly (a
///     difference is CS8826, a warning the consumer may treat as an error) and carry no XML comment of their own: the
///     author's comment on the defining declaration documents the member.
/// </remarks>
internal static class LuaGlobalFileEmitter
{
    /// <summary>Suffix of the hint name: <c>Demo.Memory.LuaGlobals.g.cs</c>.</summary>
    public const string HintSuffix = LuaGlobalTableModel.HintSuffix;

    // Computed once: reads the assembly name and version of this generator.
    private static readonly string GeneratedCodeAttribute =
        GeneratedCodeText.CreateGeneratedCodeAttribute(typeof(LuaGlobalFileEmitter));

    /// <summary>
    ///     The hint name of the file for <paramref name="table" />: resolved once, across the whole pass, by
    ///     <see cref="LuaGlobalTables.Group" /> (<see cref="LuaGlobalTableModel.HintName" />), so that two types whose
    ///     names differ only in ASCII case still get distinct files.
    /// </summary>
    public static string HintName(LuaGlobalTableModel table)
    {
        return table.HintName;
    }

    /// <summary>Emits the file for <paramref name="table" />.</summary>
    public static SourceText Emit(LuaGlobalTableModel table)
    {
        SourceWriter writer = new(4096);
        GeneratedCodeText.WriteFileHeader(writer);
        TypeScaffoldEmitter.Open(writer, table.ContainingType);

        foreach (var global in table.CachedGlobals)
        {
            writer.WriteLine(GeneratedCodeAttribute);
            writer.Write("private static readonly ");
            writer.Write(LuaApiNames.LuaRef);
            writer.Write(' ');
            writer.Write(LuaGlobalCallModel.CacheFieldFor(global));
            writer.WriteLine(" = new();");
        }

        foreach (var call in table.Calls)
        {
            writer.WriteLine();
            writer.WriteLine(GeneratedCodeAttribute);
            LuaGlobalCallEmitter.Emit(writer, call);
        }

        TypeScaffoldEmitter.Close(writer, table.ContainingType);
        return writer.ToSourceText();
    }
}
