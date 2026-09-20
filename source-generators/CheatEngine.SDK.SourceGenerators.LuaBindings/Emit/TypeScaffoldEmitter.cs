using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Emit;

/// <summary>
///     Writes the frame every generated file shares: the namespace block and one <see langword="partial" /> part per
///     containing
///     type, from the outermost in, so that the members written between <see cref="Open" /> and <see cref="Close" />
///     land in the declaring type.
/// </summary>
/// <remarks>
///     A part repeats only the declaration keyword and the name: accessibility, <see langword="static" />,
///     <see langword="sealed" /> and base
///     types are taken from the author's part (a part that states nothing agrees with every other part). Block-scoped
///     namespaces keep the file valid for every consumer language version the emitted code supports.
/// </remarks>
internal static class TypeScaffoldEmitter
{
    /// <summary>Opens the namespace (when not global) and the partial parts.</summary>
    public static void Open(SourceWriter writer, ContainingTypeModel type)
    {
        if (type.Namespace.Length > 0)
        {
            writer.Write("namespace ");
            writer.WriteLine(type.Namespace);
            writer.OpenBlock();
        }

        foreach (var declaration in type.Declarations)
        {
            if (declaration.IsReadOnly) writer.Write("readonly ");

            writer.Write("partial ");
            writer.Write(declaration.Keyword);
            writer.Write(' ');
            writer.WriteLine(declaration.Name);
            writer.OpenBlock();
        }
    }

    /// <summary>Closes what <see cref="Open" /> opened.</summary>
    public static void Close(SourceWriter writer, ContainingTypeModel type)
    {
        for (var i = 0; i < type.Declarations.Length; i++) writer.CloseBlock();

        if (type.Namespace.Length > 0) writer.CloseBlock();
    }
}
