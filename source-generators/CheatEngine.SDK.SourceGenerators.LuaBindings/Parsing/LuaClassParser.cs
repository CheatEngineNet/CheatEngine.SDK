using System.Threading;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Parsing;

/// <summary>Reduces a valid <c>[LuaClass]</c> borrowed-handle declaration to its deterministic generator model.</summary>
internal static class LuaClassParser
{
    /// <summary>Builds the model, leaving malformed declarations invalid so valid siblings can still generate.</summary>
    public static LuaClassModel Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var type = (INamedTypeSymbol)context.TargetSymbol;
        var compilation = context.SemanticModel.Compilation;
        var isSdkAttribute = LuaBindingSymbols.ContainsSdkAttribute(context.Attributes, compilation,
            LuaBindingsGenerator.LuaClassAttributeMetadataName);
        var luaName = LuaBindingSymbols.ReadSdkAttributeName(context.Attributes, compilation,
            LuaBindingsGenerator.LuaClassAttributeMetadataName);
        var isValid = isSdkAttribute && LuaNames.IsValidName(luaName) && IsBorrowedHandleShape(type, cancellationToken);

        return new LuaClassModel(ContainingTypeParser.Parse(type), luaName ?? string.Empty, isValid);
    }

    /// <summary>
    ///     Whether <paramref name="type" /> can receive the generated handle surface. Kept internal so the instance
    ///     member parsers use precisely the same ownership and collision rule.
    /// </summary>
    internal static bool IsBorrowedHandleShape(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        if (type.TypeKind != TypeKind.Struct || type.IsGenericType) return false;

        if (ContainingTypeShape.Inspect(type, cancellationToken) != ContainingTypeIssues.None) return false;

        if (!IsReadOnlyStruct(type, cancellationToken)) return false;

        return !HasGeneratedIdentityCollision(type);
    }

    /// <summary>Whether the type carries the actual SDK <c>[LuaClass]</c> marker and can receive generated members.</summary>
    internal static bool IsGeneratedHandle(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        if (!IsBorrowedHandleShape(type, cancellationToken)) return false;

        foreach (var attribute in type.GetAttributes())
        {
            var attributes = System.Collections.Immutable.ImmutableArray.Create(attribute);
            if (LuaBindingSymbols.ContainsSdkAttribute(attributes, compilation,
                    LuaBindingsGenerator.LuaClassAttributeMetadataName))
                return LuaNames.IsValidName(LuaBindingSymbols.ReadSdkAttributeName(
                    attributes,
                    compilation,
                    LuaBindingsGenerator.LuaClassAttributeMetadataName));
        }

        return false;
    }

    private static bool IsReadOnlyStruct(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is StructDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
                return true;
        }

        return false;
    }

    // A generated member never silently replaces an author declaration. The analyzer explains the collision as
    // CESDK2007; the generator just drops this type and leaves independent valid types alone.
    private static bool HasGeneratedIdentityCollision(INamedTypeSymbol type)
    {
        return HasMember(type, "_handle")
               || HasMember(type, "Handle")
               || HasMember(type, "FromHandle")
               || HasMember(type, "Equals")
               || HasMember(type, "GetHashCode")
               || HasMember(type, "Push")
               || HasMember(type, "TryRead")
               || HasMember(type, "op_Equality")
               || HasMember(type, "op_Inequality");
    }

    private static bool HasMember(INamedTypeSymbol type, string name)
    {
        return type.GetMembers(name).Length != 0;
    }
}
